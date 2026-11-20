using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VisualMusicBlockLooper.Recognition;

namespace VisualMusicBlockLooper.Tests
{
    [TestClass]
    public class ModelPackageTests
    {
        internal static readonly string Sample = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "model-fixture.zip");

        [TestMethod]
        public void ReadsTheActualTeachableMachineExport()
        {
            var model = ModelPackage.Load(Sample);
            CollectionAssert.AreEqual(new[] { "cerchio", "quadrato", "croce", "triangolo" }, model.Labels);
            Assert.AreEqual(224, model.ImageSize);
            Assert.IsTrue(model.Files.ContainsKey("weights.bin"));
            Assert.AreEqual(model.Id, ModelPackage.Load(Sample).Id);
        }

        [TestMethod]
        [DataRow("weights.bin")]
        [DataRow("../weights.bin")]
        [DataRow("https://example.invalid/weights.bin")]
        public void RejectsMissingOrNonLocalWeights(string weights)
        {
            var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".zip");
            try
            {
                using (var archive = ZipFile.Open(path, ZipArchiveMode.Create))
                {
                    Write(archive, "metadata.json", "{\"labels\":[\"émpty\",\"un suono\"],\"imageSize\":224}");
                    Write(archive, "model.json", "{\"modelTopology\":{},\"weightsManifest\":[{\"paths\":[\"" + weights + "\"]}]}");
                }

                Assert.ThrowsException<InvalidDataException>(() => ModelPackage.Load(path));
            }
            finally
            {
                File.Delete(path);
            }
        }

        private static void Write(ZipArchive zip, string name, string content)
        {
            using (var writer = new StreamWriter(zip.CreateEntry(name).Open(), new UTF8Encoding(false)))
                writer.Write(content);
        }
    }
}
