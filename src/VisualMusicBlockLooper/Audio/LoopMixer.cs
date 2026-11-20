using System;
using System.Collections.Generic;
using NAudio.Wave;
using VisualMusicBlockLooper.Scene;
using VisualMusicBlockLooper.Vision;

namespace VisualMusicBlockLooper.Audio
{
    internal sealed class LoopMixer : ISampleProvider
    {
        public const int SampleRate = 44100;
        public const int Channels = 2;
        private const long SamplesPerMinute = 60L * SampleRate;

        private readonly float[][][] columns;
        private readonly int bpm;
        private readonly List<Voice> voices = new List<Voice>();
        private long position;
        private long nextStep;

        public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(SampleRate, Channels);

        public LoopMixer(float[][][] columns, int bpm)
        {
            if (columns.Length != GridLayout.Columns)
                throw new ArgumentException("La sequenza deve avere otto colonne.", nameof(columns));
            if (bpm < SceneSettings.MinimumBpm || bpm > SceneSettings.MaximumBpm)
                throw new ArgumentOutOfRangeException(nameof(bpm));

            this.columns = columns;
            this.bpm = bpm;
        }

        private long FrameAtStep(long step) => (step * SamplesPerMinute + bpm / 2) / bpm;

        public int ColumnAtFrame(long frame)
        {
            long step = frame * bpm / SamplesPerMinute;
            if (FrameAtStep(step + 1) <= frame)
                ++step;
            return (int)(step % GridLayout.Columns);
        }

        public int Read(float[] buffer, int offset, int count)
        {
            if (count % Channels != 0)
                throw new ArgumentException("Il buffer deve contenere frame stereo interi.", nameof(count));

            Array.Clear(buffer, offset, count);
            long end = position + count / Channels;

            while (FrameAtStep(nextStep) < end)
            {
                long start = FrameAtStep(nextStep);
                foreach (var clip in columns[nextStep % GridLayout.Columns])
                    voices.Add(new Voice(start, clip));
                ++nextStep;
            }

            for (int index = voices.Count - 1; index >= 0; index--)
            {
                var voice = voices[index];
                int from = checked((int)(Math.Max(0, position - voice.Start) * Channels));
                int to = checked((int)(Math.Max(0, voice.Start - position) * Channels));
                int length = Math.Min(voice.Samples.Length - from, count - to);
                for (int sample = 0; sample < length; sample++)
                    buffer[offset + to + sample] += voice.Samples[from + sample];

                if (from + length == voice.Samples.Length)
                    voices.RemoveAt(index);
            }

            for (int index = offset; index < offset + count; index++)
                buffer[index] = Math.Max(-1, Math.Min(1, buffer[index]));

            position = end;
            return count;
        }

        private struct Voice
        {
            public readonly long Start;
            public readonly float[] Samples;

            public Voice(long start, float[] samples)
            {
                Start = start;
                Samples = samples;
            }
        }
    }
}
