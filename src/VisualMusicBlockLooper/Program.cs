using System;
using System.IO;
using System.Windows.Forms;
using VisualMusicBlockLooper.Vision;

namespace VisualMusicBlockLooper
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            if (args.Length == 2 && args[0] == "--export-grid")
            {
                File.WriteAllText(args[1], GridLayout.Original.CreateSvg());
                return;
            }

            if (args.Length == 2 && args[0] == "--export-grid-32")
            {
                File.WriteAllText(args[1], GridLayout.Detailed.CreateSvg());
                return;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }
}
