using System.Globalization;
using System.Text;
using OpenCvSharp;
using OpenCvSharp.Aruco;

namespace VisualMusicBlockLooper.Vision
{
    internal static class GridLayout
    {
        public const int Width = 297;
        public const int Height = 210;
        public const int Rows = 4;
        public const int Columns = 8;
        public const int CellSize = 27;
        public const int Left = 40;
        public const int Top = 51;
        public const int MarkerSize = 18;
        public const PredefinedDictionaryName Dictionary = PredefinedDictionaryName.Dict4X4_50;

        private static readonly Point[] MarkerOrigins =
        {
            new Point(14, 26), new Point(265, 26), new Point(265, 166), new Point(14, 166)
        };

        public static int MarkerCount => MarkerOrigins.Length;

        public static Rect Cell(int row, int column) =>
            new Rect(Left + column * CellSize, Top + row * CellSize, CellSize, CellSize);

        public static Point2f[] MarkerCorners(int id) =>
            Corners(new Rect(MarkerOrigins[id].X, MarkerOrigins[id].Y, MarkerSize, MarkerSize));

        public static Point2f[] Corners(Rect rectangle) => new[]
        {
            new Point2f(rectangle.Left, rectangle.Top), new Point2f(rectangle.Right, rectangle.Top),
            new Point2f(rectangle.Right, rectangle.Bottom), new Point2f(rectangle.Left, rectangle.Bottom)
        };

        public static string CreateSvg()
        {
            var svg = new StringBuilder();
            svg.AppendLine("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"297mm\" height=\"210mm\" viewBox=\"0 0 297 210\">");
            Rectangle(svg, new Rect(0, 0, Width, Height), "#fff");
            svg.AppendLine("  <text x=\"40\" y=\"39\" font-family=\"sans-serif\" font-size=\"8\" font-weight=\"bold\">Visual Music Block Looper</text>");
            svg.AppendLine("  <text x=\"40\" y=\"178\" font-family=\"sans-serif\" font-size=\"4\">Disegna un simbolo per casella. Lascia in bianco le pause.</text>");
            svg.AppendLine("  <text x=\"40\" y=\"185\" font-family=\"sans-serif\" font-size=\"4\">Inquadra tutti e quattro i marcatori. Il tempo scorre da sinistra a destra.</text>");

            using (var dictionary = CvAruco.GetPredefinedDictionary(Dictionary))
            using (var marker = new Mat())
            {
                int modules = dictionary.MarkerSize + 2;
                int size = MarkerSize / modules;
                for (int id = 0; id < MarkerCount; id++)
                {
                    dictionary.GenerateImageMarker(id, modules, marker, 1);
                    var origin = MarkerOrigins[id];
                    for (int y = 0; y < modules; y++)
                    for (int x = 0; x < modules; x++)
                    {
                        if (marker.At<byte>(y, x) == 0)
                            Rectangle(svg, new Rect(origin.X + x * size, origin.Y + y * size, size, size), "#000");
                    }
                }
            }

            Rectangle(svg, new Rect(Left, Top, Columns * CellSize, Rows * CellSize), "none");
            for (int column = 1; column < Columns; column++)
                Rectangle(svg, new Rect(Left + column * CellSize, Top, 0, Rows * CellSize), "none");
            for (int row = 1; row < Rows; row++)
                Rectangle(svg, new Rect(Left, Top + row * CellSize, Columns * CellSize, 0), "none");
            svg.AppendLine("</svg>");
            return svg.ToString().Replace("\r\n", "\n");
        }

        private static void Rectangle(StringBuilder svg, Rect rectangle, string fill)
        {
            var stroke = fill == "none" ? " stroke=\"#000\" stroke-width=\"0.4\"" : "";
            if (rectangle.Width == 0 || rectangle.Height == 0)
            {
                svg.AppendFormat(CultureInfo.InvariantCulture,
                    "  <line x1=\"{0}\" y1=\"{1}\" x2=\"{2}\" y2=\"{3}\"{4} />\n",
                    rectangle.Left, rectangle.Top, rectangle.Right, rectangle.Bottom, stroke);
                return;
            }

            svg.AppendFormat(CultureInfo.InvariantCulture,
                "  <rect x=\"{0}\" y=\"{1}\" width=\"{2}\" height=\"{3}\" fill=\"{4}\"{5} />\n",
                rectangle.X, rectangle.Y, rectangle.Width, rectangle.Height, fill, stroke);
        }
    }
}
