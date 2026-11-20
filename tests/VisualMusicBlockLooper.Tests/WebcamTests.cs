using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VisualMusicBlockLooper.Vision;

namespace VisualMusicBlockLooper.Tests
{
    [TestClass]
    public class WebcamTests
    {
        [TestMethod]
        [TestCategory("Hardware")]
        public void CapturesAFrameFromAPhysicalUsbCamera()
        {
            var device = Webcam.Devices().FirstOrDefault(camera =>
                camera.MonikerString.IndexOf("usb", StringComparison.OrdinalIgnoreCase) >= 0);
            if (device == null)
                Assert.Inconclusive("Nessuna webcam USB collegata.");

            using (var webcam = new Webcam(device.MonikerString))
            {
                var clock = Stopwatch.StartNew();
                while (clock.Elapsed < TimeSpan.FromSeconds(8))
                {
                    using (var frame = webcam.Snapshot())
                    {
                        if (frame != null)
                        {
                            Assert.IsTrue(frame.Width > 0 && frame.Height > 0);
                            Console.WriteLine(device.Name + " " + frame.Width + "×" + frame.Height);
                            return;
                        }
                    }

                    if (webcam.Error != null)
                        Assert.Fail(webcam.Error);
                    Thread.Sleep(60);
                }
                Assert.Fail("La webcam non ha consegnato fotogrammi entro 8 secondi.");
            }
        }
    }
}
