using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json.Linq;
using OpenCvSharp;
using VisualMusicBlockLooper.Audio;
using VisualMusicBlockLooper.Recognition;
using VisualMusicBlockLooper.Scene;
using VisualMusicBlockLooper.Vision;

namespace VisualMusicBlockLooper.Tests
{
    [TestClass]
    public class ClassifierTests
    {
        [TestMethod]
        [TestCategory("WebView2")]
        public async Task TurnsARecognizedGridIntoALoopWithTheActualModel()
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
                            await RejectIncompatibleReplacement(classifier);
                            Assert.AreSame(package, classifier.Active);
                            using (var sheet = GridTests.RasterizeSheet())
                            {
                                Cv2.Circle(sheet, new Point(214, 258), 26, Scalar.Black, 5);
                                Cv2.Circle(sheet, new Point(214, 366), 26, Scalar.Black, 5);
                                var cells = GridReader.Read(sheet);
                                var scores = await classifier.PredictAsync(cells.Select(cell => cell.Png).ToArray());
                                Assert.AreEqual(32, scores.Length);
                                Assert.AreEqual(2, scores.Count(score => score != null));
                                Assert.IsNull(scores[1]);
                                Assert.AreEqual(4, scores[0].Length);
                                Assert.AreEqual(1.0, scores[0].Sum(), 0.001);
                                Assert.IsNotNull(scores[8]);

                                var library = SoundLibrary.Load(SceneSettings.DefaultSoundsDirectory);
                                var scene = new SceneSettings();
                                foreach (var label in package.Labels)
                                    scene.Assign(package.Id, label, "kick-01.wav");

                                var paths = scores.Select(score => CellMatch.Label(package.Labels, score, 0))
                                    .Select(label => label == null ? null : library.Resolve(scene.SoundFor(package.Id, label)))
                                    .ToArray();
                                var mixer = SoundSamples.CreateMixer(paths, scene.Bpm, out var unavailable);
                                Assert.AreEqual(0, unavailable.Length);
                                var output = new float[44100 * 2 * 4 + 2048];
                                mixer.Read(output, 0, output.Length);
                                Assert.IsTrue(output.Take(2048).Any(sample => Math.Abs(sample) > 0.01f));
                                Assert.IsTrue(output.Skip(44100).Take(2048).All(sample => sample == 0));
                                CollectionAssert.AreEqual(output.Take(2048).ToArray(), output.Skip(44100 * 2 * 4).ToArray());
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

        private static async Task RejectIncompatibleReplacement(ImageClassifier classifier)
        {
            string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".zip");
            try
            {
                File.Copy(ModelPackageTests.Sample, path);
                using (var archive = ZipFile.Open(path, ZipArchiveMode.Update))
                {
                    var oldMetadata = archive.GetEntry("metadata.json");
                    JObject changed;
                    using (var reader = new StreamReader(oldMetadata.Open()))
                        changed = JObject.Parse(reader.ReadToEnd());
                    changed["imageSize"] = 223;
                    oldMetadata.Delete();
                    using (var writer = new StreamWriter(archive.CreateEntry("metadata.json").Open()))
                        writer.Write(changed.ToString());
                }

                await Assert.ThrowsExceptionAsync<InvalidDataException>(() => classifier.LoadAsync(ModelPackage.Load(path)));
            }
            finally
            {
                File.Delete(path);
            }
        }
    }
}
