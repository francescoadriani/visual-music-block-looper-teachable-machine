using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VisualMusicBlockLooper.Recognition;

namespace VisualMusicBlockLooper.Tests
{
    [TestClass]
    public class ClassifierTests
    {
        [TestMethod]
        [TestCategory("WebView2")]
        public async Task ClassifiesLocallyWithTheActualModel()
        {
            var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var thread = new Thread(() =>
            {
                using (var form = new Form { ShowInTaskbar = false, Width = 160, Height = 90, Opacity = 0 })
                using (var classifier = new ImageClassifier(form, Path.Combine(Path.GetTempPath(), "looper-webview-tests")))
                {
                    form.Shown += async (sender, args) =>
                    {
                        try
                        {
                            var package = ModelPackage.Load(ModelPackageTests.Sample);
                            await classifier.LoadAsync(package);
                            using (var tile = new Bitmap(100, 100))
                            using (var graphics = Graphics.FromImage(tile))
                            using (var stream = new MemoryStream())
                            {
                                graphics.Clear(Color.White);
                                graphics.DrawEllipse(Pens.Black, 20, 20, 60, 60);
                                tile.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
                                var scores = await classifier.PredictAsync(new[] { stream.ToArray(), null, stream.ToArray() });
                                Assert.AreEqual(3, scores.Length);
                                Assert.IsNull(scores[1]);
                                Assert.AreEqual(4, scores[0].Length);
                                Assert.AreEqual(1.0, scores[0].Sum(), 0.001);
                                CollectionAssert.AreEqual(scores[0], scores[2]);
                            }

                            completion.TrySetResult(true);
                        }
                        catch (Exception error)
                        {
                            completion.TrySetException(error);
                        }
                        finally
                        {
                            form.Close();
                        }
                    };
                    Application.Run(form);
                }
            }) { IsBackground = true };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();

            if (await Task.WhenAny(completion.Task, Task.Delay(TimeSpan.FromSeconds(90))) != completion.Task)
                Assert.Fail("L'integrazione con WebView2 non ha risposto.");
            await completion.Task;
            thread.Join(6000);
        }
    }
}
