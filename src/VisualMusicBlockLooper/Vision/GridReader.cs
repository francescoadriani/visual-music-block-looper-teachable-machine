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
        public int Step { get; }
        public Point2f[] Outline { get; }
        public byte[] Png { get; }

        public GridCell(int step, Point2f[] outline, byte[] png)
        {
            Step = step;
            Outline = outline;
            Png = png;
        }
    }

    internal sealed class GridCapture
    {
        public GridLayout Layout { get; }
        public GridCell[] Cells { get; }

        public GridCapture(GridLayout layout, GridCell[] cells)
        {
            Layout = layout;
            Cells = cells;
        }
    }

    internal static class GridReader
    {
        private const int PixelsPerMillimeter = 5;
        private const string InvalidGrid = "Inquadra un solo foglio con tutti e quattro i marcatori e acquisisci di nuovo.";

        public static GridCapture Read(System.Drawing.Bitmap photo)
        {
            using (var png = new MemoryStream())
            {
                photo.Save(png, ImageFormat.Png);
                using (var frame = Cv2.ImDecode(png.ToArray(), ImreadModes.Color))
                    return Read(frame);
            }
        }

        public static GridCapture Read(Mat frame)
        {
            using (var dictionary = CvAruco.GetPredefinedDictionary(GridLayout.Dictionary))
            {
                var detector = new DetectorParameters
                {
                    CornerRefinementMethod = CornerRefineMethod.Subpix
                };
                CvAruco.DetectMarkers(frame, dictionary, out var markers, out var ids, detector, out _);
                var layout = GridLayout.FromMarkers(ids);
                if (layout == null)
                    throw new InvalidOperationException(InvalidGrid);

                var observed = new List<Point2d>(GridLayout.MarkerCount * 4);
                var expected = new List<Point2d>(GridLayout.MarkerCount * 4);

                for (int index = 0; index < GridLayout.MarkerCount; index++)
                {
                    int match = Array.IndexOf(ids, layout.MarkerId(index));
                    observed.AddRange(markers[match].Select(point => new Point2d(point.X, point.Y)));
                    expected.AddRange(GridLayout.MarkerCorners(index).Select(point =>
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
                    return new GridCapture(layout, ReadCells(layout, page, toPhoto, frame.Size()));
                }
            }
        }

        private static GridCell[] ReadCells(GridLayout layout, Mat page, Mat toPhoto, Size photoSize)
        {
            var cells = new GridCell[layout.Rows * layout.Columns];
            int inset = layout.CropInsetPixels;
            for (int row = 0; row < layout.Rows; row++)
            for (int column = 0; column < layout.Columns; column++)
            {
                var cell = layout.Cell(row, column);
                var outline = GridLayout.Corners(cell).Select(p => p * PixelsPerMillimeter);
                var projected = Cv2.PerspectiveTransform(outline, toPhoto);
                if (projected.Any(p => !(p.X >= 0 && p.X < photoSize.Width && p.Y >= 0 && p.Y < photoSize.Height)))
                    throw new InvalidOperationException(InvalidGrid);

                var crop = new Rect(cell.Left * PixelsPerMillimeter + inset,
                    cell.Top * PixelsPerMillimeter + inset,
                    cell.Width * PixelsPerMillimeter - 2 * inset,
                    cell.Height * PixelsPerMillimeter - 2 * inset);

                using (var tile = new Mat(page, crop))
                {
                    byte[] png = null;
                    if (HasInk(tile))
                        Cv2.ImEncode(".png", tile, out png);
                    cells[row * layout.Columns + column] = new GridCell(layout.StepAt(row, column), projected, png);
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
