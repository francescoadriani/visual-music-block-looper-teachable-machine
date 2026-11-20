using System;
using System.Drawing;
using System.Linq;
using AForge.Video;
using AForge.Video.DirectShow;

namespace VisualMusicBlockLooper.Vision
{
    internal sealed class Webcam : IDisposable
    {
        private readonly VideoCaptureDevice device;
        private readonly object frameLock = new object();
        private Bitmap latest;
        private string error;
        private bool disposed;

        public static FilterInfo[] Devices() =>
            new FilterInfoCollection(FilterCategory.VideoInputDevice).Cast<FilterInfo>().ToArray();

        public Webcam(string id)
        {
            device = new VideoCaptureDevice(id);
            var resolution = device.VideoCapabilities
                .Where(mode => mode.FrameSize.Width <= 1920)
                .OrderBy(mode => Math.Abs(mode.FrameSize.Width - 1280) + Math.Abs(mode.FrameSize.Height - 720))
                .ThenByDescending(mode => mode.AverageFrameRate)
                .FirstOrDefault();
            if (resolution != null)
                device.VideoResolution = resolution;

            device.NewFrame += OnFrame;
            device.VideoSourceError += OnError;
            device.Start();
        }

        public string Error
        {
            get { lock (frameLock) return error; }
        }

        public Bitmap Snapshot()
        {
            lock (frameLock)
                return latest == null ? null : (Bitmap)latest.Clone();
        }

        private void OnFrame(object sender, NewFrameEventArgs args)
        {
            lock (frameLock)
            {
                if (disposed)
                    return;

                latest?.Dispose();
                latest = (Bitmap)args.Frame.Clone();
                error = null;
            }
        }

        private void OnError(object sender, VideoSourceErrorEventArgs args)
        {
            lock (frameLock)
            {
                error = args.Description;
                latest?.Dispose();
                latest = null;
            }
        }

        public void Dispose()
        {
            lock (frameLock)
            {
                if (disposed)
                    return;

                disposed = true;
                latest?.Dispose();
                latest = null;
            }

            device.NewFrame -= OnFrame;
            device.VideoSourceError -= OnError;
            device.SignalToStop();
            device.WaitForStop();
        }
    }
}
