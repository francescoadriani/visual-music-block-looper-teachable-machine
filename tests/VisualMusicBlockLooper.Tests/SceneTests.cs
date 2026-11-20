using System;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VisualMusicBlockLooper.Scene;

namespace VisualMusicBlockLooper.Tests
{
    [TestClass]
    public class SceneTests
    {
        [TestMethod]
        public void SavesAReopenableConfigurationWithAssignmentsPerModel()
        {
            var directory = Path.Combine(Path.GetTempPath(), "looper-tests", Guid.NewGuid().ToString("N"));
            var path = Path.Combine(directory, "scena.json");
            try
            {
                var store = new SceneStore(path);
                var settings = store.Load(out var warning);
                Assert.IsNull(warning);
                Assert.AreEqual(120, settings.Bpm);
                Assert.AreEqual(85, settings.ThresholdPercent);
                settings.Bpm = 146;
                settings.ThresholdPercent = 72;
                settings.CameraId = "camera-id";
                settings.ModelPath = "modello.zip";
                settings.SoundsDirectory = "suoni";
                settings.Assign("A", "étoile bleue", "Kick.WAV");
                settings.Assign("B", "étoile bleue", "Hat.wav");
                store.Save(settings);
                settings.Bpm = 147;
                store.Save(settings);

                var restored = store.Load(out warning);
                Assert.IsNull(warning);
                Assert.AreEqual(147, restored.Bpm);
                Assert.AreEqual(72, restored.ThresholdPercent);
                Assert.AreEqual("camera-id", restored.CameraId);
                Assert.AreEqual("modello.zip", restored.ModelPath);
                Assert.AreEqual("suoni", restored.SoundsDirectory);
                Assert.AreEqual("Kick.WAV", restored.SoundFor("A", "étoile bleue"));
                Assert.AreEqual("Hat.wav", restored.SoundFor("B", "étoile bleue"));

                File.WriteAllText(path, "{non valido");
                Assert.AreEqual(120, store.Load(out warning).Bpm);
                Assert.IsNotNull(warning);
                Assert.AreEqual("{non valido", File.ReadAllText(path));

                File.WriteAllText(path, "{\"Bpm\":2000}");
                Assert.AreEqual(120, store.Load(out warning).Bpm);
                Assert.IsNotNull(warning);
            }
            finally
            {
                foreach (var item in Directory.GetFiles(directory))
                    File.Delete(item);
                Directory.Delete(directory);
            }
        }

        [TestMethod]
        public void UsesTheTopClassAndSkipsUncertainAndEmptyCells()
        {
            var labels = new[] { "croce", "VUOTO", "triangolo" };
            Assert.AreEqual("croce", CellMatch.Label(labels, new[] { 0.85, 0.1, 0.05 }, 85));
            Assert.IsNull(CellMatch.Label(labels, new[] { 0.849, 0.1, 0.051 }, 85));
            Assert.IsNull(CellMatch.Label(labels, new[] { 0.01, 0.98, 0.01 }, 85));
            Assert.IsNull(CellMatch.Label(labels, null, 85));
        }
    }
}
