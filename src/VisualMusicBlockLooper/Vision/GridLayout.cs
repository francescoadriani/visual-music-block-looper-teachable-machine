using System.Globalization;
using System.Linq;
using System.Text;
using OpenCvSharp;
using OpenCvSharp.Aruco;

namespace VisualMusicBlockLooper.Vision
{
    internal sealed class GridLayout
    {
        public const int Width = 297;
        public const int Height = 210;
        public const int Lanes = 4;
        public const int MarkerSize = 18;
        public const PredefinedDictionaryName Dictionary = PredefinedDictionaryName.Dict4X4_50;

        private static readonly Point[] MarkerOrigins =
        {
            new Point(14, 26), new Point(265, 26), new Point(265, 166), new Point(14, 166)
        };

        public static readonly GridLayout Original = new GridLayout(firstMarker: 0, blocks: 1, columns: 8,
            stepsPerBeat: 1, cellSize: 27, left: 40, top: 51, blockGap: 0, cropInsetPixels: 10, name: "Foglio 1");
        public static readonly GridLayout Detailed = new GridLayout(firstMarker: 4, blocks: 2, columns: 16,
            stepsPerBeat: 4, cellSize: 14, left: 36, top: 37, blockGap: 16, cropInsetPixels: 7, name: "Foglio 2");

        private readonly int firstMarker;
        private readonly int blocks;
        private readonly int cellSize;
        private readonly int left;
        private readonly int top;
        private readonly int blockGap;

        public int Rows => blocks * Lanes;
        public int Columns { get; }
        public int StepCount => blocks * Columns;
        public int StepsPerBeat { get; }
        public int CropInsetPixels { get; }
        public string Name { get; }
        public static int MarkerCount => MarkerOrigins.Length;

        private GridLayout(int firstMarker, int blocks, int columns, int stepsPerBeat,
            int cellSize, int left, int top, int blockGap, int cropInsetPixels, string name)
        {
            this.firstMarker = firstMarker;
            this.blocks = blocks;
            this.cellSize = cellSize;
            this.left = left;
            this.top = top;
            this.blockGap = blockGap;
            Columns = columns;
            StepsPerBeat = stepsPerBeat;
            CropInsetPixels = cropInsetPixels;
            Name = name;
        }

        private bool OwnsMarker(int id) => id >= firstMarker && id < firstMarker + MarkerCount;

        public static bool IsSheetMarker(int id) => Original.OwnsMarker(id) || Detailed.OwnsMarker(id);

        public static GridLayout FromMarkers(int[] ids)
        {
            var known = ids.Where(IsSheetMarker).ToArray();
            if (known.Length != MarkerCount || known.Distinct().Count() != MarkerCount)
                return null;
            if (known.All(Original.OwnsMarker))
                return Original;
            return known.All(Detailed.OwnsMarker) ? Detailed : null;
        }

        public int MarkerId(int index) => firstMarker + index;

        public int StepAt(int row, int column) => row / Lanes * Columns + column;

        public Rect Cell(int row, int column) =>
            new Rect(left + column * cellSize, top + row * cellSize + row / Lanes * blockGap, cellSize, cellSize);

        public static Point2f[] MarkerCorners(int index) =>
            Corners(new Rect(MarkerOrigins[index].X, MarkerOrigins[index].Y, MarkerSize, MarkerSize));

        public static Point2f[] Corners(Rect rectangle) => new[]
        {
            new Point2f(rectangle.Left, rectangle.Top), new Point2f(rectangle.Right, rectangle.Top),
            new Point2f(rectangle.Right, rectangle.Bottom), new Point2f(rectangle.Left, rectangle.Bottom)
        };

        public string CreateSvg()
        {
            var svg = new StringBuilder();
            svg.AppendLine("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"297mm\" height=\"210mm\" viewBox=\"0 0 297 210\">");
            Rectangle(svg, new Rect(0, 0, Width, Height), "#fff");
            if (blocks == 1)
            {
                Caption(svg, left, 39, "Visual Music Block Looper", true);
                Caption(svg, left, 178, "Disegna un simbolo per casella. Lascia in bianco le pause.");
                Caption(svg, left, 185, "Inquadra tutti e quattro i marcatori. Il tempo scorre da sinistra a destra.");
            }
            else
            {
                Caption(svg, left, 22, "Visual Music Block Looper - Foglio 2", true);
                Caption(svg, left, 33, "Blocco 1 - passi 1-16");
                Caption(svg, left, 105, "Blocco 2 - passi 17-32");
                Caption(svg, left, 186, "Disegna un simbolo per casella. Lascia in bianco le pause.");
                Caption(svg, left, 193, "Inquadra tutti e quattro i marcatori. Ogni casella dura un sedicesimo.");
            }

            using (var dictionary = CvAruco.GetPredefinedDictionary(Dictionary))
            using (var marker = new Mat())
            {
                int modules = dictionary.MarkerSize + 2;
                int size = MarkerSize / modules;
                for (int index = 0; index < MarkerCount; index++)
                {
                    dictionary.GenerateImageMarker(MarkerId(index), modules, marker, 1);
                    var origin = MarkerOrigins[index];
                    for (int y = 0; y < modules; y++)
                    for (int x = 0; x < modules; x++)
                    {
                        if (marker.At<byte>(y, x) == 0)
                            Rectangle(svg, new Rect(origin.X + x * size, origin.Y + y * size, size, size), "#000");
                    }
                }
            }

            for (int block = 0; block < blocks; block++)
            {
                var start = Cell(block * Lanes, 0);
                Rectangle(svg, new Rect(start.X, start.Y, Columns * cellSize, Lanes * cellSize), "none");
                for (int column = 1; column < Columns; column++)
                {
                    var weight = StepsPerBeat > 1 && column % StepsPerBeat == 0 ? "0.7" : "0.4";
                    Rectangle(svg, new Rect(start.X + column * cellSize, start.Y, 0, Lanes * cellSize), "none", weight);
                }
                for (int row = 1; row < Lanes; row++)
                    Rectangle(svg, new Rect(start.X, start.Y + row * cellSize, Columns * cellSize, 0), "none");
            }
            svg.AppendLine("</svg>");
            return svg.ToString().Replace("\r\n", "\n");
        }

        private static void Caption(StringBuilder svg, int x, int y, string text, bool title = false)
        {
            svg.AppendFormat(CultureInfo.InvariantCulture, "  <text x=\"{0}\" y=\"{1}\" font-family=\"sans-serif\" font-size=\"{2}\"{3}>{4}</text>\n",
                x, y, title ? 8 : 4, title ? " font-weight=\"bold\"" : "", text);
        }

        private static void Rectangle(StringBuilder svg, Rect rectangle, string fill, string weight = "0.4")
        {
            var stroke = fill == "none" ? " stroke=\"#000\" stroke-width=\"" + weight + "\"" : "";
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
