using System;

using Android.App;
using Android.Content;
using Android.Runtime;
using Android.Views;
using Android.Widget;
using Android.OS;
using System.Collections.Generic;
using Rdio.TangoAndCache.Android.Collections;
using Android.Graphics;
using Rdio.TangoAndCache.Android.UI.Drawables;
using Rdio.TangoAndCache.Android.Widget;
using System.Threading;
using System.Net;

namespace Sample
{
    [Activity(Label = "@string/app_name", MainLauncher = true, Icon = "@drawable/icon")]
    public class MainActivity : Activity
    {
        GridView grid_view;
        ReuseBitmapDrawableCache image_cache;
        readonly Handler main_thread_handler = new Handler();

        readonly List<string> images_to_fetch = new List<string>{
            "http://rdio3img-a.akamaihd.net/album/8/c/7/00000000004987c8/1/square-400.jpg",
            "http://img00.cdn2-rdio.com/album/4/5/9/000000000021f954/4/square-400.jpg",
            "http://img02.cdn2-rdio.com/album/f/2/a/000000000049aa2f/1/square-400.jpg",
            "http://img00.cdn2-rdio.com/album/4/f/3/00000000004a33f4/1/square-400.jpg",
            "http://rdio1img-a.akamaihd.net/album/0/e/c/0000000000498ce0/3/square-400.jpg",
            "http://img02.cdn2-rdio.com/album/2/7/e/00000000004aee72/1/square-400.jpg",
            "http://img00.cdn2-rdio.com/album/3/c/0/00000000004b20c3/1/square-400.jpg",
            "http://img02.cdn2-rdio.com/album/5/c/8/000000000047a8c5/3/square-400.jpg",
            "http://rdio1img-a.akamaihd.net/album/f/7/e/0000000000487e7f/3/square-400.jpg",
            "http://rdio3img-a.akamaihd.net/album/2/c/9/000000000037a9c2/1/square-400.jpg",
            "http://img00.cdn2-rdio.com/album/f/f/a/0000000000479aff/5/square-400.jpg",
            "http://img02.cdn2-rdio.com/album/9/d/e/0000000000493ed9/1/square-400.jpg",
            "http://rdio3img-a.akamaihd.net/album/1/4/3/0000000000010341/square-400.jpg",
            "http://rdio1img-a.akamaihd.net/album/6/f/1/00000000000911f6/4/square-400.jpg",
            "http://img00.cdn2-rdio.com/album/e/6/4/00000000004bb46e/1/square-400.jpg"
        };

        protected override void OnCreate(Bundle bundle)
        {
            base.OnCreate(bundle);

            var highWatermark = Java.Lang.Runtime.GetRuntime().MaxMemory() / 3;
            var lowWatermark = highWatermark / 2;

            // The GC threshold is the amount of bytes that have been evicted from the cache
            // that will trigger a GC.Collect. For example if set to 2mb, a GC will be performed
            // each time the cache has evicted a total of 2mb.
            // This means that we can have highWatermark + gcThreshold amount of memory in use
            // before a GC is forced, so we should ensure that the threshold value + hightWatermark
            // is less than our total memory.
            // In our case, the highWatermark is 1/3 of max memory, so using the same value for the
            // GC threshold means we can have up to 2/3 of max memory in use before kicking the GC.
            var gcThreshold = highWatermark;

            image_cache = new ReuseBitmapDrawableCache(highWatermark, lowWatermark, gcThreshold);

            // Set our view from the "main" layout resource
            SetContentView(Resource.Layout.Main);

            ThreadPool.QueueUserWorkItem(DownloadImages);

        }

        private void DownloadImages(object state)
        {
            var client = new WebClient();
            foreach (var uri in images_to_fetch) {
                var bytes = client.DownloadData(uri);
                var bitmap = BitmapFactory.DecodeByteArray(bytes, 0, bytes.Length);
                // ReuseBitmapDrawableCache is threadsafe
                image_cache.Add(new Uri(uri), new SelfDisposingBitmapDrawable(Resources, bitmap));
            }
            main_thread_handler.Post(() => {
                FindViewById<ProgressBar>(Resource.Id.progress).Visibility = ViewStates.Gone;
                grid_view = FindViewById<GridView>(Resource.Id.grid);
                grid_view.Adapter = new ImageAdapter(this);
            });
        }

        private class ImageAdapter : BaseAdapter
        {
            MainActivity activity;
            const int count_multiplier = 10;

            public ImageAdapter(MainActivity activity) : base()
            {
                this.activity = activity;
            }

            #region implemented abstract members of BaseAdapter

            public override Java.Lang.Object GetItem(int position)
            {
                position = position % count_multiplier;
                return activity.images_to_fetch[position];
            }

            public override long GetItemId(int position)
            {
                return position;
            }

            public override View GetView(int position, View convertView, ViewGroup parent)
            {
                position = position % count_multiplier;

                var imageView = (ImageView)convertView ?? new ManagedImageView(activity);
                var key = new Uri(activity.images_to_fetch[position]);
                // This assumes the image exists in the cache. In the real world you'd want to
                // Wrap cache checking to download the image if it is not in the cache.
                var drawable = activity.image_cache[key];
                imageView.SetImageDrawable(drawable);
                return imageView;
            }

            public override int Count {
                get {
                    return activity.images_to_fetch.Count * count_multiplier;
                }
            }

            #endregion

        }
    }
}


