//
// ReuseBitmapDrawableCache.cs
//
// Author:
//   Brett Duncavage <brett.duncavage@rd.io>
//
// Copyright 2013 Rdio, Inc.
//

using System.Linq;
using System;
using Rdio.TangoAndCache.Collections;
using Rdio.TangoAndCache.Android.UI.Drawables;
using Android.Graphics.Drawables;
using Android.OS;
using Android.Graphics;
using Android.App;
using System.Collections.Generic;
using Android.Content;
using Rdio.TangoAndCache.Android.Util;
using Android.Util;

namespace Rdio.TangoAndCache.Android.Collections
{
    public class ReuseBitmapDrawableCache : IDictionary<Uri, SelfDisposingBitmapDrawable>
    {
        private const string TAG = "ReuseBitmapDrawableCache";

        private int total_added;
        private int total_removed;
        private int total_reuse_hits;
        private int total_reuse_misses;
        private int total_evictions;
        private int total_cache_hits;
        private int total_forced_gc_collections;
        private long current_cache_byte_count;
        private long current_evicted_byte_count;

        private readonly object monitor = new object();

        private readonly long high_watermark;
        private readonly long low_watermark;
        private readonly long gc_threshold;
        private bool reuse_pool_refill_needed = true;

        /// <summary>
        /// Contains all entries that are currently being displayed. These entries are not eligible for
        /// reuse or eviction. Entries will be added to the reuse pool when they are no longer displayed.
        /// </summary>
        private IDictionary<Uri, SelfDisposingBitmapDrawable> displayed_cache;
        /// <summary>
        /// Contains entries that potentially available for reuse and candidates for eviction.
        /// This is the default location for newly added entries. This cache
        /// is searched along with the displayed cache for cache hits. If a cache hit is found here, its
        /// place in the LRU list will be refreshed. Items only move out of reuse and into displayed
        /// when the entry has SetIsDisplayed(true) called on it.
        /// </summary>
        private ByteBoundStrongLruCache<Uri, SelfDisposingBitmapDrawable> reuse_pool;

        private TimeSpan debug_dump_interval = TimeSpan.FromSeconds(10);
        private Handler main_thread_handler;

        /// <summary>
        /// Initializes a new instance of the <see cref="Pulser.Android.Collections.AndroidBitmapDrawableCache"/> class.
        /// </summary>
        /// <param name="highWatermark">Maximum number of bytes the reuse pool will hold before starting evictions.
        /// <param name="lowWatermark">Number of bytes the reuse pool will be drained down to after the high watermark is exceeded.</param> 
        /// On Honeycomb and higher this value is used for the reuse pool size.</param>
        /// <param name="gcThreshold">Threshold in bytes that triggers a System.GC.Collect (Honeycomb+ only).</param>
        /// <param name="debugDump">If set to <c>true</c> dump stats to log every 10 seconds.</param>
        public ReuseBitmapDrawableCache(long highWatermark, long lowWatermark, long gcThreshold = 2 * 1024 * 1024, bool debugDump = false)
        {
            low_watermark = lowWatermark;
            high_watermark = highWatermark;

            gc_threshold = gcThreshold;
            displayed_cache = new Dictionary<Uri, SelfDisposingBitmapDrawable>();
            reuse_pool = new ByteBoundStrongLruCache<Uri, SelfDisposingBitmapDrawable>(highWatermark, lowWatermark);
            reuse_pool.EntryRemoved += OnEntryRemovedFromReusePool;

            if (debugDump) {
                main_thread_handler = new Handler();
                DebugDumpStats();
            }
        }

        /// <summary>
        /// Attempts to find a bitmap suitable for reuse based on the given dimensions.
        /// Note that any returned instance will have SetIsRetained(true) called on it
        /// to ensure that it does not release its resources prematurely as it is leaving
        /// cache management. This means you must call SetIsRetained(false) when you no
        /// longer need the instance.
        /// </summary>
        /// <returns>A SelfDisposingBitmapDrawable that has been retained. You must call SetIsRetained(false)
        /// when finished using it.</returns>
        /// <param name="width">Width of the image to be written to the bitmap allocation.</param>
        /// <param name="height">Height of the image to be written to the bitmap allocation.</param>
        public SelfDisposingBitmapDrawable GetReusableBitmapDrawable(int width, int height)
        {
            if (reuse_pool == null) return null;

            // Only attempt to get a bitmap for reuse if the reuse cache is full.
            // This prevents us from prematurely depleting the pool and allows
            // more cache hits, as the most recently added entries will have a high
            // likelihood of being accessed again so we don't want to steal those bytes too soon.
            lock (monitor) {
                if (reuse_pool.CacheSizeInBytes < low_watermark && reuse_pool_refill_needed) {
                    Log.Debug(TAG, "Reuse pool is not full, refusing reuse request");
                    total_reuse_misses++;
                    return null;
                }
                reuse_pool_refill_needed = false;

                SelfDisposingBitmapDrawable reuseDrawable = null;

                if (reuse_pool.Count > 0) {
                    var reuse_keys = reuse_pool.Keys;
                    foreach (var k in reuse_keys) {
                        var bd = reuse_pool.Peek(k);
                        // TODO: Implement check for KitKat and higher since
                        // bitmaps that are smaller than the requested size can be used.
                        if (Build.VERSION.SdkInt >= BuildVersionCodes.Kitkat) {
                            // We can reuse a bitmap if the allocation to be reused is larger
                            // than the requested bitmap.
                            if (bd.Bitmap.Width >= width && bd.Bitmap.Height >= height && !bd.IsRetained) {
                                reuseDrawable = bd;
                                Log.Debug(TAG, "Reuse hit! Using larger allocation.");
                                break;
                            }
                        } else if (bd.Bitmap.Width == width &&
                            bd.Bitmap.Height == height &&
                            bd.Bitmap.IsMutable &&
                            !bd.IsRetained) {

                            reuseDrawable = bd;

                            Log.Debug(TAG, "Reuse hit!");
                            break;
                        }
                    }
                    if (reuseDrawable != null) {
                        reuseDrawable.SetIsRetained(true);

                        UpdateByteUsage(reuseDrawable.Bitmap, decrement:true, causedByEviction: true);

                        // Cleanup the entry
                        reuseDrawable.Displayed -= OnEntryDisplayed;
                        reuseDrawable.NoLongerDisplayed -= OnEntryNoLongerDisplayed;
                        reuseDrawable.SetIsCached(false);

                        reuse_pool.Remove(reuseDrawable.InCacheKey);
                        total_reuse_hits++;
                    }
                }
                if (reuseDrawable == null) {
                    total_reuse_misses++;
                    // Indicate that the pool may need to be refilled.
                    // There is little harm in setting this flag since it will be unset
                    // on the next reuse request if the threshold is reuse_pool.CacheSizeInBytes >= low_watermark.
                    reuse_pool_refill_needed = true;
                }
                return reuseDrawable;
            }
        }

        private void UpdateByteUsage(Bitmap bitmap, bool decrement = false, bool causedByEviction = false)
        {
            lock(monitor) {
                var byteCount = bitmap.RowBytes * bitmap.Height;
                current_cache_byte_count += byteCount * (decrement ? -1 : 1);
                if (causedByEviction) {
                    current_evicted_byte_count += byteCount;
                    // Kick the gc if we've accrued more than our desired threshold.
                    // TODO: Implement high/low watermarks to prevent thrashing
                    if (current_evicted_byte_count > gc_threshold) {
                        total_forced_gc_collections++;
                        Log.Debug(TAG, "Memory usage exceeds threshold, invoking GC.Collect");
                        System.GC.Collect();
                        current_evicted_byte_count = 0;
                    }
                }
            }
        }

        private void OnEntryRemovedFromReusePool (object sender, EntryRemovedEventArgs<Uri, SelfDisposingBitmapDrawable> e)
        {
            ProcessRemoval(e.OldValue, e.Evicted);
        }

        private void ProcessRemoval(SelfDisposingBitmapDrawable value, bool evicted)
        {
            lock(monitor) {
                total_removed++;
                if (evicted) {
                    Log.Debug(TAG, "Evicted key: {0}", value.InCacheKey);
                    total_evictions++;
                }
            }

            // We only really care about evictions because we do direct Remove()als
            // all the time when promoting to the displayed_cache. Only when the
            // entry has been evicted is it truly not longer being held by us.
            if (evicted) {
                UpdateByteUsage(value.Bitmap, decrement: true, causedByEviction: true);

                value.SetIsCached(false);
                value.Displayed -= OnEntryDisplayed;
                value.NoLongerDisplayed -= OnEntryNoLongerDisplayed;
            }
        }

        private void OnEntryNoLongerDisplayed(object sender, EventArgs args)
        {
            if (!(sender is SelfDisposingBitmapDrawable)) return;

            var sdbd = (SelfDisposingBitmapDrawable)sender;
            lock (monitor) {
                if (displayed_cache.ContainsKey(sdbd.InCacheKey)) {
                    DemoteDisplayedEntryToReusePool(sdbd);
                }
            }
        }

        private void OnEntryDisplayed(object sender, EventArgs args)
        {
            if (!(sender is SelfDisposingBitmapDrawable)) return;

            // see if the sender is in the reuse pool and move it
            // into the displayed_cache if found.
            var sdbd = (SelfDisposingBitmapDrawable)sender;
            lock (monitor) {
                if (reuse_pool.ContainsKey(sdbd.InCacheKey)) {
                    PromoteReuseEntryToDisplayedCache(sdbd);
                }
            }
        }

        private void OnEntryAdded(Uri key, BitmapDrawable value)
        {
            total_added++;
            Log.Debug(TAG, "OnEntryAdded(key = {0})", key);
            if (value is SelfDisposingBitmapDrawable) {
                var sdbd = (SelfDisposingBitmapDrawable)value;
                sdbd.SetIsCached(true);
                sdbd.InCacheKey = key;
                sdbd.Displayed += OnEntryDisplayed;
                UpdateByteUsage(sdbd.Bitmap);
            }
        }

        private void PromoteReuseEntryToDisplayedCache(SelfDisposingBitmapDrawable value)
        {
            value.Displayed -= OnEntryDisplayed;
            value.NoLongerDisplayed += OnEntryNoLongerDisplayed;
            reuse_pool.Remove(value.InCacheKey);
            displayed_cache.Add(value.InCacheKey, value);
        }

        private void DemoteDisplayedEntryToReusePool(SelfDisposingBitmapDrawable value)
        {
            value.NoLongerDisplayed -= OnEntryNoLongerDisplayed;
            value.Displayed += OnEntryDisplayed;
            displayed_cache.Remove(value.InCacheKey);
            reuse_pool.Add(value.InCacheKey, value);
        }

        #region IDictionary implementation

        public void Add(Uri key, SelfDisposingBitmapDrawable value)
        {
            if (value == null) {
                Log.Warn(TAG, "Attempt to add null value, refusing to cache");
                return;
            }

            if (value.Bitmap == null) {
                Log.Warn(TAG, "Attempt to add Drawable with null bitmap, refusing to cache");
                return;
            }

            lock (monitor) {
                if (!displayed_cache.ContainsKey(key) && !reuse_pool.ContainsKey(key)) {
                    reuse_pool.Add(key, (SelfDisposingBitmapDrawable)value);
                    OnEntryAdded(key, value);
                }
            }
        }

        public bool ContainsKey(Uri key)
        {
            lock (monitor) {
                return displayed_cache.ContainsKey(key) || reuse_pool.ContainsKey(key);
            }
        }

        public bool Remove(Uri key)
        {
            SelfDisposingBitmapDrawable tmp = null;
            SelfDisposingBitmapDrawable reuseTmp = null;
            var result = false;
            lock (monitor) {
                if (displayed_cache.TryGetValue(key, out tmp)) {
                    result = displayed_cache.Remove(key);
                } else if (reuse_pool.TryGetValue(key, out reuseTmp)) {
                    result = reuse_pool.Remove(key);
                }
                if (tmp is SelfDisposingBitmapDrawable) {
                    ProcessRemoval((SelfDisposingBitmapDrawable)tmp, evicted: true);
                }
                if (reuseTmp is SelfDisposingBitmapDrawable) {
                    ProcessRemoval(reuseTmp, evicted: true);
                }
                return result;
            }
        }

        public bool TryGetValue(Uri key, out SelfDisposingBitmapDrawable value)
        {
            lock (monitor) {
                var result = displayed_cache.TryGetValue(key, out value);
                if (result) {
                    total_cache_hits++;
                    Log.Debug(TAG, "Cache hit");
                } else {

                    SelfDisposingBitmapDrawable tmp = null;
                    result = reuse_pool.TryGetValue(key, out tmp); // If key is found, its place in the LRU is refreshed
                    if (result) {
                        Log.Debug(TAG, "Cache hit from reuse pool");
                        total_cache_hits++;
                    }
                    value = tmp;
                }
                return result;
            }
        }

        public SelfDisposingBitmapDrawable this[Uri index] {
            get {
                lock (monitor) {
                    SelfDisposingBitmapDrawable tmp = null;
                    TryGetValue(index, out tmp);
                    return tmp;
                }
            }
            set {
                Add(index, value);
            }
        }

        public ICollection<Uri> Keys {
            get {
                lock (monitor) {
                    var cacheKeys = displayed_cache.Keys;
                    var allKeys = new List<Uri>(cacheKeys);
                    allKeys.AddRange(reuse_pool.Keys);
                    return allKeys;
                }
            }
        }

        public ICollection<SelfDisposingBitmapDrawable> Values {
            get {
                lock (monitor) {
                    var cacheValues = displayed_cache.Values;
                    var allValues = new List<SelfDisposingBitmapDrawable>(cacheValues);
                    allValues.AddRange(reuse_pool.Values);
                    return allValues;
                }
            }
        }

        #endregion

        #region ICollection implementation

        public void Add(KeyValuePair<Uri, SelfDisposingBitmapDrawable> item)
        {
            Add(item.Key, item.Value);
        }

        public void Clear()
        {
            lock (monitor) {
                foreach (var k in displayed_cache.Keys) {
                    var tmp = displayed_cache[k];
                    if (tmp is SelfDisposingBitmapDrawable) {
                        ProcessRemoval((SelfDisposingBitmapDrawable)tmp , evicted: true);
                    }
                }
                displayed_cache.Clear();

                foreach (var k in reuse_pool.Keys) {
                    ProcessRemoval(reuse_pool[k], evicted: true);
                }
                reuse_pool.Clear();
            }
        }

        public bool Contains(KeyValuePair<Uri, SelfDisposingBitmapDrawable> item)
        {
            return ContainsKey(item.Key);
        }

        public void CopyTo(KeyValuePair<Uri, SelfDisposingBitmapDrawable>[] array, int arrayIndex)
        {
            throw new NotImplementedException("CopyTo is not supported");
        }

        public bool Remove(KeyValuePair<Uri, SelfDisposingBitmapDrawable> item)
        {
            return Remove(item.Key);
        }

        public int Count {
            get {
                lock (monitor) {
                    return displayed_cache.Count + reuse_pool.Count;
                }
            }
        }

        public bool IsReadOnly {
            get {
                lock (monitor) {
                    return displayed_cache.IsReadOnly;
                }
            }
        }

        #endregion

        #region IEnumerable implementation

        public IEnumerator<KeyValuePair<Uri, SelfDisposingBitmapDrawable>> GetEnumerator()
        {
            List<KeyValuePair<Uri, SelfDisposingBitmapDrawable>> values;
            lock (monitor) {
                values = new List<KeyValuePair<Uri, SelfDisposingBitmapDrawable>>(Count);
                foreach (var k in Keys) {
                    values.Add(new KeyValuePair<Uri, SelfDisposingBitmapDrawable>(k, this[k]));
                }
            }
            foreach (var kvp in values) {
                yield return kvp;
            }
        }

        #endregion

        #region IEnumerable implementation

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        #endregion

        private void DebugDumpStats()
        {
            main_thread_handler.PostDelayed(DebugDumpStats, (long)debug_dump_interval.TotalMilliseconds);

            lock (monitor) {
                Log.Debug(TAG, "--------------------");
                Log.Debug(TAG, "current total count: " + Count);
                Log.Debug(TAG, "cumulative additions: " + total_added);
                Log.Debug(TAG, "cumulative removals: " + total_removed);
                Log.Debug(TAG, "total evictions: " + total_evictions);
                Log.Debug(TAG, "total cache hits: " + total_cache_hits);
                Log.Debug(TAG, "reuse hits: " + total_reuse_hits);
                Log.Debug(TAG, "reuse misses: " + total_reuse_misses);
                Log.Debug(TAG, "reuse pool count: " + reuse_pool.Count);
                Log.Debug(TAG, "gc threshlold:         " + gc_threshold);
                Log.Debug(TAG, "cache size in bytes:   " + current_cache_byte_count);
                Log.Debug(TAG, "reuse pool in bytes:   " + reuse_pool.CacheSizeInBytes);
                Log.Debug(TAG, "current evicted bytes: " + current_evicted_byte_count);
                Log.Debug(TAG, "high watermark:        " + high_watermark);
                Log.Debug(TAG, "low watermark:         " + low_watermark);
                Log.Debug(TAG, "total force gc collections: " + total_forced_gc_collections);
                if (total_reuse_hits > 0 || total_reuse_misses > 0) {
                    Log.Debug(TAG, "reuse hit %: " + (100f * (total_reuse_hits / (float)(total_reuse_hits + total_reuse_misses))));
                }
            }
        }
    }
}

