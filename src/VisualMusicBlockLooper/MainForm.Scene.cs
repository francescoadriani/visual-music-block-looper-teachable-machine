using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using VisualMusicBlockLooper.Recognition;
using VisualMusicBlockLooper.Scene;

namespace VisualMusicBlockLooper
{
    internal sealed partial class MainForm
    {
        private readonly Button import = ActionButton("Importa modello");
        private readonly Button chooseSounds = ActionButton("Cartella WAV");
        private readonly NumericUpDown tempo = new NumericUpDown
        {
            Minimum = SceneSettings.MinimumBpm, Maximum = SceneSettings.MaximumBpm, Value = 120, Width = 65
        };
        private readonly NumericUpDown threshold = new NumericUpDown
        {
            Minimum = 0, Maximum = 100, Value = 85, Width = 62
        };
        private readonly FlowLayoutPanel mappings = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, Height = 85, AutoScroll = true, WrapContents = false, Visible = false
        };
        private readonly Label modelInfo = new Label
        {
            AutoSize = true, ForeColor = Accent, Margin = new Padding(5, 13, 8, 4)
        };
        private readonly SceneStore store = new SceneStore(Path.Combine(SceneStore.DefaultDirectory, "scena.json"));

        private ImageClassifier classifier;
        private SceneSettings scene;
        private SoundLibrary library;
        private double[][] scores;
        private bool restoring;

        private void AddSceneControls()
        {
            classifier = new ImageClassifier(this, Path.Combine(SceneStore.DefaultDirectory, "WebView2"));
            toolbar.Controls.Add(import);
            toolbar.Controls.Add(chooseSounds);
            AddTool(toolbar, "BPM", tempo);
            AddTool(toolbar, "Soglia %", threshold);
            toolbar.Controls.Add(modelInfo);

            import.Click += async (sender, args) =>
            {
                using (var dialog = new OpenFileDialog
                {
                    Filter = "Modello immagini Teachable Machine (*.zip)|*.zip",
                    Title = "Importa l'esportazione TensorFlow.js"
                })
                    if (dialog.ShowDialog(this) == DialogResult.OK)
                        await ImportAsync(dialog.FileName, true);
            };

            chooseSounds.Click += (sender, args) => SelectSoundsDirectory();
            tempo.ValueChanged += (sender, args) =>
            {
                if (restoring || scene == null)
                    return;
                StopPlayback();
                scene.Bpm = (int)tempo.Value;
                SaveScene();
            };
            threshold.ValueChanged += (sender, args) =>
            {
                if (restoring || scene == null)
                    return;
                StopPlayback();
                scene.ThresholdPercent = (int)threshold.Value;
                PublishResults();
                SaveScene();
            };
        }

        private string RestoreScene()
        {
            scene = store.Load(out var warning);
            tempo.Value = scene.Bpm;
            threshold.Value = scene.ThresholdPercent;
            try
            {
                library = SoundLibrary.Load(scene.SoundsDirectory);
            }
            catch (Exception error)
            {
                warning = JoinWarnings(warning, "Cartella WAV: " + error.Message);
            }
            return warning;
        }

        private async Task ImportAsync(string path, bool persist)
        {
            StopPlayback();
            SetBusy(true);
            status.Text = "Caricamento del modello…";
            bool accepted = false;
            try
            {
                if (!Path.IsPathRooted(path))
                    path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, path);
                var package = await Task.Run(() => ModelPackage.Load(path));
                await classifier.LoadAsync(package);
                accepted = true;
                scene.ModelPath = path;
                scores = null;
                pattern = null;
                modelInfo.Text = Path.GetFileNameWithoutExtension(path);
                BuildMappings();
                await RecognizeAsync();
            }
            catch (Exception error)
            {
                status.Text = "Modello: " + error.Message;
            }
            finally
            {
                if (accepted && persist)
                    SaveScene();
                SetBusy(false);
                picture.Invalidate();
            }
        }

        private async Task RecognizeAsync()
        {
            if (grid == null)
            {
                status.Text = "Modello importato. Acquisisci il foglio per riconoscere i simboli.";
                return;
            }
            if (classifier.Active == null)
            {
                status.Text = grid.Layout.Name + " acquisito. Importa il modello per riconoscere i simboli.";
                return;
            }

            status.Text = grid.Layout.Name + " · Riconoscimento dei simboli…";
            scores = null;
            scores = await classifier.PredictAsync(grid.Cells.Select(cell => cell.Png).ToArray());
            PublishResults();
        }

        private string RecognizedLabel(int index)
        {
            return scores == null ? null :
                CellMatch.Label(classifier.Active.Labels, scores[index], scene.ThresholdPercent);
        }

        private void PublishResults()
        {
            if (scores == null)
                return;

            int recognized = 0;
            int playable = 0;
            var routes = new string[scores.Length];
            var missing = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int index = 0; index < scores.Length; index++)
            {
                var label = RecognizedLabel(index);
                if (label == null)
                    continue;
                ++recognized;
                var assigned = scene.SoundFor(classifier.Active.Id, label);
                routes[index] = library?.Resolve(assigned);
                if (routes[index] != null)
                    ++playable;
                else if (assigned != null)
                    missing.Add(assigned);
            }

            pattern = playable == 0 ? null : routes;
            status.Text = grid.Layout.Name + " · " + (recognized == 0 ? "Nessun simbolo riconosciuto sopra soglia." :
                "Simboli riconosciuti: " + recognized + " · Caselle con suono: " + playable);
            if (missing.Count > 0)
                status.Text += " · WAV mancanti: " + string.Join(", ", missing);
            picture.Invalidate();
            UpdateButtons();
        }

        private void BuildMappings()
        {
            mappings.SuspendLayout();
            while (mappings.Controls.Count != 0)
            {
                var old = mappings.Controls[0];
                mappings.Controls.RemoveAt(0);
                old.Dispose();
            }

            var model = classifier.Active;
            if (model != null)
            {
                foreach (var label in model.Labels)
                    AddMappingControl(model.Id, label);
            }
            mappings.Visible = model != null;
            mappings.ResumeLayout();
        }

        private void AddMappingControl(string modelId, string label)
        {
            var pane = new Panel { Width = 202, Height = 59, Margin = new Padding(0, 3, 10, 3) };
            var name = new Label
            {
                Text = label, AutoEllipsis = true, Dock = DockStyle.Top, Height = 24,
                AccessibleName = label, ForeColor = Accent
            };
            var choices = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList, Width = pane.Width, Left = 0, Top = 25,
                AccessibleName = "Suono per " + label
            };

            var assigned = scene.SoundFor(modelId, label);
            choices.Items.Add(new SoundChoice(null, "Nessun suono"));
            if (library != null)
                foreach (var file in library.Names)
                    choices.Items.Add(new SoundChoice(file, file));

            if (assigned != null && library?.Resolve(assigned) == null)
                choices.Items.Add(new SoundChoice(assigned, "Mancante: " + assigned));

            choices.SelectedIndex = 0;
            for (int index = 1; index < choices.Items.Count; index++)
            {
                if (string.Equals(((SoundChoice)choices.Items[index]).Name, assigned, StringComparison.OrdinalIgnoreCase))
                {
                    choices.SelectedIndex = index;
                    break;
                }
            }
            bool empty = string.Equals(label.Trim(), "VUOTO", StringComparison.OrdinalIgnoreCase);
            if (empty)
                choices.SelectedIndex = 0;
            choices.Enabled = !empty;
            choices.SelectedIndexChanged += (sender, args) =>
            {
                StopPlayback();
                var sound = ((SoundChoice)choices.SelectedItem).Name;
                scene.Assign(modelId, label, sound);
                PublishResults();
                SaveScene();
                PreviewSound(sound);
            };
            pane.Controls.AddRange(new Control[] { name, choices });
            mappings.Controls.Add(pane);
        }

        private void SelectSoundsDirectory()
        {
            using (var dialog = new FolderBrowserDialog { Description = "Scegli la cartella dei campioni WAV" })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK)
                    return;

                try
                {
                    StopPlayback();
                    library = SoundLibrary.Load(dialog.SelectedPath);
                    scene.SoundsDirectory = dialog.SelectedPath;
                    BuildMappings();
                    PublishResults();
                    if (!library.Names.Any())
                        status.Text = "La cartella selezionata non contiene file WAV.";
                    SaveScene();
                }
                catch (Exception error)
                {
                    status.Text = "Cartella WAV: " + error.Message;
                }
            }
        }

        private void SaveScene()
        {
            try
            {
                store.Save(scene);
            }
            catch (Exception error)
            {
                status.Text = "Salvataggio della scena: " + error.Message;
            }
        }

        private static string JoinWarnings(string first, string second)
        {
            if (string.IsNullOrWhiteSpace(first))
                return second;
            if (string.IsNullOrWhiteSpace(second))
                return first;
            return first + " " + second;
        }

        private sealed class SoundChoice
        {
            public string Name { get; }
            private readonly string display;

            public SoundChoice(string name, string display)
            {
                Name = name;
                this.display = display;
            }

            public override string ToString() => display;
        }
    }
}
