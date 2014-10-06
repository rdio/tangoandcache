using System;
using Android.Widget;
using Android.Content;
using Android.Util;
using Rdio.TangoAndCache.Android.UI.Drawables;
using Android.Graphics.Drawables;

namespace Rdio.TangoAndCache.Android.Widget
{
    public class ManagedImageView : ImageView
    {
        public ManagedImageView(Context context)
            : base(context)
        {
        }

        public ManagedImageView(Context context, IAttributeSet attrs)
            : base(context, attrs)
        {
        }

        public ManagedImageView(Context context, IAttributeSet attrs, int defStyle)
            : base(context, attrs, defStyle)
        {
        }

        public override void SetImageDrawable(Drawable drawable)
        {
            var previous = Drawable;

            base.SetImageDrawable(drawable);

            UpdateDrawableDisplayedState(drawable, true);
            UpdateDrawableDisplayedState(previous, false);
        }

        public override void SetImageResource(int resId)
        {
            var previous = Drawable;
            // Ultimately calls SetImageDrawable, where the state will be updated.
            base.SetImageResource(resId);
            UpdateDrawableDisplayedState(previous, false);
        }

        public override void SetImageURI(global::Android.Net.Uri uri)
        {
            var previous = Drawable;
            // Ultimately calls SetImageDrawable, where the state will be updated.
            base.SetImageURI(uri);
            UpdateDrawableDisplayedState(previous, false);
        }

        private void UpdateDrawableDisplayedState(Drawable drawable, bool isDisplayed)
        {
            if (drawable is SelfDisposingBitmapDrawable) {
                ((SelfDisposingBitmapDrawable)drawable).SetIsDisplayed(isDisplayed);
            } else if (drawable is LayerDrawable) {
                var layerDrawable = (LayerDrawable)drawable;
                for (var i = 0; i < layerDrawable.NumberOfLayers; i++) {
                    UpdateDrawableDisplayedState(layerDrawable.GetDrawable(i), isDisplayed);
                }
            }
        }

    }
}

