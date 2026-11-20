using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using VisualMusicBlockLooper.Vision;

namespace VisualMusicBlockLooper.Audio
{
    internal static class SoundSamples
    {
        public static LoopMixer CreateMixer(GridLayout layout, string[] selected, int bpm, out string[] unavailable)
        {
            if (selected.Length != layout.Rows * layout.Columns)
                throw new ArgumentException("La sequenza non corrisponde al foglio acquisito.", nameof(selected));

            var cache = new Dictionary<string, float[]>(StringComparer.OrdinalIgnoreCase);
            var missing = new List<string>();
            foreach (var path in selected.Where(path => path != null).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    cache.Add(path, Read(path));
                }
                catch (Exception error) when (error is IOException || error is InvalidDataException || error is UnauthorizedAccessException ||
                    error is ArgumentException || error is FormatException || error is NotSupportedException)
                {
                    missing.Add(Path.GetFileName(path));
                }
            }

            unavailable = missing.ToArray();
            if (cache.Count == 0)
                throw new InvalidDataException("Nessun WAV riproducibile. " + string.Join(", ", unavailable));
            var steps = Enumerable.Range(0, layout.StepCount)
                .Select(_ => new List<float[]>(GridLayout.Lanes)).ToArray();
            for (int row = 0; row < layout.Rows; row++)
            for (int column = 0; column < layout.Columns; column++)
            {
                var path = selected[row * layout.Columns + column];
                if (path != null && cache.TryGetValue(path, out var samples))
                    steps[layout.StepAt(row, column)].Add(samples);
            }
            return new LoopMixer(steps.Select(voices => voices.ToArray()).ToArray(), bpm, layout.StepsPerBeat);
        }

        public static float[] Read(string path)
        {
            using (var wave = new WaveFileReader(path))
            {
                ISampleProvider source = wave.ToSampleProvider();
                if (source.WaveFormat.Channels == 1)
                    source = new MonoToStereoSampleProvider(source);
                else if (source.WaveFormat.Channels != LoopMixer.Channels)
                    throw new InvalidDataException("Il WAV deve essere mono o stereo: " + Path.GetFileName(path));

                if (source.WaveFormat.SampleRate != LoopMixer.SampleRate)
                    source = new WdlResamplingSampleProvider(source, LoopMixer.SampleRate);

                var output = new List<float>();
                var buffer = new float[8192];
                int read;
                while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
                    for (int index = 0; index < read; index++)
                    {
                        float sample = buffer[index];
                        output.Add(float.IsNaN(sample) || float.IsInfinity(sample)
                            ? 0 : Math.Max(-1, Math.Min(1, sample)) * 0.5f);
                    }

                if (output.Count == 0 || output.Count % LoopMixer.Channels != 0)
                    throw new InvalidDataException("Il WAV non contiene frame audio validi: " + Path.GetFileName(path));

                return output.ToArray();
            }
        }
    }
}
