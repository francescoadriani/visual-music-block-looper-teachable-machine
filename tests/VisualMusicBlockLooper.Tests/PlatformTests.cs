using System;
using System.Reflection;
using System.Runtime.Versioning;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AForge.Video.DirectShow;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.Web.WebView2.Core;
using NAudio.Wave;
using OpenCvSharp;
using VisualMusicBlockLooper.Audio;

namespace VisualMusicBlockLooper.Tests
{
    [TestClass]
    public class PlatformTests
    {
        [TestMethod]
        public void TheAppTargetsFramework47AndLoadsNativeDependencies()
        {
            var target = typeof(Program).Assembly.GetCustomAttribute<TargetFrameworkAttribute>();
            Assert.AreEqual(".NETFramework,Version=v4.7", target.FrameworkName);
            Assert.IsTrue(Environment.Is64BitProcess);
            Assert.IsFalse(string.IsNullOrEmpty(Cv2.GetVersionString()));
            var cameras = new FilterInfoCollection(FilterCategory.VideoInputDevice);
            Console.WriteLine("Webcam DirectShow: " + cameras.Count);
        }

        [TestMethod]
        [TestCategory("WebView2")]
        public void WindowsHasAWebView2Runtime()
        {
            Assert.IsFalse(string.IsNullOrWhiteSpace(CoreWebView2Environment.GetAvailableBrowserVersionString()));
        }

        [TestMethod]
        [TestCategory("Hardware")]
        public void WindowsCanOpenTheDefaultAudioDevice()
        {
            using (var output = new WaveOutEvent())
            {
                output.Init(new SilenceProvider(new WaveFormat(44100, 16, 2)));
                Assert.AreEqual(44100, output.OutputWaveFormat.SampleRate);
                output.Play();
                var clock = Stopwatch.StartNew();
                while (output.GetPosition() == 0 && clock.Elapsed < TimeSpan.FromSeconds(3))
                    Thread.Sleep(30);
                Assert.IsTrue(output.GetPosition() > 0);
                output.Stop();
                Assert.AreEqual(PlaybackState.Stopped, output.PlaybackState);
            }
        }

        [TestMethod]
        [TestCategory("Hardware")]
        public async Task SoundPreviewPlaysOnceAndReleasesTheWaveFile()
        {
            var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".wav");
            try
            {
                using (var wave = new WaveFileWriter(path, new WaveFormat(22050, 16, 1)))
                {
                    var silence = new byte[4410];
                    wave.Write(silence, 0, silence.Length);
                }

                using (var preview = new SoundPreview(path))
                {
                    var ended = new TaskCompletionSource<StoppedEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
                    preview.Stopped += (sender, args) => ended.TrySetResult(args);
                    preview.Play();
                    Assert.AreSame(ended.Task, await Task.WhenAny(ended.Task, Task.Delay(TimeSpan.FromSeconds(3))));
                    Assert.IsNull((await ended.Task).Exception);
                }

                using (File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                {
                }
            }
            finally
            {
                File.Delete(path);
            }
        }
    }
}
