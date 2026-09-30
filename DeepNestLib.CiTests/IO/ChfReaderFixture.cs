namespace DeepNestLib.CiTests.IO
{
  using System;
  using System.Collections.Generic;
  using System.IO;
  using System.Linq;
  using System.Text;
  using DeepNestLib.IO;
  using FluentAssertions;
  using IxMilia.Dxf.Entities;
  using Xunit;

  /// <summary>
  /// CHF is the laser CAM's own format (SC2000). The sample is a two-part duct side sheet the operator saved
  /// from the CAM: its holes and outlines on the CAM's cut layer, its bend lines on the next one.
  /// </summary>
  public class ChfReaderFixture
  {
    private static string Sample([System.Runtime.CompilerServices.CallerFilePath] string here = "")
      => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here), "..", "..", "test-data", "chf", "sides.chf"));

    [Fact]
    public void TheSampleReadsIntoItsCircles_LinesAndOutlines()
    {
      File.Exists(Sample()).Should().BeTrue();
      var file = ChfReader.Read(File.ReadAllBytes(Sample()));

      file.Entities.OfType<DxfCircle>().Should().HaveCount(74).And.OnlyContain(c => c.Layer == DxfOutputLayers.CutName);
      // Two outlines of 28 pieces each: 20 lines and 8 corner arcs written as cubic splines.
      file.Entities.OfType<DxfSpline>().Should().HaveCount(16).And.OnlyContain(s => s.DegreeOfCurve == 3 && s.ControlPoints.Count == 4);
      file.Entities.OfType<DxfLine>().Where(l => l.Layer == DxfOutputLayers.CutName).Should().HaveCount(40);
      file.Entities.OfType<DxfLine>().Where(l => l.Layer == DxfOutputLayers.PaintName).Should().HaveCount(16, "the bend lines are on the CAM's second layer");
    }

    [Fact]
    public void ACutPartOfTheSampleNestsAsOneOutlineWithItsHoles()
    {
      var parts = DxfPartSplitter.WriteParts(Sample());
      parts.Should().HaveCount(2, "the sheet holds two sides");

      foreach (var part in parts)
      {
        var raw = new NestExecutionHelper().LoadRawDetail(new FileInfo(part.Path));
        raw.TryConvertToNfp(0, out INfp nfp).Should().BeTrue();
        (nfp.MaxX - nfp.MinX).Should().BeApproximately(503.0, 0.1);
        (nfp.MaxY - nfp.MinY).Should().BeApproximately(958.0, 0.1);
        nfp.Children.Should().HaveCount(37, "each side carries 37 holes");
      }
    }

    [Fact]
    public void ADrawingIsTakenForChfByItsContentNotItsName()
    {
      ChfReader.IsChf(Encoding.ASCII.GetBytes("scFlie\n5\n")).Should().BeTrue();
      ChfReader.IsChf(Encoding.ASCII.GetBytes("  0\nSECTION\n")).Should().BeFalse();
      DxfFileExtensions.IsDxf(@"C:\jobs\side.CHF").Should().BeTrue();
    }

    [Fact]
    public void ArcsPolylinesGroupsAndTextsReadAsTheCamDrawsThem()
    {
      // A nested-sheet shape: a group (a part) whose outline is a polyline with one bulged corner, a hole
      // drawn as two arcs (one clockwise), a spline whose leading numbers are junk, and a text.
      string crafts = "<Crafts>\n-1\n0.0\n<End Crafts>";
      string chf = string.Join(
        "\n",
        "scFlie", "5", "<Begin Graphs>", "2",
        "####graph NO:1", "9", "300", "0,0", "100,100", "0,0", "0,0", "3",
        "####Group elem NO:1", "0.100000", "<Glyphs>", "1", "0,0", "1,1", "0,0", "0,0", "1",
        "####Gly: 1", "1", "6", "1", "4", "0,0", "0.0", "100,0", "0.0", "100,100", "1.0", "0,100", "0.0",
        "<End Glyphs>", "0", "1", crafts,
        "####Group elem NO:2", "0.100000", "<Glyphs>", "1", "0,0", "1,1", "0,0", "0,0", "2",
        "####Gly: 1", "1", "3", "30,30", "5.000000", "0.0", "3.141593",
        "####Gly: 2", "-1", "3", "30,30", "5.000000", "0.0", "-3.141593",
        "<End Glyphs>", "0", "2", crafts,
        "####Group elem NO:3", "0.100000", "<Glyphs>", "1", "0,0", "1,1", "0,0", "0,0", "1",
        "####Gly: 1", "1", "7", "56099488", "-1073741375", "4", "10,10", "20,20", "30,10", "40,20", "8",
        "0.0", "0.0", "0.0", "0.0", "2.0", "2.0", "2.0", "2.0",
        "<End Glyphs>", "1", "3", crafts,
        "0", "1",
        "####graph NO:2", "10", "5,5", "0.0", "1.000000", "1.000000", "A1", "<Text para>", "??", "<End Text para>",
        "1", "0,0", "1,1", "0,0", "0,0", "1",
        "####Group elem NO:1", "0.010000", "<Glyphs>", "1", "0,0", "1,1", "0,0", "0,0", "1",
        "####Gly: 1", "1", "4", "70,70", "2.0",
        "<End Glyphs>", "8", "1", crafts,
        "8", "1",
        "<End Graphs>", "0");

      var file = ChfReader.Read(Encoding.ASCII.GetBytes(chf));

      var outline = file.Entities.OfType<DxfLwPolyline>().Should().ContainSingle().Subject;
      outline.IsClosed.Should().BeTrue();
      outline.Vertices.Select(v => v.Bulge).Should().Equal(0, 0, 1, 0);
      var arcs = file.Entities.OfType<DxfArc>().ToList();
      arcs.Should().HaveCount(2);
      arcs[0].StartAngle.Should().BeApproximately(0, 1e-3, "anticlockwise from 0 to pi is the upper half");
      arcs[0].EndAngle.Should().BeApproximately(180, 1e-3);
      arcs[1].StartAngle.Should().BeApproximately(180, 1e-3, "clockwise from 0 to -pi is the lower half, from its other end");
      (arcs[1].EndAngle % 360).Should().BeApproximately(0, 1e-3);
      var spline = file.Entities.OfType<DxfSpline>().Should().ContainSingle().Subject;
      spline.DegreeOfCurve.Should().Be(3, "8 knots over 4 control points");
      spline.Layer.Should().Be(DxfOutputLayers.PaintName);
      file.Entities.Where(e => e.EntityType == DxfEntityType.Circle).Should().ContainSingle().Which.Layer.Should().Be(DxfOutputLayers.PaintName, "the text's letters");
    }

    [Theory]
    [InlineData(0, DxfOutputLayers.CutName)]
    [InlineData(1, DxfOutputLayers.PaintName)]
    [InlineData(2, DxfOutputLayers.CutName)]
    [InlineData(6, DxfOutputLayers.CutName)]
    [InlineData(8, DxfOutputLayers.PaintName)]
    [InlineData(9, DxfOutputLayers.CutName)]
    [InlineData(10, null)]
    public void TheCamsEngravingLayersAreMarkedAndItsOthersCut(int camLayer, string expected)
    {
      // 0 green cut, 1 blue bends, 8 red texts, 10 the background (left out); the rest are cut with
      // other settings (other metals), so a part kept on one of them must still come out as a cut.
      ChfReader.LayerFor(camLayer).Should().Be(expected);
    }

    [Fact]
    public void AnEmptyFileSaysItIsEmpty()
    {
      Action read = () => DrawingFile.Load(Array.Empty<byte>());
      read.Should().Throw<InvalidDataException>().WithMessage("*empty*");
    }

    /// <summary>
    /// Every CHF in a folder (SHEETNEST_CHF_DIR) through the whole import: read, chained into contours,
    /// split into parts. Opt-in, since the operator's files are not part of the repository.
    /// </summary>
    [Fact]
    public void EveryChfInAFolderImports()
    {
      string dir = Environment.GetEnvironmentVariable("SHEETNEST_CHF_DIR");
      if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
      {
        return;
      }

      var report = new StringBuilder();
      int ok = 0;
      var failures = new List<string>();
      foreach (string path in Directory.GetFiles(dir, "*.chf"))
      {
        string name = Path.GetFileName(path);
        try
        {
          var file = DrawingFile.Load(path);
          var raw = new NestExecutionHelper().LoadRawDetail(new FileInfo(path));
          bool nfp = raw.TryConvertToNfp(0, out INfp outline) && outline.Points.Length >= 3;
          int parts = DxfPartSplitter.Split(path, file.Entities).Count;
          report.AppendLine(FormattableString.Invariant(
            $"{name}\tentities={file.Entities.Count}\tparts={parts}\tnests={nfp}\tsize={(nfp ? $"{outline.MaxX - outline.MinX:0.#}x{outline.MaxY - outline.MinY:0.#}" : "-")}"));
          ok++;
        }
        catch (Exception ex)
        {
          failures.Add($"{name}\t{ex.GetType().Name}: {ex.GetBaseException().Message}");
        }
      }

      string outPath = Environment.GetEnvironmentVariable("SHEETNEST_CHF_REPORT");
      if (!string.IsNullOrEmpty(outPath))
      {
        File.WriteAllText(outPath, report + "\nFAILED\n" + string.Join("\n", failures));
      }

      failures.Where(f => !f.Contains("empty")).Should().BeEmpty($"{ok} files imported");
    }

    [Fact]
    public void APieceOfAnUnknownTypeIsRefusedByName()
    {
      string chf = string.Join("\n", "scFlie", "5", "<Begin Graphs>", "1", "####graph NO:1", "8", "0.100000", "<Glyphs>",
        "1.0", "0,0", "1,1", "0,0", "1,1", "1", "####Gly: 1", "1", "9", "0,0", "<End Glyphs>", "0", "1", "<End Graphs>");

      Action read = () => ChfReader.Read(Encoding.ASCII.GetBytes(chf));
      read.Should().Throw<InvalidDataException>().WithMessage("*type 9*");
    }
  }
}
