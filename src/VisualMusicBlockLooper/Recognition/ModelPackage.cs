using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace VisualMusicBlockLooper.Recognition
{
    internal sealed class ModelPackage
    {
        private const long MaximumPackageSize = 128L * 1024 * 1024;
        private const int MaximumJsonSize = 8 * 1024 * 1024;
        private static readonly Regex RelativePath = new Regex(@"^[A-Za-z0-9_.-]+(?:/[A-Za-z0-9_.-]+)*$");

        public string Id { get; }
        public string[] Labels { get; }
        public int ImageSize { get; }
        public IReadOnlyDictionary<string, byte[]> Files { get; }

        private ModelPackage(string id, string[] labels, int imageSize, Dictionary<string, byte[]> files)
        {
            Id = id;
            Labels = labels;
            ImageSize = imageSize;
            Files = files;
        }

        public static ModelPackage Load(string path)
        {
            try
            {
                using (var stream = File.OpenRead(path))
                using (var hash = SHA256.Create())
                {
                    if (stream.Length > MaximumPackageSize)
                        throw new InvalidDataException("Il pacchetto supera 128 MB.");

                    string id = BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
                    stream.Position = 0;
                    using (var zip = new ZipArchive(stream, ZipArchiveMode.Read))
                        return ReadArchive(id, zip);
                }
            }
            catch (JsonException error)
            {
                throw new InvalidDataException("I file JSON del modello non sono validi.", error);
            }
        }

        private static ModelPackage ReadArchive(string id, ZipArchive zip)
        {
            var files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            byte[] Read(string key, long max)
            {
                if (!RelativePath.IsMatch(key) || key.Split('/').Any(part => part == "." || part == ".."))
                    throw new InvalidDataException("Il modello contiene un percorso risorse non valido.");

                if (files.TryGetValue(key, out var existing))
                    return existing;

                var entries = zip.Entries.Where(entry => entry.FullName == key).Take(2).ToArray();
                if (entries.Length != 1 || entries[0].Length <= 0 || entries[0].Length > max)
                    throw new InvalidDataException("Risorsa del modello mancante o non valida: " + key);

                var bytes = new byte[entries[0].Length];
                using (var input = entries[0].Open())
                {
                    int offset = 0;
                    while (offset < bytes.Length)
                    {
                        int read = input.Read(bytes, offset, bytes.Length - offset);
                        if (read == 0)
                            throw new InvalidDataException("Risorsa troncata: " + key);
                        offset += read;
                    }

                    if (input.ReadByte() != -1)
                        throw new InvalidDataException("Dimensione risorsa non valida: " + key);
                }

                files.Add(key, bytes);
                return bytes;
            }

            var meta = Parse(Read("metadata.json", MaximumJsonSize));
            var model = Parse(Read("model.json", MaximumJsonSize));
            if (!(meta["labels"] is JArray labelData) || labelData.Count == 0 ||
                labelData.Any(label => label.Type != JTokenType.String || string.IsNullOrWhiteSpace((string)label)))
                throw new InvalidDataException("Il modello non contiene etichette valide.");

            var labels = labelData.Values<string>().ToArray();
            if (labels.Distinct(StringComparer.Ordinal).Count() != labels.Length)
                throw new InvalidDataException("Le etichette del modello devono essere distinte.");

            var imageSize = meta["imageSize"];
            if (imageSize?.Type != JTokenType.Integer || (int)imageSize < 1 || (int)imageSize > 1024)
                throw new InvalidDataException("Dimensione di ingresso del modello non valida.");

            if (!(model["modelTopology"] is JObject) || !(model["weightsManifest"] is JArray manifest) ||
                manifest.Count == 0)
                throw new InvalidDataException("Il modello non è un'esportazione TensorFlow.js per immagini.");

            long remaining = MaximumPackageSize - files.Values.Sum(bytes => bytes.LongLength);
            foreach (var group in manifest)
            {
                if (!(group is JObject section) || !(section["paths"] is JArray paths) || paths.Count == 0)
                    throw new InvalidDataException("Manifest dei pesi non valido.");

                foreach (var path in paths)
                {
                    if (path.Type != JTokenType.String || (string)path == "model.json" || (string)path == "metadata.json")
                        throw new InvalidDataException("Percorso dei pesi non valido.");

                    bool alreadyRead = files.ContainsKey((string)path);
                    var bytes = Read((string)path, remaining);
                    if (!alreadyRead)
                        remaining -= bytes.LongLength;
                }
            }

            return new ModelPackage(id, labels, (int)imageSize, files);
        }

        private static JObject Parse(byte[] bytes)
        {
            var json = Encoding.UTF8.GetString(bytes).TrimStart('\uFEFF');
            return JObject.Parse(json, new JsonLoadSettings
            {
                DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error
            });
        }
    }
}
