using System;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VisualMusicBlockLooper.Audio;
using VisualMusicBlockLooper.Scene;

namespace VisualMusicBlockLooper.Tests
{
    [TestClass]
    public class AudioTests
    {
        [TestMethod]
        public void MixesBothRowsOnTheSameFrameAndRepeatsAtFourSeconds()
        {
            var columns = EmptyPattern();
            columns[0] = new[] { new[] { 0.2f, 0.1f }, new[] { 0.3f, 0.4f } };
            columns[7] = new[] { new[] { 0.25f, 0.25f } };
            var mixer = new LoopMixer(columns, 120);
            const int loop = 4 * LoopMixer.SampleRate;
            var rendered = new float[(loop + 1) * 2];

            Assert.AreEqual(rendered.Length, mixer.Read(rendered, 0, rendered.Length));
            Assert.AreEqual(0.5f, rendered[0], 0.00001f);
            Assert.AreEqual(0.5f, rendered[1], 0.00001f);
            Assert.AreEqual(0f, rendered[2]);
            Assert.AreEqual(0.25f, rendered[7 * 22050 * 2]);
            Assert.AreEqual(0.5f, rendered[loop * 2]);
            Assert.AreEqual(7, mixer.ColumnAtFrame(loop - 1));
            Assert.AreEqual(0, mixer.ColumnAtFrame(loop));
        }

        [TestMethod]
        public void HoldsTempoAcrossManyLoopsAndPreservesAClipAcrossReads()
        {
            var columns = EmptyPattern();
            columns[0] = new[] { new[] { 0.2f, 0.2f, 0.3f, 0.3f } };
            var mixer = new LoopMixer(columns, 137);
            var first = new float[2];
            var next = new float[4];
            mixer.Read(first, 0, first.Length);
            mixer.Read(next, 0, next.Length);
            CollectionAssert.AreEqual(new[] { 0.2f, 0.2f }, first);
            CollectionAssert.AreEqual(new[] { 0.3f, 0.3f, 0f, 0f }, next);

            long framesAtRepeat = (long)Math.Round(LoopMixer.SampleRate * 60.0 * (8 * 137) / 137);
            Assert.AreEqual(7, mixer.ColumnAtFrame(framesAtRepeat - 1));
            Assert.AreEqual(0, mixer.ColumnAtFrame(framesAtRepeat));
        }

        [TestMethod]
        public void ReadsAllBundledWavsForTheStereoMixer()
        {
            var library = SoundLibrary.Load(SceneSettings.DefaultSoundsDirectory);
            Assert.AreEqual(8, library.Names.Count());
            foreach (string name in library.Names)
            {
                var samples = SoundSamples.Read(library.Resolve(name));
                Assert.IsTrue(samples.Length > 0 && samples.Length % 2 == 0, name);
                Assert.IsTrue(samples.Any(sample => Math.Abs(sample) > 0.001), name);
            }
            Assert.IsNull(library.Resolve(Path.Combine("..", "kick-01.wav")));
        }

        private static float[][][] EmptyPattern() =>
            Enumerable.Range(0, 8).Select(_ => Array.Empty<float[]>()).ToArray();
    }
}
