using System.Collections.Generic;

namespace VisualMusicBlockLooper.Scene
{
    internal sealed class SceneSettings
    {
        public const int MinimumBpm = 30;
        public const int MaximumBpm = 300;
        public const string DefaultSoundsDirectory = "assets/suoni";

        public string CameraId { get; set; }
        public string ModelPath { get; set; }
        public string SoundsDirectory { get; set; } = DefaultSoundsDirectory;
        public int Bpm { get; set; } = 120;
        public int ThresholdPercent { get; set; } = 85;
        public Dictionary<string, Dictionary<string, string>> Assignments { get; set; } =
            new Dictionary<string, Dictionary<string, string>>();

        public string SoundFor(string modelId, string label)
        {
            if (Assignments.TryGetValue(modelId, out var map) && map != null && map.TryGetValue(label, out var file))
                return file;

            return null;
        }

        public void Assign(string modelId, string label, string file)
        {
            if (!Assignments.TryGetValue(modelId, out var map) || map == null)
                Assignments[modelId] = map = new Dictionary<string, string>();

            if (file == null)
                map.Remove(label);
            else
                map[label] = file;
        }
    }
}
