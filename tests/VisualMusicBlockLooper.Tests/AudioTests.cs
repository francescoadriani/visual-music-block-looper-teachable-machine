using System;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VisualMusicBlockLooper.Audio;
using VisualMusicBlockLooper.Scene;
using VisualMusicBlockLooper.Vision;

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
            var mixer = new LoopMixer(columns, 120, 1);
            const int loop = 4 * LoopMixer.SampleRate;
            var rendered = new float[(loop + 1) * 2];

            Assert.AreEqual(rendered.Length, mixer.Read(rendered, 0, rendered.Length));
            Assert.AreEqual(0.5f, rendered[0], 0.00001f);
            Assert.AreEqual(0.5f, rendered[1], 0.00001f);
            Assert.AreEqual(0f, rendered[2]);
            Assert.AreEqual(0.25f, rendered[7 * 22050 * 2]);
            Assert.AreEqual(0.5f, rendered[loop * 2]);
            Assert.AreEqual(7, mixer.StepAtFrame(loop - 1));
            Assert.AreEqual(0, mixer.StepAtFrame(loop));
        }

        [TestMethod]
        public void HoldsTempoAcrossManyLoopsAndPreservesAClipAcrossReads()
        {
            var columns = EmptyPattern();
            columns[0] = new[] { new[] { 0.2f, 0.2f, 0.3f, 0.3f } };
            var mixer = new LoopMixer(columns, 137, 1);
            var first = new float[2];
            var next = new float[4];
            mixer.Read(first, 0, first.Length);
            mixer.Read(next, 0, next.Length);
            CollectionAssert.AreEqual(new[] { 0.2f, 0.2f }, first);
            CollectionAssert.AreEqual(new[] { 0.3f, 0.3f, 0f, 0f }, next);

            long framesAtRepeat = (long)Math.Round(LoopMixer.SampleRate * 60.0 * (8 * 137) / 137);
            Assert.AreEqual(7, mixer.StepAtFrame(framesAtRepeat - 1));
            Assert.AreEqual(0, mixer.StepAtFrame(framesAtRepeat));
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

        [TestMethod]
        public void PlaysAvailableSamplesWhenAnotherFileHasDisappeared()
        {
            var pattern = new string[32];
            var library = SoundLibrary.Load(SceneSettings.DefaultSoundsDirectory);
            pattern[0] = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".wav");
            pattern[8] = library.Resolve("kick-01.wav");

            var mixer = SoundSamples.CreateMixer(GridLayout.Original, pattern, 120, out var unavailable);
            CollectionAssert.AreEqual(new[] { Path.GetFileName(pattern[0]) }, unavailable);
            var output = new float[2048];
            mixer.Read(output, 0, output.Length);
            Assert.IsTrue(output.Any(sample => Math.Abs(sample) > 0.01f));
        }

        [TestMethod]
        public void ThirtyTwoStepsUseTheSameFourSecondLoopAndSampleAccurateBoundaries()
        {
            var steps = Enumerable.Range(0, 32).Select(_ => Array.Empty<float[]>()).ToArray();
            steps[15] = new[] { new[] { 0.2f, 0.2f } };
            steps[16] = new[] { new[] { 0.3f, 0.3f }, new[] { 0.2f, 0.2f } };
            steps[0] = new[] { new[] { 0.1f, 0.1f } };
            var mixer = new LoopMixer(steps, 120, 4);
            var output = new float[(4 * LoopMixer.SampleRate + 1) * 2];
            mixer.Read(output, 0, output.Length);
            Assert.AreEqual(0.2f, output[82688 * 2]);
            Assert.AreEqual(0f, output[88199 * 2]);
            Assert.AreEqual(0.5f, output[88200 * 2]);
            Assert.AreEqual(0.1f, output[176400 * 2]);
            Assert.AreEqual(15, mixer.StepAtFrame(88199));
            Assert.AreEqual(16, mixer.StepAtFrame(88200));
            Assert.AreEqual(31, mixer.StepAtFrame(176399));
            Assert.AreEqual(0, mixer.StepAtFrame(176400));

            var uneven = new LoopMixer(steps, 137, 4);
            long repeat = 8L * 60 * LoopMixer.SampleRate;
            Assert.AreEqual(31, uneven.StepAtFrame(repeat - 1));
            Assert.AreEqual(0, uneven.StepAtFrame(repeat));
        }

        [TestMethod]
        public void TheLowerBlockStartsAtStepSeventeen()
        {
            var library = SoundLibrary.Load(SceneSettings.DefaultSoundsDirectory);
            var pattern = new string[128];
            pattern[4 * 16] = library.Resolve("kick-01.wav");

            var mixer = SoundSamples.CreateMixer(GridLayout.Detailed, pattern, 120, out var unavailable);
            Assert.AreEqual(0, unavailable.Length);
            var output = new float[88200 * 2 + 2048];
            mixer.Read(output, 0, output.Length);
            Assert.IsTrue(output.Take(88200 * 2).All(sample => sample == 0));
            Assert.IsTrue(output.Skip(88200 * 2).Any(sample => Math.Abs(sample) > 0.01f));
        }

        private static float[][][] EmptyPattern() =>
            Enumerable.Range(0, 8).Select(_ => Array.Empty<float[]>()).ToArray();
    }
}
