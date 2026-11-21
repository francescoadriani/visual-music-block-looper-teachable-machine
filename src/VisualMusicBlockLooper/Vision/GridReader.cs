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
        // Both templates leave clear bands between the side markers and the printed grid.
        private const int LeftPaperMargin = 33;
        private const int RightPaperMargin = 261;
        private const double WhiteTarget = 242;
        private const double WhiteBalanceStrength = 0.75;
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
                    layout = RecoverMarkers(frame, dictionary, detector, ref markers, ref ids);
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

        private static GridLayout RecoverMarkers(Mat frame, Dictionary dictionary, DetectorParameters detector,
            ref Point2f[][] markers, ref int[] ids)
        {
            var known = ids.Where(GridLayout.IsSheetMarker).ToArray();
            if (known.Length >= GridLayout.MarkerCount || known.Distinct().Count() != known.Length)
                return null;

            double scale = Math.Min(2, 2560.0 / Math.Max(frame.Width, frame.Height));
            if (scale <= 1)
                return null;

            using (var enlarged = new Mat())
            {
                Cv2.Resize(frame, enlarged, new Size(), scale, scale, InterpolationFlags.Cubic);
                CvAruco.DetectMarkers(enlarged, dictionary, out var recovered, out var recoveredIds, detector, out _);
                var recoveredKnown = recoveredIds.Where(GridLayout.IsSheetMarker).ToArray();
                if (recoveredKnown.Distinct().Count() != recoveredKnown.Length)
                    return null;

                var combinedIds = known.Union(recoveredKnown).ToArray();
                var layout = GridLayout.FromMarkers(combinedIds);
                if (layout == null)
                    return null;

                var combinedCorners = new Point2f[combinedIds.Length][];
                for (int index = 0; index < combinedIds.Length; index++)
                {
                    int id = combinedIds[index];
                    int original = Array.IndexOf(ids, id);
                    if (original >= 0)
                        combinedCorners[index] = markers[original];
                    else
                        combinedCorners[index] = recovered[Array.IndexOf(recoveredIds, id)]
                            .Select(point => new Point2f((float)(point.X / scale), (float)(point.Y / scale)))
                            .ToArray();
                }

                markers = combinedCorners;
                ids = combinedIds;
                return layout;
            }
        }

        private static GridCell[] ReadCells(GridLayout layout, Mat page, Mat toPhoto, Size photoSize)
        {
            var cells = new GridCell[layout.Rows * layout.Columns];
            int inset = layout.CropInsetPixels;
            for (int row = 0; row < layout.Rows; row++)
            {
                var first = layout.Cell(row, 0);
                var leftPaper = SamplePaper(page, first, LeftPaperMargin, 2);
                var rightPaper = SamplePaper(page, first, RightPaperMargin, 3);
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
                        // Fine texture occupies more rectified pixels when a small camera crop is enlarged.
                        int minimumInkArea = ShortestEdge(projected) < 20 ? 32 : 12;
                        if (HasInk(tile, minimumInkArea))
                            png = EncodeWithPaperWhite(tile, leftPaper, rightPaper, (column + 0.5) / layout.Columns);
                        cells[row * layout.Columns + column] = new GridCell(layout.StepAt(row, column), projected, png);
                    }
                }
            }

            return cells;
        }

        private static double ShortestEdge(Point2f[] outline)
        {
            return Math.Min(Math.Min(outline[0].DistanceTo(outline[1]), outline[1].DistanceTo(outline[2])),
                Math.Min(outline[2].DistanceTo(outline[3]), outline[3].DistanceTo(outline[0])));
        }

        private static double[] SamplePaper(Mat page, Rect cell, int margin, int width)
        {
            var band = new Rect(margin * PixelsPerMillimeter, (cell.Top + 1) * PixelsPerMillimeter,
                width * PixelsPerMillimeter, (cell.Height - 2) * PixelsPerMillimeter);
            int channels = page.Channels();
            int measuredChannels = Math.Min(3, channels);
            var histograms = new int[measuredChannels][];
            for (int channel = 0; channel < measuredChannels; channel++)
                histograms[channel] = new int[256];

            for (int y = band.Top; y < band.Bottom; y++)
            for (int x = band.Left; x < band.Right; x++)
            {
                if (channels == 1)
                    histograms[0][page.At<byte>(y, x)]++;
                else if (channels == 3)
                {
                    var color = page.At<Vec3b>(y, x);
                    histograms[0][color.Item0]++;
                    histograms[1][color.Item1]++;
                    histograms[2][color.Item2]++;
                }
                else
                {
                    var color = page.At<Vec4b>(y, x);
                    histograms[0][color.Item0]++;
                    histograms[1][color.Item1]++;
                    histograms[2][color.Item2]++;
                }
            }

            int middle = band.Width * band.Height / 2;
            var white = new double[measuredChannels];
            for (int channel = 0; channel < measuredChannels; channel++)
            {
                int seen = 0;
                int bin = 0;
                while (bin < 255 && (seen += histograms[channel][bin]) <= middle)
                    bin++;
                white[channel] = bin;
            }
            return white;
        }

        private static byte[] EncodeWithPaperWhite(Mat tile, double[] left, double[] right, double position)
        {
            using (var corrected = new Mat())
            {
                int channels = tile.Channels();
                using (var transform = new Mat(channels, channels, MatType.CV_64FC1, Scalar.All(0)))
                {
                    for (int channel = 0; channel < left.Length; channel++)
                    {
                        double white = left[channel] + (right[channel] - left[channel]) * position;
                        double gain = white < 16 ? 1 : Math.Min(2.4, WhiteTarget / white);
                        transform.Set(channel, channel, gain);
                    }
                    if (channels == 4)
                        transform.Set(3, 3, 1.0);

                    Cv2.Transform(tile, corrected, transform);
                }
                // Keep some original tonal detail when the correction saturates a highlight.
                Cv2.AddWeighted(corrected, WhiteBalanceStrength, tile, 1 - WhiteBalanceStrength, 0, corrected);
                Cv2.ImEncode(".png", corrected, out var png);
                return png;
            }
        }

        private static bool HasInk(Mat tile, int minimumArea)
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
                    if (stats.At<int>(index, (int)ConnectedComponentsTypes.Area) >= minimumArea)
                        return true;
                }

                return false;
            }
        }
    }
}
