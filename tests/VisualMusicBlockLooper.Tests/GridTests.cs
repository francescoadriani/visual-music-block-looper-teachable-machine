using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenCvSharp;
using OpenCvSharp.Aruco;
using VisualMusicBlockLooper.Vision;

namespace VisualMusicBlockLooper.Tests
{
    [TestClass]
    public class GridTests
    {
        [TestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public void ReadsThePrintedGridInRowOrderEvenWhenRotated(bool rotated)
        {
            using (var page = RasterizeSheet())
            {
                Cv2.Line(page, new Point(54 * 4, 61 * 4), new Point(58 * 4, 69 * 4), new Scalar(183), 3);
                Cv2.Circle(page, new Point(242 * 4, 147 * 4), 18, Scalar.Black, 4);
                using (var transform = CameraPerspective(page, rotated))
                using (var photo = new Mat())
                {
                    Cv2.WarpPerspective(page, photo, transform, new Size(1280, 860),
                        borderValue: new Scalar(200));
                    var capture = GridReader.Read(photo);
                    Assert.AreSame(GridLayout.Original, capture.Layout);
                    var cells = capture.Cells;
                    Assert.AreEqual(32, cells.Length);
                    Assert.AreEqual(2, cells.Count(cell => cell.Png != null));
                    Assert.IsNotNull(cells[0].Png);
                    Assert.IsNotNull(cells[31].Png);

                    var expected = Cv2.PerspectiveTransform(new[] { new Point2f(40 * 4, 51 * 4) }, transform)[0];
                    Assert.AreEqual(expected.X, cells[0].Outline[0].X, 6);
                    Assert.AreEqual(expected.Y, cells[0].Outline[0].Y, 6);
                }
            }
        }

        [TestMethod]
        public void MissingMarkerRequiresAnotherCapture()
        {
            using (var page = RasterizeSheet())
            {
                Cv2.Rectangle(page, new Rect(12 * 4, 24 * 4, 23 * 4, 23 * 4), Scalar.White, -1);
                Assert.ThrowsException<InvalidOperationException>(() => GridReader.Read(page));
            }
        }

        [TestMethod]
        public void RecoversMarkersNearTheEdgeOfASmallImage()
        {
            using (var page = RasterizeSheet())
            using (var small = new Mat())
            using (var dictionary = CvAruco.GetPredefinedDictionary(GridLayout.Dictionary))
            {
                Cv2.Line(page, new Point(52 * 4, 61 * 4), new Point(58 * 4, 69 * 4), Scalar.Black, 3);
                Cv2.Resize(page, small, new Size(640, 453), 0, 0, InterpolationFlags.Area);
                using (var photo = new Mat(small, new Rect(29, 0, 611, small.Height)))
                {
                    CvAruco.DetectMarkers(photo, dictionary, out _, out var detected,
                        new DetectorParameters { CornerRefinementMethod = CornerRefineMethod.Subpix }, out _);
                    Assert.IsNull(GridLayout.FromMarkers(detected));

                    var capture = GridReader.Read(photo);
                    Assert.AreSame(GridLayout.Original, capture.Layout);
                    CollectionAssert.AreEqual(new[] { 0 }, MarkedCells(capture));
                }
            }
        }

        [TestMethod]
        public void KeepsTheEmptyCellsSilentOnASmallRealCameraImage()
        {
            using (var photo = ReadFixture("foglio-2.png", 640))
            {
                var capture = GridReader.Read(photo);
                Assert.AreSame(GridLayout.Detailed, capture.Layout);
                CollectionAssert.AreEqual(new[] { 0, 7, 9, 10, 20, 28, 34, 38, 46, 48,
                    64, 71, 73, 74, 84, 92, 98, 102, 110 }, MarkedCells(capture));
            }
        }

        [TestMethod]
        public void CombinesComplementaryMarkerDetections()
        {
            using (var photo = ReadFixture("foglio-2.png", 357))
            {
                var capture = GridReader.Read(photo);
                Assert.AreSame(GridLayout.Detailed, capture.Layout);
                Assert.AreEqual(128, capture.Cells.Length);
            }
        }

        [TestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public void UsesThePaperMarginsToCorrectColorUnderUnevenLighting(bool detailed)
        {
            var layout = detailed ? GridLayout.Detailed : GridLayout.Original;
            using (var page = RasterizeSheet(layout))
            {
                var target = layout.Cell(0, 0);
                Cv2.Circle(page, new Point(target.X * 4 + target.Width * 2, target.Y * 4 + target.Height * 2),
                    target.Width, new Scalar(40), 3);

                using (var tinted = new Mat())
                {
                    Cv2.CvtColor(page, tinted, ColorConversionCodes.GRAY2BGR);
                    for (int x = 0; x < tinted.Width; x++)
                    {
                        double light = 0.60 + 0.25 * x / tinted.Width;
                        using (var column = tinted.Col(x))
                            Cv2.Multiply(column, new Scalar(light * 0.8, light, light * 1.12), column);
                    }

                    var capture = GridReader.Read(tinted);
                    CollectionAssert.AreEqual(new[] { 0 }, MarkedCells(capture));
                    using (var tile = Cv2.ImDecode(capture.Cells[0].Png, ImreadModes.Color))
                    {
                        var paper = tile.At<Vec3b>(tile.Rows / 2, tile.Cols / 2);
                        Assert.IsTrue(paper.Item0 >= 210 && paper.Item1 >= 210 && paper.Item2 >= 210, paper.ToString());
                        Assert.IsTrue(Math.Abs(paper.Item0 - paper.Item2) <= 16, paper.ToString());
                    }
                }
            }
        }

        internal static Mat ReadFixture(string name, int width)
        {
            using (var source = Cv2.ImRead(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "fixtures", name)))
            {
                Assert.IsFalse(source.Empty(), name);
                var resized = new Mat();
                Cv2.Resize(source, resized, new Size(width, source.Height * width / source.Width), 0, 0, InterpolationFlags.Area);
                return resized;
            }
        }

        private static int[] MarkedCells(GridCapture capture) =>
            Enumerable.Range(0, capture.Cells.Length).Where(index => capture.Cells[index].Png != null).ToArray();

        [TestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public void ReadsTheTwoBlocksAsThirtyTwoConsecutiveSteps(bool rotated)
        {
            using (var page = RasterizeSheet(GridLayout.Detailed))
            {
                Cv2.Line(page, new Point(252 * 4, 83 * 4), new Point(254 * 4, 87 * 4), new Scalar(183), 3);
                Cv2.Circle(page, new Point(43 * 4, 116 * 4), 13, Scalar.Black, 3);
                using (var transform = CameraPerspective(page, rotated))
                using (var photo = new Mat())
                {
                    Cv2.WarpPerspective(page, photo, transform, new Size(1280, 860), borderValue: new Scalar(200));
                    var capture = GridReader.Read(photo);
                    Assert.AreSame(GridLayout.Detailed, capture.Layout);
                    Assert.AreEqual(128, capture.Cells.Length);
                    CollectionAssert.AreEqual(new[] { 15, 16 },
                        capture.Cells.Where(cell => cell.Png != null).Select(cell => cell.Step).ToArray());
                    foreach (var step in Enumerable.Range(0, 32))
                        Assert.AreEqual(4, capture.Cells.Count(cell => cell.Step == step));
                }
            }
        }

        private static Mat CameraPerspective(Mat page, bool rotated)
        {
            var pageCorners = new[] { new Point2f(0, 0), new Point2f(page.Width, 0),
                new Point2f(page.Width, page.Height), new Point2f(0, page.Height) };
            var photoCorners = new[] { new Point2f(92, 74), new Point2f(1190, 19),
                new Point2f(1226, 825), new Point2f(38, 782) };
            if (rotated)
                photoCorners = photoCorners.Skip(2).Concat(photoCorners.Take(2)).ToArray();
            return Cv2.GetPerspectiveTransform(pageCorners, photoCorners);
        }

        [TestMethod]
        public void RejectsMarkersFromDifferentFormats()
        {
            using (var page = RasterizeSheet(GridLayout.Detailed))
            using (var dictionary = CvAruco.GetPredefinedDictionary(GridLayout.Dictionary))
            using (var marker = new Mat())
            using (var topLeft = new Mat(page, new Rect(14 * 4, 26 * 4, 18 * 4, 18 * 4)))
            {
                dictionary.GenerateImageMarker(0, 18 * 4, marker, 1);
                marker.CopyTo(topLeft);
                Assert.ThrowsException<InvalidOperationException>(() => GridReader.Read(page));
            }
        }

        internal static Mat RasterizeSheet(GridLayout layout = null)
        {
            var document = XDocument.Parse((layout ?? GridLayout.Original).CreateSvg());
            var root = document.Root;
            Assert.AreEqual("297mm", (string)root.Attribute("width"));
            Assert.AreEqual("210mm", (string)root.Attribute("height"));

            var image = new Mat(840, 1188, MatType.CV_8UC1, Scalar.White);
            foreach (var rect in root.Descendants().Where(e => e.Name.LocalName == "rect"))
            {
                Func<string, int> px = name =>
                    (int)Math.Round(double.Parse((string)rect.Attribute(name), CultureInfo.InvariantCulture) * 4);
                var box = new Rect(px("x"), px("y"), px("width"), px("height"));
                if ((string)rect.Attribute("fill") == "#000")
                    Cv2.Rectangle(image, box, Scalar.Black, -1);
                if ((string)rect.Attribute("stroke") == "#000")
                    Cv2.Rectangle(image, box, Scalar.Black, 2);
            }

            foreach (var line in root.Descendants().Where(e => e.Name.LocalName == "line"))
            {
                Func<string, int> px = name => (int)line.Attribute(name) * 4;
                int thickness = (int)Math.Ceiling((double)line.Attribute("stroke-width") * 4);
                Cv2.Line(image, new Point(px("x1"), px("y1")), new Point(px("x2"), px("y2")), Scalar.Black, thickness);
            }

            return image;
        }
    }
}
