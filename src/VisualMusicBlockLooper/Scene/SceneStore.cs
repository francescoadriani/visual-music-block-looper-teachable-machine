using System;
using System.IO;
using System.Text;
using Newtonsoft.Json;

namespace VisualMusicBlockLooper.Scene
{
    internal sealed class SceneStore
    {
        public static readonly string DefaultDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VisualMusicBlockLooper");

        private readonly string path;

        public SceneStore(string path)
        {
            this.path = Path.GetFullPath(path);
        }

        public SceneSettings Load(out string warning)
        {
            warning = null;
            if (!File.Exists(path))
                return new SceneSettings();

            try
            {
                var settings = JsonConvert.DeserializeObject<SceneSettings>(File.ReadAllText(path));
                if (settings == null || settings.Assignments == null ||
                    settings.Bpm < SceneSettings.MinimumBpm || settings.Bpm > SceneSettings.MaximumBpm ||
                    settings.ThresholdPercent < 0 || settings.ThresholdPercent > 100 ||
                    string.IsNullOrWhiteSpace(settings.SoundsDirectory))
                    throw new InvalidDataException("Valori della scena fuori intervallo.");

                return settings;
            }
            catch (Exception error) when (error is IOException || error is InvalidDataException ||
                error is UnauthorizedAccessException || error is JsonException)
            {
                warning = "Impostazioni di scena non disponibili. Verranno usati i valori iniziali. " + error.Message;
                return new SceneSettings();
            }
        }

        public void Save(SceneSettings settings)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temp, JsonConvert.SerializeObject(settings, Formatting.Indented), new UTF8Encoding(false));
                if (File.Exists(path))
                    File.Replace(temp, path, path + ".bak");
                else
                    File.Move(temp, path);
            }
            finally
            {
                if (File.Exists(temp))
                    File.Delete(temp);
            }
        }
    }
}
