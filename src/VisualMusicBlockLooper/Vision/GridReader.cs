using System;
using System.Collections.Generic;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using OpenCvSharp;
using OpenCvSharp.Aruco;

namespace VisualMusicBlockLooper.Vision
{
    internal sealed class GridCell
    {
        public int Row { get; }
        public int Column { get; }
        public Point2f[] Outline { get; }
        public byte[] Png { get; }

        public GridCell(int row, int column, Point2f[] outline, byte[] png)
        {
            Row = row;
            Column = column;
            Outline = outline;
            Png = png;
        }
    }

    internal static class GridReader
    {
        private const int PixelsPerMillimeter = 5;
        private const int CropInset = 2;
        private const string InvalidGrid = "Inquadra il foglio intero con tutti e quattro i marcatori e acquisisci di nuovo.";

        public static GridCell[] Read(System.Drawing.Bitmap photo)
        {
            using (var png = new MemoryStream())
            {
                photo.Save(png, ImageFormat.Png);
                using (var frame = Cv2.ImDecode(png.ToArray(), ImreadModes.Color))
                    return Read(frame);
            }
        }

        public static GridCell[] Read(Mat frame)
        {
            using (var dictionary = CvAruco.GetPredefinedDictionary(GridLayout.Dictionary))
            {
                var detector = new DetectorParameters
                {
                    CornerRefinementMethod = CornerRefineMethod.Subpix
                };
                CvAruco.DetectMarkers(frame, dictionary, out var markers, out var ids, detector, out _);
                var observed = new List<Point2d>(GridLayout.MarkerCount * 4);
                var expected = new List<Point2d>(GridLayout.MarkerCount * 4);

                for (int id = 0; id < GridLayout.MarkerCount; id++)
                {
                    var matches = Enumerable.Range(0, ids.Length).Where(index => ids[index] == id).ToArray();
                    if (matches.Length != 1)
                        throw new InvalidOperationException(InvalidGrid);

                    observed.AddRange(markers[matches[0]].Select(point => new Point2d(point.X, point.Y)));
                    expected.AddRange(GridLayout.MarkerCorners(id).Select(point =>
                        new Point2d(point.X * PixelsPerMillimeter, point.Y * PixelsPerMillimeter)));
                }

                using (var toPage = Cv2.FindHomography(observed, expected))
                using (var toPhoto = new Mat())
                using (var page = new Mat())
                {
                    if (toPage.Empty() || Cv2.Invert(toPage, toPhoto) == 0)
                        throw new InvalidOperationException(InvalidGrid);

                    var fitted = Cv2.PerspectiveTransform(observed, toPage);
                    if (fitted.Where((point, i) => point.DistanceTo(expected[i]) > PixelsPerMillimeter * 2).Any())
                        throw new InvalidOperationException(InvalidGrid);

                    Cv2.WarpPerspective(frame, page, toPage,
                        new Size(GridLayout.Width * PixelsPerMillimeter, GridLayout.Height * PixelsPerMillimeter));
                    return ReadCells(page, toPhoto, frame.Size());
                }
            }
        }

        private static GridCell[] ReadCells(Mat page, Mat toPhoto, Size photoSize)
        {
            var cells = new GridCell[GridLayout.Rows * GridLayout.Columns];
            for (int row = 0; row < GridLayout.Rows; row++)
            for (int column = 0; column < GridLayout.Columns; column++)
            {
                var cell = GridLayout.Cell(row, column);
                var outline = GridLayout.Corners(cell).Select(p => p * PixelsPerMillimeter);
                var projected = Cv2.PerspectiveTransform(outline, toPhoto);
                if (projected.Any(p => !(p.X >= 0 && p.X < photoSize.Width && p.Y >= 0 && p.Y < photoSize.Height)))
                    throw new InvalidOperationException(InvalidGrid);

                var crop = new Rect((cell.Left + CropInset) * PixelsPerMillimeter,
                    (cell.Top + CropInset) * PixelsPerMillimeter,
                    (cell.Width - 2 * CropInset) * PixelsPerMillimeter,
                    (cell.Height - 2 * CropInset) * PixelsPerMillimeter);

                using (var tile = new Mat(page, crop))
                {
                    byte[] png = null;
                    if (HasInk(tile))
                        Cv2.ImEncode(".png", tile, out png);
                    cells[row * GridLayout.Columns + column] = new GridCell(row, column, projected, png);
                }
            }

            return cells;
        }

        private static bool HasInk(Mat tile)
        {
            using (var gray = new Mat())
            using (var background = new Mat())
            using (var mask = new Mat())
            using (var labels = new Mat())
            using (var stats = new Mat())
            using (var centers = new Mat())
            {
                if (tile.Channels() == 1)
                    tile.CopyTo(gray);
                else
                    Cv2.CvtColor(tile, gray, ColorConversionCodes.BGR2GRAY);

                Cv2.GaussianBlur(gray, background, new Size(31, 31), 0);
                Cv2.Subtract(background, gray, mask);
                Cv2.Threshold(mask, mask, 9, 255, ThresholdTypes.Binary);
                int components = Cv2.ConnectedComponentsWithStats(mask, labels, stats, centers);
                for (int index = 1; index < components; index++)
                {
                    if (stats.At<int>(index, (int)ConnectedComponentsTypes.Area) >= 12)
                        return true;
                }

                return false;
            }
        }
    }
}
