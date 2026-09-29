namespace DeepNestLib.CiTests.IO
{
  using System;
  using System.Collections.Generic;
  using System.IO;
  using System.Linq;
  using System.Text.Json;
  using DeepNestLib.IO;
  using DeepNestLib.NestProject;
  using FluentAssertions;
  using IxMilia.Dxf;
  using IxMilia.Dxf.Entities;
  using Xunit;

  /// <summary>
  /// Flipping a part writes a mirrored copy of its drawing. Every kind of curve has to come out as the
  /// mirror image of what went in (an arc or an ellipse that swaps the wrong ends cuts the other half of
  /// its circle), and flipping back has to give the operator's own drawing again.
  /// </summary>
  public class PartMirrorFixture
  {
    private static string TestData(string name, [System.Runtime.CompilerServices.CallerFilePath] string here = "")
      => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here), "..", "..", "test-data", name));

    public static IEnumerable<object[]> Drawings() => new[]
    {
      "dxf/Flat-Pattern - top^Tr_300x300.DXF",
      "dxf/Flat-Pattern - main^R2p_300x200_250x200.DXF",
      "dxf/Hodgson.dxf",
      "dxf/inhole.dxf",
      "dxf/test2.dxf",
      "chf/sides.chf",
    }.SelectMany(d => new[] { PartFlip.LeftRight, PartFlip.UpDown, PartFlip.LeftRight | PartFlip.UpDown }.Select(f => new object[] { d, f }));

    [Theory]
    [MemberData(nameof(Drawings))]
    public void AFlippedDrawingIsTheMirrorImageOfTheOriginal(string drawing, PartFlip flip)
    {
      string source = TestData(drawing);
      string flipped = PartMirror.Write(source, flip);

      Path.GetFileNameWithoutExtension(flipped).Should().Be(Path.GetFileNameWithoutExtension(source), "the file name is the part's name");
      ShouldBeMirrorImages(Contours(source), Contours(flipped), flip);
    }

    [Theory]
    [InlineData(PartFlip.LeftRight)]
    [InlineData(PartFlip.UpDown)]
    [InlineData(PartFlip.LeftRight | PartFlip.UpDown)]
    public void ArcsEllipsesAndSplinesKeepTheirHalfWhenFlipped(PartFlip flip)
    {
      // A lopsided part: a quarter arc, half an ellipse and a spline, none of them symmetric about an axis,
      // so an arc or an ellipse that swapped the wrong ends would show as the other half of its curve.
      var file = new DxfFile();
      file.Header.Version = DxfAcadVersion.R2000;
      file.Header.DefaultDrawingUnits = DxfUnits.Millimeters;
      file.Entities.Add(new DxfLine(new DxfPoint(0, 0, 0), new DxfPoint(100, 0, 0)));
      file.Entities.Add(new DxfArc(new DxfPoint(100, 30, 0), 30, 270, 360));
      file.Entities.Add(new DxfLine(new DxfPoint(130, 30, 0), new DxfPoint(130, 80, 0)));
      file.Entities.Add(new DxfEllipse(new DxfPoint(110, 80, 0), new DxfVector(20, 0, 0), 0.5) { StartParameter = 0, EndParameter = Math.PI });
      file.Entities.Add(new DxfLine(new DxfPoint(90, 80, 0), new DxfPoint(40, 80, 0)));
      var spline = new DxfSpline { DegreeOfCurve = 3 };
      foreach (var (x, y) in new[] { (40.0, 80.0), (30.0, 95.0), (10.0, 60.0), (0.0, 0.0) })
      {
        spline.ControlPoints.Add(new DxfControlPoint(new DxfPoint(x, y, 0)));
      }

      foreach (double k in new[] { 0.0, 0, 0, 0, 1, 1, 1, 1 })
      {
        spline.KnotValues.Add(k);
      }

      file.Entities.Add(spline);
      file.Entities.Add(new DxfCircle(new DxfPoint(60, 40, 0), 8));
      file.Entities.Add(new DxfText(new DxfPoint(20, 20, 0), 5, "note"));
      file.Entities.Add(new DxfLeader(new[] { new DxfPoint(0, 0, 0), new DxfPoint(5, 5, 0) }));

      string dir = Directory.CreateTempSubdirectory("sheetnest_mirror_test").FullName;
      string source = Path.Combine(dir, "lopsided.dxf");
      file.Save(source);

      string flipped = PartMirror.Write(source, flip);
      ShouldBeMirrorImages(Contours(source), Contours(flipped), flip);
      DxfFile written = DxfFile.Load(flipped);
      written.Entities.OfType<DxfText>().Should().ContainSingle().Which.Value.Should().Be("note", "a note moves with the part");
      written.Entities.OfType<DxfLeader>().Should().BeEmpty("an annotation nothing reads is dropped, not left unflipped");
      written.Header.DefaultDrawingUnits.Should().Be(DxfUnits.Millimeters, "the drawing keeps its units");
    }

    [Fact]
    public void TheSameFlipTwiceGivesTheOriginalDrawingBack()
    {
      string source = TestData("dxf/inhole.dxf");
      var part = new DetailLoadInfo { Path = source };

      PartMirror.Toggle(part, PartFlip.LeftRight);
      part.Flip.Should().Be(PartFlip.LeftRight);
      part.UnflippedPath.Should().Be(source);
      part.Path.Should().NotBe(source);

      PartMirror.Toggle(part, PartFlip.UpDown);
      part.Flip.Should().Be(PartFlip.LeftRight | PartFlip.UpDown);
      part.UnflippedPath.Should().Be(source, "the second flip is taken from the original, not from a copy");

      PartMirror.Toggle(part, PartFlip.UpDown);
      PartMirror.Toggle(part, PartFlip.LeftRight);
      part.Flip.Should().Be(PartFlip.None);
      part.Path.Should().Be(source);
      part.UnflippedPath.Should().BeNull();
    }

    [Fact]
    public void AFlippedPartWhoseCopyWentWithTempIsWrittenAgain()
    {
      string source = TestData("dxf/inhole.dxf");
      var part = new DetailLoadInfo { Path = source };
      PartMirror.Toggle(part, PartFlip.LeftRight);

      var reopened = JsonSerializer.Deserialize<DetailLoadInfo>(part.ToJson());
      reopened.Flip.Should().Be(PartFlip.LeftRight);
      reopened.UnflippedPath.Should().Be(source);

      File.Delete(reopened.Path);
      PartMirror.Restore(reopened).Should().BeTrue();
      reopened.IsExists.Should().BeTrue();
      ShouldBeMirrorImages(Contours(source), Contours(reopened.Path), PartFlip.LeftRight);
    }

    [Fact]
    public void AnUnflippedPartSavesNothingAboutFlipping()
    {
      string json = new DetailLoadInfo { Path = "x.dxf" }.ToJson();
      json.Should().NotContain("Flip");
    }

    [Theory]
    [InlineData(PartShape.Rectangle, 100, 50, 100, 50, 5000)]
    [InlineData(PartShape.Rectangle, 100, 100, 100, 100, 10000)]
    [InlineData(PartShape.Circle, 100, 0, 100, 100, 7853.98)]
    public void AShapeIsAPartOfItsSize(PartShape shape, double width, double height, double w, double h, double area)
    {
      string dir = Directory.CreateTempSubdirectory("sheetnest_shape_test").FullName;
      string path = PartShapes.Write(dir, "Квадрат " + PartShapes.SizeText(shape, width, height), shape, width, height, millimetres: true);

      var raw = new NestExecutionHelper().LoadRawDetail(new FileInfo(path));
      raw.TryConvertToNfp(0, out INfp nfp).Should().BeTrue();
      (nfp.MaxX - nfp.MinX).Should().BeApproximately(w, 0.1, "a circle is read as a polygon inside it");
      (nfp.MaxY - nfp.MinY).Should().BeApproximately(h, 0.1);
      Math.Abs(nfp.Area).Should().BeApproximately(area, area * 0.005);
      DxfFile.Load(path).Entities.Should().OnlyContain(e => e.Layer == DxfOutputLayers.CutName);
    }

    [Fact]
    public void AShapeIsNamedBySize()
    {
      PartShapes.SizeText(PartShape.Rectangle, 100, 50.5).Should().Be("100x50.5");
      PartShapes.SizeText(PartShape.Circle, 80, 80).Should().Be("D80");
    }

    private static List<(double X, double Y)[]> Contours(string path)
    {
      var raw = new NestExecutionHelper().LoadRawDetail(new FileInfo(path));
      raw.TryConvertToNfp(0, out INfp nfp).Should().BeTrue();
      var contours = new List<(double X, double Y)[]> { nfp.Points.Select(p => (p.X, p.Y)).ToArray() };
      contours.AddRange((nfp.Children ?? new List<INfp>()).Select(c => c.Points.Select(p => (p.X, p.Y)).ToArray()));
      return contours;
    }

    /// <summary>Reflects the original, sets both on their lower left corners and asks that every vertex of
    /// each lies on the other: the same outline and holes, wherever the reader starts each contour.</summary>
    private static void ShouldBeMirrorImages(List<(double X, double Y)[]> original, List<(double X, double Y)[]> flipped, PartFlip flip)
    {
      double sx = flip.HasFlag(PartFlip.LeftRight) ? -1 : 1;
      double sy = flip.HasFlag(PartFlip.UpDown) ? -1 : 1;
      var expected = Normalise(original.Select(c => c.Select(p => (sx * p.X, sy * p.Y)).ToArray()).ToList());
      var actual = Normalise(flipped);

      actual.Count.Should().Be(expected.Count, "the same number of contours");
      FarthestVertex(expected, actual).Should().BeLessThan(0.1, "within the chord tolerance curves are drawn with");
      FarthestVertex(actual, expected).Should().BeLessThan(0.1, "within the chord tolerance curves are drawn with");
    }

    private static List<(double X, double Y)[]> Normalise(List<(double X, double Y)[]> contours)
    {
      double minX = contours.SelectMany(c => c).Min(p => p.X);
      double minY = contours.SelectMany(c => c).Min(p => p.Y);
      return contours.Select(c => c.Select(p => (p.X - minX, p.Y - minY)).ToArray()).ToList();
    }

    private static double FarthestVertex(List<(double X, double Y)[]> from, List<(double X, double Y)[]> onto)
    {
      var segments = onto.SelectMany(c => c.Select((p, i) => (A: p, B: c[(i + 1) % c.Length]))).ToArray();
      double worst = 0;
      foreach (var p in from.SelectMany(c => c))
      {
        double best = double.MaxValue;
        foreach (var (a, b) in segments)
        {
          best = Math.Min(best, DistanceToSegment(p, a, b));
        }

        worst = Math.Max(worst, best);
      }

      return worst;
    }

    private static double DistanceToSegment((double X, double Y) p, (double X, double Y) a, (double X, double Y) b)
    {
      double dx = b.X - a.X, dy = b.Y - a.Y;
      double len = (dx * dx) + (dy * dy);
      double t = len == 0 ? 0 : Math.Clamp((((p.X - a.X) * dx) + ((p.Y - a.Y) * dy)) / len, 0, 1);
      double ex = a.X + (t * dx) - p.X, ey = a.Y + (t * dy) - p.Y;
      return Math.Sqrt((ex * ex) + (ey * ey));
    }
  }
}
