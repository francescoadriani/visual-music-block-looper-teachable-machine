using System.Windows.Forms;

namespace VisualMusicBlockLooper
{
    internal sealed class MainForm : Form
    {
        public MainForm()
        {
            Text = "Visual Music Block Looper";
            WindowState = FormWindowState.Maximized;
            FormBorderStyle = FormBorderStyle.None;
            KeyPreview = true;
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
    }
}
