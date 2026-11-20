using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using AForge.Video.DirectShow;
using VisualMusicBlockLooper.Vision;

namespace VisualMusicBlockLooper
{
    internal sealed partial class MainForm : Form
    {
        private static readonly Color Canvas = Color.FromArgb(14, 22, 32);
        private static readonly Color Surface = Color.FromArgb(23, 35, 48);
        private static readonly Color Foreground = Color.FromArgb(233, 241, 247);
        private static readonly Color Accent = Color.FromArgb(94, 234, 190);

        private readonly ComboBox cameras = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 250 };
        private readonly Button capture = ActionButton("Acquisisci", true);
        private readonly Button resume = ActionButton("Torna alla webcam");
        private readonly Label status = new Label { AutoSize = true, ForeColor = Foreground };
        private readonly PictureBox picture = new PictureBox
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(8, 13, 20),
            SizeMode = PictureBoxSizeMode.Zoom,
            AccessibleName = "Foto o anteprima della webcam"
        };
        private readonly FlowLayoutPanel toolbar = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, AutoSize = true, WrapContents = true
        };
        private readonly TableLayoutPanel layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Padding = new Padding(24, 12, 24, 12)
        };
        private readonly Timer previewClock = new Timer { Interval = 66 };

        private Webcam webcam;
        private bool frozen;
        private bool busy;
        private GridCell[] cells;

        public MainForm()
        {
            Text = "Visual Music Block Looper";
            Font = new Font("Segoe UI", 11);
            BackColor = Canvas;
            ForeColor = Foreground;
            MinimumSize = new Size(780, 550);
            WindowState = FormWindowState.Maximized;
            FormBorderStyle = FormBorderStyle.None;
            KeyPreview = true;

            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 60));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var title = new Label
            {
                Text = Text, Font = new Font("Segoe UI Semibold", 24),
                Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft
            };
            AddTool(toolbar, "Webcam", cameras);
            AddSceneControls();
            status.Margin = new Padding(14, 16, 4, 6);
            var footer = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true };
            footer.Controls.AddRange(new Control[] { capture, resume, status });
            layout.Controls.Add(title, 0, 0);
            layout.Controls.Add(toolbar, 0, 1);
            layout.Controls.Add(mappings, 0, 2);
            layout.Controls.Add(picture, 0, 3);
            layout.Controls.Add(footer, 0, 4);
            Controls.Add(layout);

            Shown += async (s, e) => await InitializeAsync();
            cameras.SelectedIndexChanged += async (s, e) => await SelectCameraAsync();
            capture.Click += async (s, e) => await CaptureAsync();
            resume.Click += (s, e) => ReturnToWebcam();
            picture.Paint += DrawOverlay;
            previewClock.Tick += (s, e) => ShowPreview();
            UpdateButtons();
        }

        private static Button ActionButton(string text, bool primary = false) => new Button
        {
            Text = text, AutoSize = true, Height = 38, MinimumSize = new Size(100, 38),
            Padding = new Padding(10, 1, 10, 1), Margin = new Padding(0, 7, 8, 6),
            BackColor = primary ? Accent : Surface, ForeColor = primary ? Canvas : Foreground,
            UseVisualStyleBackColor = false, FlatStyle = FlatStyle.Flat
        };

        private static void AddTool(FlowLayoutPanel parent, string label, Control control)
        {
            control.AccessibleName = label;
            var caption = new Label { Text = label, AutoSize = true, Margin = new Padding(0, 11, 7, 6) };
            control.Margin = new Padding(0, 7, 14, 7);
            parent.Controls.AddRange(new[] { caption, control });
        }

        private async Task InitializeAsync()
        {
            restoring = true;
            var warning = RestoreScene();
            try
            {
                var devices = Webcam.Devices();
                cameras.DisplayMember = nameof(FilterInfo.Name);
                cameras.Items.AddRange(devices);
                var preferred = Array.FindIndex(devices, device => device.MonikerString == scene.CameraId);
                if (devices.Length > 0)
                    cameras.SelectedIndex = Math.Max(0, preferred);
                else
                    warning = JoinWarnings(warning, "Collega una webcam e riapri l'app.");
                if (preferred < 0 && scene.CameraId != null)
                    warning = JoinWarnings(warning, "La webcam salvata non è disponibile.");
            }
            catch (Exception error)
            {
                warning = JoinWarnings(warning, "Webcam: " + error.Message);
            }

            restoring = false;
            await SelectCameraAsync(false);
            if (!string.IsNullOrWhiteSpace(scene.ModelPath))
                await ImportAsync(scene.ModelPath, false);
            if (warning != null)
                status.Text = JoinWarnings(warning, status.Text);
            previewClock.Start();
        }

        private async Task SelectCameraAsync(bool persist = true)
        {
            if (busy || restoring || !(cameras.SelectedItem is FilterInfo selected))
                return;

            ReturnToWebcam();
            SetBusy(true);
            status.Text = "Apertura webcam…";
            var previous = webcam;
            webcam = null;
            try
            {
                webcam = await Task.Run(() =>
                {
                    previous?.Dispose();
                    return new Webcam(selected.MonikerString);
                });
                status.Text = "In attesa della webcam…";
                if (persist)
                {
                    scene.CameraId = selected.MonikerString;
                    SaveScene();
                }
            }
            catch (Exception error)
            {
                status.Text = "Webcam: " + error.Message;
            }
            finally
            {
                SetBusy(false);
            }
        }

        private void ShowPreview()
        {
            if (busy || frozen || webcam == null)
                return;

            var frame = webcam.Snapshot();
            if (frame != null)
            {
                if (picture.Image == null && status.Text == "In attesa della webcam…")
                    status.Text = "Inquadra tutti e quattro i marcatori.";
                SetImage(frame);
            }
            else if (webcam.Error != null)
            {
                SetImage(null);
                status.Text = "Webcam: " + webcam.Error;
            }

            UpdateButtons();
        }

        private void ReturnToWebcam()
        {
            frozen = false;
            cells = null;
            scores = null;
            SetImage(null);
            status.Text = "In attesa della webcam…";
            UpdateButtons();
        }

        private async Task CaptureAsync()
        {
            using (var frame = webcam?.Snapshot())
            {
                if (frame == null)
                    return;

                frozen = true;
                cells = null;
                scores = null;
                SetImage((Bitmap)frame.Clone());
                SetBusy(true);
                status.Text = "Lettura della griglia…";
                try
                {
                    cells = await Task.Run(() => GridReader.Read(frame));
                    await RecognizeAsync();
                }
                catch (Exception error)
                {
                    status.Text = error.Message;
                }
                finally
                {
                    picture.Invalidate();
                    SetBusy(false);
                }
            }
        }

        private void SetImage(Image image)
        {
            var previous = picture.Image;
            picture.Image = image;
            previous?.Dispose();
        }

        private void SetBusy(bool value)
        {
            busy = value;
            UpdateButtons();
        }

        private void UpdateButtons()
        {
            cameras.Enabled = !busy;
            capture.Enabled = !busy && webcam != null && picture.Image != null;
            resume.Enabled = !busy && frozen;
            import.Enabled = !busy;
            chooseSounds.Enabled = !busy;
            mappings.Enabled = !busy;
            tempo.Enabled = !busy;
            threshold.Enabled = !busy;
        }

        private void DrawOverlay(object sender, PaintEventArgs e)
        {
            if (picture.Image == null || cells == null)
                return;

            float scale = Math.Min((float)picture.Width / picture.Image.Width, (float)picture.Height / picture.Image.Height);
            float left = (picture.Width - picture.Image.Width * scale) / 2;
            float top = (picture.Height - picture.Image.Height * scale) / 2;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var pen = new Pen(Accent, 2))
            {
                for (int index = 0; index < cells.Length; index++)
                {
                    var cell = cells[index];
                    if (cell.Png == null)
                        continue;

                    var label = RecognizedLabel(index);
                    if (scores != null && label == null)
                        continue;
                    var polygon = Array.ConvertAll(cell.Outline, point =>
                        new PointF(left + point.X * scale, top + point.Y * scale));
                    e.Graphics.DrawPolygon(pen, polygon);
                    if (label == null)
                        continue;

                    var sound = scene.SoundFor(classifier.Active.Id, label);
                    var text = library?.Resolve(sound) == null
                        ? label + " · Nessun suono" : Path.GetFileNameWithoutExtension(sound);
                    float x = polygon.Min(point => point.X) + 3;
                    float y = polygon.Average(point => point.Y);
                    int width = Math.Max(10, (int)(polygon.Max(point => point.X) - x));
                    var bounds = new Rectangle((int)x, (int)y, width, Font.Height + 4);
                    using (var brush = new SolidBrush(Color.FromArgb(224, Canvas)))
                        e.Graphics.FillRectangle(brush, bounds);
                    TextRenderer.DrawText(e.Graphics, text, Font, bounds, Foreground,
                        TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
                }
            }
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape)
            {
                FormBorderStyle = FormBorderStyle.Sizable;
                WindowState = FormWindowState.Normal;
                e.Handled = true;
            }

            base.OnKeyDown(e);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (busy && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                return;
            }

            previewClock.Dispose();
            classifier.Dispose();
            webcam?.Dispose();
            SetImage(null);
            base.OnFormClosing(e);
        }
    }
}
