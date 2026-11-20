using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using NAudio.Wave;
using VisualMusicBlockLooper.Audio;
using VisualMusicBlockLooper.Vision;

namespace VisualMusicBlockLooper
{
    internal sealed partial class MainForm
    {
        private readonly Button start = ActionButton("Start", true);

        private string[] pattern;
        private LoopPlayer player;
        private int playingColumn = -1;

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

            if (pattern == null)
                return;

            var selected = pattern;
            int bpm = scene.Bpm;
            string message = status.Text;
            SetBusy(true);
            status.Text = "Caricamento dei campioni WAV…";
            try
            {
                var mixer = await Task.Run(() => LoadMixer(selected, bpm));
                player = new LoopPlayer(mixer);
                player.Stopped += OnPlaybackStopped;
                player.Play();
                playingColumn = 0;
                status.Text = message;
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

        private static LoopMixer LoadMixer(string[] selected, int bpm)
        {
            var cache = selected.Where(path => path != null).Distinct(StringComparer.OrdinalIgnoreCase)
                .ToDictionary(path => path, SoundSamples.Read, StringComparer.OrdinalIgnoreCase);
            var columns = new float[GridLayout.Columns][][];
            for (int column = 0; column < GridLayout.Columns; column++)
            {
                var voices = new List<float[]>(GridLayout.Rows);
                for (int row = 0; row < GridLayout.Rows; row++)
                {
                    var path = selected[row * GridLayout.Columns + column];
                    if (path != null)
                        voices.Add(cache[path]);
                }
                columns[column] = voices.ToArray();
            }
            return new LoopMixer(columns, bpm);
        }

        private void UpdatePlaybackPosition()
        {
            if (player == null)
                return;

            try
            {
                var column = player.Column;
                if (column != playingColumn)
                {
                    playingColumn = column;
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
            playingColumn = -1;
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
