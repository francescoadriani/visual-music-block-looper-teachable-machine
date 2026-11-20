using System;
using System.Threading.Tasks;
using System.Windows.Forms;
using NAudio.Wave;
using VisualMusicBlockLooper.Audio;

namespace VisualMusicBlockLooper
{
    internal sealed partial class MainForm
    {
        private readonly Button start = ActionButton("Start", true);

        private string[] pattern;
        private LoopPlayer player;
        private int playingStep = -1;

        private void AddPlaybackControls()
        {
            start.Click += async (sender, args) => await TogglePlaybackAsync();
            toolbar.Controls.Add(start);
        }

        private async Task TogglePlaybackAsync()
        {
            if (player != null)
            {
                StopPlayback();
                PublishResults();
                return;
            }

            PublishResults();
            if (pattern == null || grid == null)
                return;

            var selected = pattern;
            var layout = grid.Layout;
            int bpm = scene.Bpm;
            string message = status.Text;
            SetBusy(true);
            status.Text = "Caricamento dei campioni WAV…";
            try
            {
                var prepared = await Task.Run(() =>
                {
                    var mixer = SoundSamples.CreateMixer(layout, selected, bpm, out var unavailable);
                    return (Mixer: mixer, Unavailable: unavailable);
                });
                player = new LoopPlayer(prepared.Mixer);
                player.Stopped += OnPlaybackStopped;
                player.Play();
                previewClock.Interval = layout.StepsPerBeat > 1 ? 25 : PreviewInterval;
                playingStep = 0;
                status.Text = prepared.Unavailable.Length == 0 ? message :
                    message + " · WAV saltati: " + string.Join(", ", prepared.Unavailable);
                picture.Invalidate();
            }
            catch (Exception error)
            {
                StopPlayback();
                status.Text = "Riproduzione: " + error.Message;
            }
            finally
            {
                SetBusy(false);
            }
        }

        private void UpdatePlaybackPosition()
        {
            if (player == null)
                return;

            try
            {
                var step = player.Step;
                if (step != playingStep)
                {
                    playingStep = step;
                    picture.Invalidate();
                }
            }
            catch (Exception error)
            {
                StopPlayback();
                status.Text = "Riproduzione: " + error.Message;
            }
        }

        private void OnPlaybackStopped(object sender, StoppedEventArgs args)
        {
            StopPlayback();
            status.Text = args.Exception == null ? "Riproduzione fermata." : "Riproduzione: " + args.Exception.Message;
        }

        private void StopPlayback()
        {
            if (player == null)
                return;

            player.Stopped -= OnPlaybackStopped;
            player.Dispose();
            player = null;
            playingStep = -1;
            previewClock.Interval = PreviewInterval;
            picture.Invalidate();
            UpdateButtons();
        }

        private void UpdatePlaybackButton()
        {
            start.Text = player == null ? "Start" : "Stop";
            start.Enabled = !busy && (player != null || pattern != null);
        }
    }
}
