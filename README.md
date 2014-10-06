Tango And Cache
=======

C# in-memory Bitmap cache for Android

Details
=======

TangoAndCache is an in-memory cache for bitmaps. At a basic level it is a byte-bound LRU cache, evicting the oldest entries as new ones
push the cache over the high watermark. It facilitates Bitmap reuse to reduce the number of allocations made and monitors the count of
bytes evicted from the cache in order to force GC cycles to prevent OOMs on the native side.

TangoAndCache is not an image downloader or disk cache (maybe in the future, that will be the Tango part).

Usage
=====

Usage of TangoAndCache is very straightforward. You simply create an instance of the cache, then add entries and retrieve them.

Example:
```csharp
// Create an instance.
// The high, low and gc threshold values are up to you to provide. Here is a rundown of the values:
// The GC threshold is the amount of bytes that have been evicted from the cache
// that will trigger a GC.Collect. For example if set to 2mb, a GC will be performed
// each time the cache has evicted a total of 2mb.
// This means that we can have highWatermark + gcThreshold amount of memory in use
// before a GC is forced, so we should ensure that the threshold value + hightWatermark
// is less than our total memory.
// In our case, the highWatermark is 1/3 of max memory, so using the same value for the
// GC threshold means we can have up to 2/3 of max memory in use before kicking the GC.
image_cache = new ReuseBitmapDrawableCache(highWatermark, lowWatermark, gcThreshold);

// OR

// If you need to support < API 11 (Honeycomb) create an instance of the BitmapDrawableCache.
if (Utils.HasHoneycomb) {
   image_cache = new ReuseBitmapDrawableCache(highWatermark, lowWatermark, gcThreshold);
} else {
   image_cache = new BitmapDrawableCache(highWatermark, lowWatermark, gcThreshold);
}

// Add images to the cache (in your downloader or wherever you're getting images)
image_cache.Add(new Uri(uri), new SelfDisposingBitmapDrawable(Resources, bitmap));

// Get images out of the cache
var drawable = image_cache[key];
```

You can create the cache instance with dumpDebug = true which causes the cache to periodically log stats about the cache. Stats include:
current size in bytes, current count, number of cache hits, number of cache misses, etc.

Choosing high and low watermarks is up to you as the values may vary depending on your application domain and how much room you want to
give the cache. You can also experiment with the gc threshold value to control how often forced GCs are performed.

Sample
======

The TangoAndCache solution contains a sample Android application that demonstrates trivial usage of the cache.

Future Improvements
==================

Making TangoAndCache an image downloader and disk LRU cache. Adding that functionality would make TangoAndCache a fully featured image providing
solution for C# Android apps.

Better debug stats and logging may be useful and desireable.

License
=======

The MIT License (MIT)

Copyright (c) <year> <copyright holders>

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in
all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
THE SOFTWARE.
