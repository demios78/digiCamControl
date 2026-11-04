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
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CameraControl.Core;
using CameraControl.Core.Classes;
using CameraControl.Devices;
using GalaSoft.MvvmLight;

#endregion

namespace CameraControl.ViewModel
{
    /// <summary>
    /// One semi-transparent onion skin layer drawn over the main image
    /// </summary>
    public class OnionSkinLayer : ViewModelBase
    {
        private double _opacity;

        public OnionSkinLayer(FileItem fileItem, BitmapSource image)
        {
            FileItem = fileItem;
            Image = image;
        }

        public FileItem FileItem { get; }

        public BitmapSource Image { get; }

        public double Opacity
        {
            get { return _opacity; }
            set
            {
                _opacity = value;
                RaisePropertyChanged(() => Opacity);
            }
        }
    }

    /// <summary>
    /// Onion skinning for the main window: shows the N photos taken before the displayed photo
    /// as semi-transparent layers over it. Layer updates are delayed by 150 ms so browsing
    /// through the session or taking photos in quick succession is not slowed down.
    /// </summary>
    public class OnionSkinViewModel : ViewModelBase, IDisposable
    {
        private static readonly TimeSpan UpdateDelay = TimeSpan.FromMilliseconds(150);

        private readonly Dispatcher _dispatcher;
        private readonly DispatcherTimer _updateTimer;
        private int _generation;

        public OnionSkinViewModel()
        {
            _dispatcher = Dispatcher.CurrentDispatcher;
            Layers = new ObservableCollection<OnionSkinLayer>();
            _updateTimer = new DispatcherTimer(DispatcherPriority.Background, _dispatcher) { Interval = UpdateDelay };
            _updateTimer.Tick += UpdateTimer_Tick;
            ServiceProvider.Settings.PropertyChanged += Settings_PropertyChanged;
            ServiceProvider.FileTransfered += ServiceProvider_FileTransfered;
        }

        /// <summary>
        /// Layers ordered from the oldest to the newest photo, so the newest one is drawn on top
        /// </summary>
        public ObservableCollection<OnionSkinLayer> Layers { get; }

        /// <summary>
        /// Request a layer update, executed 150 ms after the last request
        /// </summary>
        public void ScheduleUpdate()
        {
            _dispatcher.BeginInvoke(new Action(() =>
            {
                if (!ServiceProvider.Settings.OnionSkinEnabled)
                    return;
                _updateTimer.Stop();
                _updateTimer.Start();
            }));
        }

        private void UpdateTimer_Tick(object sender, EventArgs e)
        {
            _updateTimer.Stop();
            Update(false);
        }

        private void ServiceProvider_FileTransfered(object sender, FileItem fileItem)
        {
            ScheduleUpdate();
        }

        private void Settings_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case "OnionSkinEnabled":
                case "OnionSkinFrames":
                    _dispatcher.BeginInvoke(new Action(() => Update(false)));
                    break;
                case "OnionSkinTransparency":
                    _dispatcher.BeginInvoke(new Action(UpdateOpacities));
                    break;
                case "RotateIndex":
                case "FlipPreview":
                    // the layers have to follow the transformation of the main image
                    _dispatcher.BeginInvoke(new Action(() => Update(true)));
                    break;
                case "DefaultSession":
                    _dispatcher.BeginInvoke(new Action(() =>
                    {
                        Clear();
                        ScheduleUpdate();
                    }));
                    break;
            }
        }

        /// <summary>
        /// Rebuilds the layers for the currently displayed photo. Must be called on the UI thread.
        /// </summary>
        private void Update(bool forceReload)
        {
            _updateTimer.Stop();
            if (!ServiceProvider.Settings.OnionSkinEnabled)
            {
                Clear();
                return;
            }

            List<FileItem> previousFiles = GetPreviousFiles();
            if (previousFiles.Count == 0)
            {
                Clear();
                return;
            }

            // layers are kept oldest first, the selection is newest first
            previousFiles.Reverse();
            if (!forceReload && previousFiles.SequenceEqual(Layers.Select(layer => layer.FileItem)))
            {
                UpdateOpacities();
                return;
            }

            int generation = ++_generation;
            Task.Factory.StartNew(() => previousFiles.Select(item => new {item, image = LoadLayerImage(item)}).ToList())
                .ContinueWith(task =>
                {
                    // a newer update was started (or the feature was turned off) while loading
                    if (generation != _generation || task.IsFaulted || task.Result == null)
                        return;
                    Layers.Clear();
                    foreach (var loaded in task.Result.Where(loaded => loaded.image != null))
                    {
                        Layers.Add(new OnionSkinLayer(loaded.item, loaded.image));
                    }
                    UpdateOpacities();
                }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        /// <summary>
        /// The photos taken before the displayed one, newest first
        /// </summary>
        private static List<FileItem> GetPreviousFiles()
        {
            var result = new List<FileItem>();
            FileItem current = ServiceProvider.Settings.SelectedBitmap?.FileItem;
            PhotoSession session = ServiceProvider.Settings.DefaultSession;
            if (current == null || session == null || current.IsMovie)
                return result;

            List<FileItem> files = session.Files.ToList();
            int index = files.IndexOf(current);
            for (int i = index - 1; i >= 0 && result.Count < ServiceProvider.Settings.OnionSkinFrames; i--)
            {
                if (!files[i].IsMovie)
                    result.Add(files[i]);
            }
            return result;
        }

        /// <summary>
        /// Loads the cached preview of the photo with the same transformations as the main image
        /// </summary>
        private static BitmapSource LoadLayerImage(FileItem item)
        {
            try
            {
                if (!File.Exists(item.LargeThumb))
                    BitmapLoader.Instance.GenerateCache(item);
                if (!File.Exists(item.LargeThumb))
                    return null;

                PhotoUtils.WaitForFile(item.LargeThumb);
                var bitmapImage = new BitmapImage();
                bitmapImage.BeginInit();
                bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
                bitmapImage.UriSource = new Uri(item.LargeThumb);
                bitmapImage.EndInit();

                int rotateIndex = ServiceProvider.Settings.RotateIndex;
                bool flip = ServiceProvider.Settings.FlipPreview;
                if (rotateIndex == 0 && !flip)
                {
                    bitmapImage.Freeze();
                    return bitmapImage;
                }

                WriteableBitmap bitmap = BitmapFactory.ConvertToPbgra32Format(bitmapImage);
                if (rotateIndex >= 1 && rotateIndex <= 3)
                    bitmap = bitmap.Rotate(rotateIndex * 90);
                if (flip)
                    bitmap = bitmap.Flip(WriteableBitmapExtensions.FlipMode.Vertical);
                bitmap.Freeze();
                return bitmap;
            }
            catch (Exception exception)
            {
                Log.Error("Unable to load onion skin layer " + item.FileName, exception);
                return null;
            }
        }

        /// <summary>
        /// The selected photo is the most visible layer; every older photo is "transparency" percent
        /// less visible than the photo after it. Relative visibility: selected = 1, n-1 = (1 - t),
        /// n-2 = (1 - t)^2, ... normalized so the shares add up to the whole image.
        /// The layers are stacked over the selected photo (newest ghost on top), so each layer's
        /// opacity is its share divided by what is still visible below the layers above it.
        /// </summary>
        private void UpdateOpacities()
        {
            double keep = (100 - ServiceProvider.Settings.OnionSkinTransparency) / 100.0;
            int count = Layers.Count;

            double total = 0;
            for (int k = 0; k <= count; k++)
                total += Math.Pow(keep, k);

            // Layers are ordered oldest first; the newest (n-1) is the last one and drawn on top
            double remaining = 1.0;
            for (int k = 1; k <= count; k++)
            {
                double share = Math.Pow(keep, k) / total;
                OnionSkinLayer layer = Layers[count - k];
                layer.Opacity = remaining > 0 ? Math.Min(1.0, share / remaining) : 0;
                remaining -= share;
            }
        }

        private void Clear()
        {
            _generation++;
            _updateTimer.Stop();
            Layers.Clear();
        }

        public void Dispose()
        {
            _updateTimer.Stop();
            _updateTimer.Tick -= UpdateTimer_Tick;
            ServiceProvider.Settings.PropertyChanged -= Settings_PropertyChanged;
            ServiceProvider.FileTransfered -= ServiceProvider_FileTransfered;
            Clear();
        }
    }
}
