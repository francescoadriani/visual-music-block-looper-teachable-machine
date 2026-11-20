using System;
using System.Collections.Generic;
using System.IO;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace VisualMusicBlockLooper.Audio
{
    internal static class SoundSamples
    {
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
