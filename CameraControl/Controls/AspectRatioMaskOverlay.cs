#region Licence

// Distributed under MIT License
// ===========================================================
//
// digiCamControl - DSLR camera remote control open source software
// Copyright (C) 2014 Duka Istvan
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
// EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
// MERCHANTABILITY,FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT.
// IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY
// CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT,
// TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH
// THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.

#endregion

#region

using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

#endregion

namespace CameraControl.Controls
{
    /// <summary>
    /// Draws an aspect ratio mask (shaded area outside the selected ratio), rule of thirds lines and a
    /// center dot. Placed over the main image with the same size, so it zooms and pans with the image.
    /// Line widths are corrected by <see cref="Scale"/> so they keep the same size on screen.
    /// </summary>
    public class AspectRatioMaskOverlay : FrameworkElement
    {
        public static readonly DependencyProperty RatioProperty = DependencyProperty.Register("Ratio",
            typeof(string), typeof(AspectRatioMaskOverlay),
            new FrameworkPropertyMetadata("None", FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty TransparencyProperty = DependencyProperty.Register("Transparency",
            typeof(int), typeof(AspectRatioMaskOverlay),
            new FrameworkPropertyMetadata(50, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty ShowThirdsProperty = DependencyProperty.Register("ShowThirds",
            typeof(bool), typeof(AspectRatioMaskOverlay),
            new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty ShowCenterDotProperty = DependencyProperty.Register("ShowCenterDot",
            typeof(bool), typeof(AspectRatioMaskOverlay),
            new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty ScaleProperty = DependencyProperty.Register("Scale",
            typeof(double), typeof(AspectRatioMaskOverlay),
            new FrameworkPropertyMetadata(1.0, FrameworkPropertyMetadataOptions.AffectsRender));

        /// <summary>
        /// Aspect ratio as "width:height" (for example "16:9" or "2.39:1"), "None" for no mask
        /// </summary>
        public string Ratio
        {
            get { return (string)GetValue(RatioProperty); }
            set { SetValue(RatioProperty, value); }
        }

        /// <summary>
        /// Transparency of the masked area and the guides in percent (0 = opaque, 100 = invisible)
        /// </summary>
        public int Transparency
        {
            get { return (int)GetValue(TransparencyProperty); }
            set { SetValue(TransparencyProperty, value); }
        }

        public bool ShowThirds
        {
            get { return (bool)GetValue(ShowThirdsProperty); }
            set { SetValue(ShowThirdsProperty, value); }
        }

        public bool ShowCenterDot
        {
            get { return (bool)GetValue(ShowCenterDotProperty); }
            set { SetValue(ShowCenterDotProperty, value); }
        }

        /// <summary>
        /// Current zoom of the image (screen pixels per image pixel)
        /// </summary>
        public double Scale
        {
            get { return (double)GetValue(ScaleProperty); }
            set { SetValue(ScaleProperty, value); }
        }

        public AspectRatioMaskOverlay()
        {
            IsHitTestVisible = false;
            Focusable = false;
        }

        /// <summary>
        /// Parses "16:9" or "2.39:1" into width / height, returns 0 for "None" or invalid values
        /// </summary>
        public static double ParseRatio(string ratio)
        {
            if (string.IsNullOrWhiteSpace(ratio))
                return 0;
            string[] parts = ratio.Split(':');
            if (parts.Length != 2)
                return 0;
            double width;
            double height;
            if (!double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out width) ||
                !double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out height) ||
                width <= 0 || height <= 0)
                return 0;
            return width / height;
        }

        /// <summary>
        /// The largest rectangle with the given ratio centered in a width x height area
        /// </summary>
        public static Rect GetFrame(double width, double height, double ratio)
        {
            if (ratio <= 0 || width <= 0 || height <= 0)
                return new Rect(0, 0, Math.Max(0, width), Math.Max(0, height));
            double frameWidth = width;
            double frameHeight = width / ratio;
            if (frameHeight > height)
            {
                frameHeight = height;
                frameWidth = height * ratio;
            }
            return new Rect((width - frameWidth) / 2, (height - frameHeight) / 2, frameWidth, frameHeight);
        }

        protected override void OnRender(DrawingContext drawingContext)
        {
            base.OnRender(drawingContext);
            double width = ActualWidth;
            double height = ActualHeight;
            if (width <= 0 || height <= 0)
                return;

            Rect frame = GetFrame(width, height, ParseRatio(Ratio));
            // one screen pixel expressed in image pixels
            double pixel = Scale > 0 ? 1 / Scale : 1;

            // mask: shade everything outside the frame
            double maskOpacity = (100 - Transparency) / 100.0;
            if (maskOpacity > 0 && (frame.Width < width - 0.5 || frame.Height < height - 0.5))
            {
                var mask = new GeometryGroup { FillRule = FillRule.EvenOdd };
                mask.Children.Add(new RectangleGeometry(new Rect(0, 0, width, height)));
                mask.Children.Add(new RectangleGeometry(frame));
                drawingContext.DrawGeometry(new SolidColorBrush(Color.FromArgb((byte)(255 * maskOpacity), 0, 0, 0)),
                    null, mask);
                drawingContext.DrawRectangle(null, new Pen(new SolidColorBrush(Color.FromArgb(160, 255, 255, 255)), pixel), frame);
            }

            // guides use the same transparency as the mask
            if (!ShowThirds && !ShowCenterDot)
                return;
            drawingContext.PushOpacity(maskOpacity);

            // guides are drawn white with a dark outline so they are visible on any image
            var outline = new Pen(new SolidColorBrush(Color.FromArgb(140, 0, 0, 0)), 3 * pixel);
            var line = new Pen(new SolidColorBrush(Color.FromArgb(220, 255, 255, 255)), 1.2 * pixel);

            if (ShowThirds)
            {
                for (int i = 1; i <= 2; i++)
                {
                    double x = frame.Left + frame.Width * i / 3;
                    double y = frame.Top + frame.Height * i / 3;
                    var vertical = new Point[] { new Point(x, frame.Top), new Point(x, frame.Bottom) };
                    var horizontal = new Point[] { new Point(frame.Left, y), new Point(frame.Right, y) };
                    drawingContext.DrawLine(outline, vertical[0], vertical[1]);
                    drawingContext.DrawLine(outline, horizontal[0], horizontal[1]);
                    drawingContext.DrawLine(line, vertical[0], vertical[1]);
                    drawingContext.DrawLine(line, horizontal[0], horizontal[1]);
                }
            }

            if (ShowCenterDot)
            {
                var center = new Point(frame.Left + frame.Width / 2, frame.Top + frame.Height / 2);
                drawingContext.DrawEllipse(new SolidColorBrush(Color.FromArgb(230, 255, 255, 255)),
                    new Pen(new SolidColorBrush(Color.FromArgb(180, 0, 0, 0)), 1.5 * pixel), center, 4 * pixel, 4 * pixel);
            }

            drawingContext.Pop();
        }
    }
}
