//
// BitmapDrawableExtensions.cs
//
// Author:
//   Brett Duncavage <brett.duncavage@rd.io>
//
// Copyright 2013 Rdio, Inc.
//

using System;
using Android.OS;

namespace Rdio.TangoAndCache.Android.Util
{
    public static class Utils
    {
        public static bool HasHoneycomb {
            get {
                return Build.VERSION.SdkInt >= BuildVersionCodes.Honeycomb;
            }
        }
    }
}

