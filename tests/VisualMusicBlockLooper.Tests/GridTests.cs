using System;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenCvSharp;
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
                var pageCorners = new[] { new Point2f(0, 0), new Point2f(page.Width, 0),
                    new Point2f(page.Width, page.Height), new Point2f(0, page.Height) };
                var photoCorners = new[] { new Point2f(92, 74), new Point2f(1190, 19),
                    new Point2f(1226, 825), new Point2f(38, 782) };
                if (rotated)
                    photoCorners = photoCorners.Skip(2).Concat(photoCorners.Take(2)).ToArray();

                using (var transform = Cv2.GetPerspectiveTransform(pageCorners, photoCorners))
                using (var photo = new Mat())
                {
                    Cv2.WarpPerspective(page, photo, transform, new Size(1280, 860),
                        borderValue: new Scalar(200));
                    var cells = GridReader.Read(photo);
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

        private static Mat RasterizeSheet()
        {
            var document = XDocument.Parse(GridLayout.CreateSvg());
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
                Cv2.Line(image, new Point(px("x1"), px("y1")), new Point(px("x2"), px("y2")), Scalar.Black, 2);
            }

            return image;
        }
    }
}
