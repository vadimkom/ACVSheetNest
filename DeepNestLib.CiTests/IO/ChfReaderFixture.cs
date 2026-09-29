namespace DeepNestLib.CiTests.IO
{
  using System;
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
    public void APieceOfAnUnknownTypeIsRefusedByName()
    {
      string chf = string.Join("\n", "scFlie", "5", "<Begin Graphs>", "1", "####graph NO:1", "8", "0.100000", "<Glyphs>",
        "1.0", "0,0", "1,1", "0,0", "1,1", "1", "####Gly: 1", "1", "9", "0,0", "<End Glyphs>", "0", "1", "<End Graphs>");

      Action read = () => ChfReader.Read(Encoding.ASCII.GetBytes(chf));
      read.Should().Throw<InvalidDataException>().WithMessage("*type 9*");
    }
  }
}
