using System;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace VisualMusicBlockLooper.Audio
{
    internal sealed class LoopPlayer : IDisposable
    {
        private readonly LoopMixer mixer;
        private readonly WaveOutEvent output = new WaveOutEvent { DesiredLatency = 85, NumberOfBuffers = 3 };

        public event EventHandler<StoppedEventArgs> Stopped
        {
            add { output.PlaybackStopped += value; }
            remove { output.PlaybackStopped -= value; }
        }

        public LoopPlayer(LoopMixer mixer)
        {
            this.mixer = mixer;
            try
            {
                output.Init(new SampleToWaveProvider16(mixer));
            }
            catch
            {
                output.Dispose();
                throw;
            }
        }

        public void Play() => output.Play();

        public int Step => mixer.StepAtFrame(output.GetPosition() / output.OutputWaveFormat.BlockAlign);

        public void Dispose() => output.Dispose();
    }
}
