//
// BitmapDrawableCache.cs
//
// Author:
//   Brett Duncavage <brett.duncavage@rd.io>
//
// Copyright 2013 Rdio, Inc.
//

using System;
using System.Collections.Generic;
using Android.Graphics.Drawables;
using Rdio.TangoAndCache.Collections;
using Rdio.TangoAndCache.Android.UI.Drawables;
using Android.Graphics;
using Android.Util;
using Android.OS;

namespace Rdio.TangoAndCache.Android.Collections
{
    public class BitmapDrawableCache : IDictionary<Uri, SelfDisposingBitmapDrawable>
    {
        private const string TAG = "BitmapDrawableCache";

        private int total_added;
        private int total_removed;
        private int total_evictions;
        private int total_cache_hits;
        private long current_cache_byte_count;

        private readonly object monitor = new object();

        private IDictionary<Uri, SelfDisposingBitmapDrawable> displayed_cache;

        private TimeSpan debug_dump_interval = TimeSpan.FromSeconds(10);
        private Handler main_thread_handler;

        /// <summary>
        /// Initializes a new instance of the <see cref="Pulser.Android.Collections.ReuseBitmapDrawableCache"/> class.
        /// </summary>
        /// <param name="highWatermark">Maximum size of cache in bytes before evictions start.</param>
        /// <param name="lowWatermark">Size in bytes to drain the cache down to after the high watermark has been exceeded.</param>
        /// <param name="debugDump">If set to <c>true</c> dump stats to log every 10 seconds.</param>
        public BitmapDrawableCache(long highWatermark, long lowWatermark, bool debugDump = false)
        {
            var lruCache = new ByteBoundStrongLruCache<Uri, SelfDisposingBitmapDrawable>(highWatermark, lowWatermark);
            lruCache.EntryRemoved += OnLruEviction;
            displayed_cache = lruCache;

            if (debugDump) {
                main_thread_handler = new Handler();
                DebugDumpStats();
            }
        }

        private void UpdateByteUsage(Bitmap bitmap, bool decrement = false, bool causedByEviction = false)
        {
            lock(monitor) {
                var byteCount = bitmap.RowBytes * bitmap.Height;
                current_cache_byte_count += byteCount * (decrement ? -1 : 1);
            }
        }

        private void OnLruEviction(object sender, EntryRemovedEventArgs<Uri, SelfDisposingBitmapDrawable> e)
        {
            if (e.OldValue is SelfDisposingBitmapDrawable) {
                ProcessEviction((SelfDisposingBitmapDrawable)e.OldValue, e.Evicted);
            }
        }

        private void ProcessEviction(SelfDisposingBitmapDrawable value, bool evicted)
        {
            if (evicted) {
                lock (monitor) {
                    total_evictions++;
                    total_removed++;
                }
                UpdateByteUsage(value.Bitmap, decrement:true, causedByEviction:true);
                value.SetIsCached(false);
                value.Displayed -= OnEntryDisplayed;
            } else {
                lock (monitor) {
                    total_removed++;
                }
            }
        }

        private void OnEntryDisplayed(object sender, EventArgs args)
        {
            if (!(sender is SelfDisposingBitmapDrawable)) return;

            // see if the sender is in the reuse pool and move it
            // into the internal_cache if found.
            lock (monitor) {
                var sdbd = (SelfDisposingBitmapDrawable)sender;
                // Adding a key that already exists refreshes the item's
                // position in the LRU list.
                displayed_cache.Add(sdbd.InCacheKey, sdbd);
            }
        }

        private void OnEntryAdded(Uri key, SelfDisposingBitmapDrawable value)
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
                if (!displayed_cache.ContainsKey(key)) {
                    displayed_cache.Add(key, value);
                    OnEntryAdded(key, value);
                }
            }
        }

        public bool ContainsKey(Uri key)
        {
            lock (monitor) {
                return displayed_cache.ContainsKey(key);
            }
        }

        public bool Remove(Uri key)
        {
            SelfDisposingBitmapDrawable tmp = null;
            var result = false;
            lock (monitor) {
                if (displayed_cache.TryGetValue(key, out tmp)) {
                    result = displayed_cache.Remove(key);
                }
                if (tmp is SelfDisposingBitmapDrawable) {
                    ProcessEviction((SelfDisposingBitmapDrawable)tmp, evicted: true);
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
                    return displayed_cache.Keys;
                }
            }
        }

        public ICollection<SelfDisposingBitmapDrawable> Values {
            get {
                lock (monitor) {
                    return displayed_cache.Values;
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
                        ProcessEviction((SelfDisposingBitmapDrawable)tmp , evicted: true);
                    }
                }
                displayed_cache.Clear();
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
                    return displayed_cache.Count;
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
                Log.Debug(TAG, "cache size in bytes: " + current_cache_byte_count);
            }
        }
    }
}
