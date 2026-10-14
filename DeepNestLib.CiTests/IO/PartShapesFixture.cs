namespace DeepNestLib.CiTests.IO
{
  using System;
  using System.Collections.Generic;
  using System.IO;
  using System.Linq;
  using System.Threading.Tasks;
  using DeepNestLib.IO;
  using DeepNestLib.Placement;
  using FluentAssertions;
  using IxMilia.Dxf;
  using IxMilia.Dxf.Entities;
  using Xunit;

  /// <summary>
  /// Parts the program draws itself (+ Shape...): each one has to load as the part it says it is, and the
  /// perforated tape blank has to reach the sheet with its shared cuts on the cut layer.
  /// </summary>
  public class PartShapesFixture
  {
    private static string Text(string key) => key;

    private static INfp Load(string path)
    {
      var raw = new NestExecutionHelper().LoadRawDetail(new FileInfo(path));
      raw.TryConvertToNfp(0, out INfp nfp).Should().BeTrue();
      return nfp;
    }

    private static string Write(ShapeKind kind, params double[] values)
    {
      string dir = Directory.CreateTempSubdirectory("sheetnest_shape_test").FullName;
      return PartShapes.Write(dir, kind.PartName(values, null, Text), kind, values, millimetres: true);
    }

    private static double[] Tape(double length = 1000, double width = 250)
    {
      var kind = PartShapes.PerforatedTape;
      var values = kind.Parameters.Select(p => p.DefaultMm).ToArray();
      foreach (var kv in kind.Presets[0].ValuesMm)
      {
        values[kv.Key] = kv.Value;
      }

      values[PerforatedTapeShape.Length] = length;
      values[PerforatedTapeShape.Width] = width;
      return values;
    }

    [Theory]
    [InlineData(100, 50, 5000)]
    [InlineData(100, 100, 10000)]
    public void ARectangleIsAPartOfItsSize(double width, double height, double area)
    {
      var nfp = Load(Write(PartShapes.Rectangle, width, height));
      (nfp.MaxX - nfp.MinX).Should().BeApproximately(width, 0.01);
      (nfp.MaxY - nfp.MinY).Should().BeApproximately(height, 0.01);
      Math.Abs(nfp.Area).Should().BeApproximately(area, 0.5);
    }

    [Fact]
    public void ACircleIsADiscOfItsDiameter()
    {
      string path = Write(PartShapes.Circle, 100);
      var nfp = Load(path);
      (nfp.MaxX - nfp.MinX).Should().BeApproximately(100, 0.1, "a circle is read as a polygon inside it");
      Math.Abs(nfp.Area).Should().BeApproximately(7853.98, 40);
      DxfFile.Load(path).Entities.Should().OnlyContain(e => e.Layer == DxfOutputLayers.CutName);
    }

    [Fact]
    public void AShapeIsNamedBySize()
    {
      PartShapes.Rectangle.PartName(new[] { 100, 50.5 }, null, Text).Should().Be("Shape_Rectangle 100x50.5");
      PartShapes.Rectangle.PartName(new double[] { 100, 100 }, null, Text).Should().Be("Shape_Square 100x100");
      PartShapes.Circle.PartName(new double[] { 80 }, null, Text).Should().Be("Shape_Circle D80");
      PartShapes.Washer.PartName(new[] { 16, 8.4 }, PartShapes.Washer.Presets[0], Text).Should().Be("Shape_Washer M8 D16-8.4");
      PartShapes.Washer.PartName(new double[] { 37, 13 }, PartShapes.Washer.Presets[5], Text).Should().Be("Shape_WasherWideName M12 D37-13");
      PartShapes.Washer.PartName(new double[] { 30, 10 }, null, Text).Should().Be("Shape_Washer D30-10");
      PartShapes.PerforatedTape.PartName(Tape(), null, Text).Should().Be("Shape_Tape B25 1000x250");
    }

    [Theory]
    [InlineData(16, 8.4)]
    [InlineData(37, 13)]
    public void AWasherIsADiscWithOneHole(double outer, double hole)
    {
      var nfp = Load(Write(PartShapes.Washer, outer, hole));
      (nfp.MaxX - nfp.MinX).Should().BeApproximately(outer, 0.1);
      nfp.Children.Should().ContainSingle();
      var child = nfp.Children[0];
      (child.MaxX - child.MinX).Should().BeApproximately(hole, 0.1);
      (child.MinX + child.MaxX).Should().BeApproximately(nfp.MinX + nfp.MaxX, 0.05, "the hole is in the middle");
    }

    [Fact]
    public void AWasherNeedsItsHoleInsideIt()
    {
      PartShapes.Washer.Problem(new double[] { 10, 12 }).Should().Be("Shape_WasherHoleTooBig");
      Action act = () => Write(PartShapes.Washer, 10, 12);
      act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void StandardWashersAreDin125AndDin9021()
    {
      var sizes = PartShapes.Washer.Presets.Select(p => (p.LabelKey, p.Detail, Outer: p.ValuesMm[0], Hole: p.ValuesMm[1])).ToList();
      sizes.Should().Equal(
        ("Shape_WasherStandard", "M8", 16, 8.4),
        ("Shape_WasherStandard", "M10", 20, 10.5),
        ("Shape_WasherStandard", "M12", 24, 13),
        ("Shape_WasherWide", "M8", 24, 8.4),
        ("Shape_WasherWide", "M10", 30, 10.5),
        ("Shape_WasherWide", "M12", 37, 13));
    }

    [Fact]
    public void ATapeBlankIsOnePartWithItsHolesAndStripsCutBetween()
    {
      var v = Tape();
      PerforatedTapeShape.Strips(v).Should().Be(10);
      int groups = PerforatedTapeShape.Groups(v);
      groups.Should().Be(66, "15 mm apart along 1000 mm, the end groups clear of the ends");

      string path = Write(PartShapes.PerforatedTape, v);
      var nfp = Load(path);
      (nfp.MaxX - nfp.MinX).Should().BeApproximately(1000, 0.01);
      (nfp.MaxY - nfp.MinY).Should().BeApproximately(250, 0.01);
      nfp.Children.Should().HaveCount(10 * groups * 5, "a big hole and four small ones per group; the shared cuts are not holes");
      foreach (var hole in nfp.Children)
      {
        double y = (hole.MinY + hole.MaxY) / 2;
        double inStrip = y - (Math.Floor(y / 25) * 25);
        inStrip.Should().BeInRange(25 / 2.0 - 4 - 0.01, 25 / 2.0 + 4 + 0.01, "every hole sits in the middle band of its strip");
      }

      // Nine shared cuts, each broken by two 0.5 mm bridges (about every 400 mm of 1000).
      var cuts = DxfFile.Load(path).Entities.OfType<DxfLine>().Where(l => l.Layer == DxfOutputLayers.InnerCutSourceName).ToList();
      cuts.Should().HaveCount(9 * 3);
      cuts.GroupBy(c => Math.Round(c.P1.Y, 3)).Select(g => g.Key).Should().BeEquivalentTo(Enumerable.Range(1, 9).Select(k => k * 25.0));
      var (length, pieces) = DxfInnerCuts.Measure(path);
      pieces.Should().Be(27);
      length.Should().BeApproximately(9 * (1000 - (2 * 0.5)), 1e-6);
    }

    [Theory]
    [InlineData(1000, 20, "Shape_TapeBlankTooNarrow")]
    [InlineData(10, 250, "Shape_TapeTooShort")]
    public void ATapeThatCannotBeMadeSaysWhy(double length, double width, string problem)
    {
      PartShapes.PerforatedTape.Problem(Tape(length, width)).Should().Be(problem);
    }

    [Fact]
    public void TapeHolesThatWouldTouchAreRefused()
    {
      var v = Tape();
      v[PerforatedTapeShape.BigHole] = 12;
      PartShapes.PerforatedTape.Problem(v).Should().Be("Shape_TapeHolesTouch");
      v = Tape();
      v[PerforatedTapeShape.Strip] = 10;
      v[PerforatedTapeShape.Width] = 100;
      PartShapes.PerforatedTape.Problem(v).Should().Be("Shape_TapeHolesOutside");
    }

    [Fact]
    public void ABlankThatIsNotAWholeNumberOfStripsKeepsTheWholeStrips()
    {
      var v = Tape(500, 260);
      PerforatedTapeShape.Strips(v).Should().Be(10);
      var nfp = Load(Write(PartShapes.PerforatedTape, v));
      (nfp.MaxY - nfp.MinY).Should().BeApproximately(250, 0.01);
    }

    /// <summary>The shared cuts reach the sheet on the cut layer, turned with the part, and the layout draws them
    /// in the same place.</summary>
    [Fact]
    public async Task TheSharedCutsAreCutOnTheSheetWhereTheLayoutShowsThem()
    {
      string path = Write(PartShapes.PerforatedTape, Tape(800, 100));
      var turned = Load(path).Rotate(90);
      var placement = new PartPlacement(turned) { Id = 1, Source = 0, X = 300, Y = 50 };
      var sheet = new SheetPlacement(
        PlacementTypeEnum.BoundingBox, Sheet.NewSheet(1, 1250, 2500), new List<IPartPlacement> { placement }, 0, SvgNest.Config.ClipperScale);

      string outPath = Path.Combine(Path.GetDirectoryName(path), "out.dxf");
      await new DxfExporter().Export(outPath, sheet, false);
      var outDxf = DxfFile.Load(outPath);

      outDxf.Entities.Should().NotContain(e => e.Layer == DxfOutputLayers.InnerCutSourceName);
      outDxf.Entities.Should().NotContain(e => e.Layer == DxfOutputLayers.PaintName, "a shared cut is cut, not marked like a bend");
      var cuts = outDxf.Entities.OfType<DxfLine>().Where(l => l.Layer == DxfOutputLayers.CutName).ToList();
      cuts.Should().HaveCount(3 * 2, "three shared cuts, each broken by one bridge along 800 mm");
      cuts.Should().OnlyContain(l => Math.Abs(l.P1.X - l.P2.X) < 1e-3, "the part was turned a quarter, so the strips run up the sheet");

      var placed = placement.PlacedPart;
      cuts.Should().OnlyContain(l => l.P1.X > placed.MinX && l.P1.X < placed.MaxX);

      // The rotation the export applies is the part's own (PolygonsForExport), so the layout uses the same.
      var shown = DxfInnerCuts.Placed(path, placement.X, placement.Y, placement.Part.Rotation, placement.IsMirrored);
      shown.Select(s => (Math.Round(s.A.X, 3), Math.Round(s.A.Y, 3), Math.Round(s.B.X, 3), Math.Round(s.B.Y, 3))).Should().BeEquivalentTo(
        cuts.Select(c => (Math.Round(c.P1.X, 3), Math.Round(c.P1.Y, 3), Math.Round(c.P2.X, 3), Math.Round(c.P2.Y, 3))));
    }
  }
}
