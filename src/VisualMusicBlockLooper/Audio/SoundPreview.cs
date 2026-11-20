using System;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace VisualMusicBlockLooper.Audio
{
    internal sealed class SoundPreview : IDisposable
    {
        private readonly WaveFileReader wave;
        private readonly WaveOutEvent output;

        public event EventHandler<StoppedEventArgs> Stopped;

        public SoundPreview(string path)
        {
            wave = new WaveFileReader(path);
            try
            {
                output = new WaveOutEvent { DesiredLatency = 85, NumberOfBuffers = 3 };
                var samples = new VolumeSampleProvider(SoundSamples.ToMixerFormat(wave)) { Volume = 0.5f };
                output.Init(new SampleToWaveProvider16(samples));
                output.PlaybackStopped += (sender, args) => Stopped?.Invoke(this, args);
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        public void Play() => output.Play();

        public void Dispose()
        {
            try
            {
                output?.Dispose();
            }
            finally
            {
                wave.Dispose();
            }
        }
    }
}
