namespace DeepNestLib.CiTests.IO
{
  using System;
  using System.Collections.Generic;
  using System.IO;
  using System.Linq;
  using System.Threading.Tasks;
  using DeepNestLib.Geometry;
  using DeepNestLib.IO;
  using DeepNestLib.Placement;
  using FluentAssertions;
  using IxMilia.Dxf;
  using IxMilia.Dxf.Entities;
  using Xunit;

  public class DxfPartNameExportFixture
  {
    [Theory]
    [InlineData(@"C:\Drawings\Bracket.dxf", "Bracket")]
    [InlineData(@"/var/data/flange.dxf", "flange")]
    [InlineData("simple.dxf", "simple")]
    [InlineData("part_with_no_ext", "part_with_no_ext")]
    [InlineData("complex.rev2.final.dxf", "complex.rev2.final")]
    [InlineData("", "")]
    [InlineData(null, "")]
    [InlineData(@"C:\Ducts\Flat-Pattern - top^Tr_300x300.DXF", "top^Tr_300x300")]
    [InlineData("flat pattern -main.dxf", "main")]
    [InlineData("Flat-Pattern - .dxf", "Flat-Pattern - ")]
    [InlineData("Bracket Flat-Pattern - 2.dxf", "Bracket Flat-Pattern - 2")]
    public void GetPartNameLabelReturnsCleanStemWithoutExtension(string source, string expected)
    {
      DxfExporter.GetPartNameLabel(source).Should().Be(expected);
    }

    [Fact]
    public void ComputePartCentroidReturnsCorrectCoordinatesForRectangle()
    {
      var poly = CreateRectangleNfp(0, 0, 100, 50);
      var (cx, cy) = DxfExporter.ComputePartCentroid(poly);

      cx.Should().BeApproximately(50, 1e-4);
      cy.Should().BeApproximately(25, 1e-4);
    }

    [Fact]
    public void ComputePartCentroidFallsInsideNonConvexUShape()
    {
      // Create a U-shaped polygon where geometric centroid falls in the notch
      var uShape = new NoFitPolygon(new[]
      {
        new SvgPoint(0, 0),
        new SvgPoint(100, 0),
        new SvgPoint(100, 100),
        new SvgPoint(80, 100),
        new SvgPoint(80, 30),
        new SvgPoint(20, 30),
        new SvgPoint(20, 100),
        new SvgPoint(0, 100),
      });

      var (cx, cy) = DxfExporter.ComputePartCentroid(uShape);

      GeometryUtil.PointInPolygon(new SvgPoint(cx, cy), uShape).Should().BeTrue();
    }

    [Fact]
    public void ComputeTextPropertiesChoosesOrientationAndHeightForMillimeters()
    {
      var widePoly = CreateRectangleNfp(0, 0, 120, 40);
      var (wideHeight, wideVert) = DxfExporter.ComputeTextProperties(widePoly, "Bracket");

      wideVert.Should().BeFalse("width 120 is larger than height 40");
      wideHeight.Should().BeInRange(1.5, 10.0);

      var tallPoly = CreateRectangleNfp(0, 0, 20, 100);
      var (tallHeight, tallVert) = DxfExporter.ComputeTextProperties(tallPoly, "Bracket");

      tallVert.Should().BeTrue("height 100 is > 1.4 * width 20");
      tallHeight.Should().BeInRange(1.5, 10.0);
    }

    [Fact]
    public void ComputeTextPropertiesClampsForInches()
    {
      var inchPoly = CreateRectangleNfp(0, 0, 4, 2);
      var (height, isVert) = DxfExporter.ComputeTextProperties(inchPoly, "Bracket");

      isVert.Should().BeFalse();
      height.Should().BeInRange(0.06, 0.4);
    }

    [Fact]
    public async Task ExportWithDxfPartNameExportNoneEmitsNoTextEntities()
    {
      string tempDxf = Path.Combine(Path.GetTempPath(), $"sheetnest_test_part_{Guid.NewGuid():N}.dxf");
      string tempOut = Path.Combine(Path.GetTempPath(), $"sheetnest_test_out_{Guid.NewGuid():N}.dxf");

      try
      {
        WriteSquareDxf(tempDxf, 50);

        var sheetPlacement = CreateTestSheetPlacement(tempDxf, 2);
        var exporter = new DxfExporter { PartNameExport = DxfPartNameExport.None };
        await exporter.Export(tempOut, sheetPlacement, false);

        var outDxf = DxfFile.Load(tempOut);
        outDxf.Entities.OfType<DxfText>().Should().BeEmpty();
      }
      finally
      {
        if (File.Exists(tempDxf)) File.Delete(tempDxf);
        if (File.Exists(tempOut)) File.Delete(tempOut);
      }
    }

    [Fact]
    public async Task ExportWithDxfPartNameExportBackgroundEmitsTextOnBackgroundLayer()
    {
      string tempDxf = Path.Combine(Path.GetTempPath(), $"Flange_{Guid.NewGuid():N}.dxf");
      string tempOut = Path.Combine(Path.GetTempPath(), $"sheetnest_test_out_{Guid.NewGuid():N}.dxf");

      try
      {
        WriteSquareDxf(tempDxf, 60);

        var sheetPlacement = CreateTestSheetPlacement(tempDxf, 2);
        var exporter = new DxfExporter { PartNameExport = DxfPartNameExport.Background };
        await exporter.Export(tempOut, sheetPlacement, false);

        var outDxf = DxfFile.Load(tempOut);

        // Verify layer definition
        outDxf.Layers.Should().Contain(l => l.Name == DxfOutputLayers.Background.Name);
        var bgLayer = outDxf.Layers.Single(l => l.Name == DxfOutputLayers.Background.Name);
        bgLayer.Color.Index.Should().Be(8);

        // Verify text entities
        var texts = outDxf.Entities.OfType<DxfText>().ToList();
        texts.Should().HaveCount(2);

        string expectedPartStem = Path.GetFileNameWithoutExtension(tempDxf);
        foreach (var t in texts)
        {
          t.Layer.Should().Be(DxfOutputLayers.Background.Name);
          t.Value.Should().Be(expectedPartStem);
          t.HorizontalTextJustification.Should().Be(DxfHorizontalTextJustification.Center);
          t.VerticalTextJustification.Should().Be(DxfVerticalTextJustification.Middle);
          t.TextHeight.Should().BeInRange(1.5, 10.0);
        }

        // Placements are at (10, 10) and (100, 10), square 60x60 -> centroids at (40, 40) and (130, 40)
        texts[0].Location.X.Should().BeApproximately(40, 0.1);
        texts[0].Location.Y.Should().BeApproximately(40, 0.1);
        texts[1].Location.X.Should().BeApproximately(130, 0.1);
        texts[1].Location.Y.Should().BeApproximately(40, 0.1);
      }
      finally
      {
        if (File.Exists(tempDxf)) File.Delete(tempDxf);
        if (File.Exists(tempOut)) File.Delete(tempOut);
      }
    }

    [Fact]
    public async Task ExportWithDxfPartNameExportLayer9EmitsTextOnLayer9()
    {
      string tempDxf = Path.Combine(Path.GetTempPath(), $"Bracket_{Guid.NewGuid():N}.dxf");
      string tempOut = Path.Combine(Path.GetTempPath(), $"sheetnest_test_out_{Guid.NewGuid():N}.dxf");

      try
      {
        WriteSquareDxf(tempDxf, 80);

        var sheetPlacement = CreateTestSheetPlacement(tempDxf, 1);
        var exporter = new DxfExporter { PartNameExport = DxfPartNameExport.Paint };
        await exporter.Export(tempOut, sheetPlacement, false);

        var outDxf = DxfFile.Load(tempOut);

        outDxf.Layers.Should().Contain(l => l.Name == DxfOutputLayers.PaintName);
        var layer9 = outDxf.Layers.Single(l => l.Name == DxfOutputLayers.PaintName);
        layer9.Color.Index.Should().Be((byte)DxfOutputLayers.Paint.ColorIndex);

        var texts = outDxf.Entities.OfType<DxfText>().ToList();
        texts.Should().HaveCount(1);
        texts[0].Layer.Should().Be("9");
        texts[0].Value.Should().Be(Path.GetFileNameWithoutExtension(tempDxf));
      }
      finally
      {
        if (File.Exists(tempDxf)) File.Delete(tempDxf);
        if (File.Exists(tempOut)) File.Delete(tempOut);
      }
    }

    [Fact]
    public async Task CommonLineExportEmitsPartNamesOnBackgroundLayer()
    {
      string tempDir = Path.Combine(Path.GetTempPath(), $"cl_test_{Guid.NewGuid():N}");
      Directory.CreateDirectory(tempDir);
      string dxfPath = Path.Combine(tempDir, "PartCommon.dxf");
      string outPath = Path.Combine(tempDir, "out.dxf");

      try
      {
        WriteSquareDxf(dxfPath, 10);
        IRawDetail detail = new NestExecutionHelper().LoadRawDetail(new FileInfo(dxfPath));
        detail.TryConvertToNfp(0, out INfp p1).Should().BeTrue();
        detail.TryConvertToNfp(0, out INfp p2).Should().BeTrue();

        const double gap = 0.01;
        const double tol = 1e-4;
        var parts = new List<IPartPlacement>
        {
          new PartPlacement(p1) { Id = 0, Source = 0, X = 0D, Y = 0D },
          new PartPlacement(p2) { Id = 1, Source = 0, X = 10D + gap, Y = 0D },
        };

        var matches = new List<CommonEdge>();
        for (int a = 0; a < parts[0].Part.Points.Length; a++)
        {
          for (int b = 0; b < parts[1].Part.Points.Length; b++)
          {
            if (CommonLineGeometryResolver.TryCreateCommonEdge(
              parts[0], a, parts[1], b, gap, tol, out CommonEdge edge))
            {
              matches.Add(edge);
            }
          }
        }

        matches.Should().ContainSingle();

        var sheetPlacement = new SheetPlacement(
          PlacementTypeEnum.BoundingBox,
          Sheet.NewSheet(0, 100, 100),
          parts,
          0D,
          SvgNest.Config.ClipperScale,
          requiresMergedLines: true,
          commonEdges: matches,
          commonLineGap: gap,
          geometryTolerance: tol);

        var exporter = new DxfExporter { PartNameExport = DxfPartNameExport.Background, RoleLayers = true };
        await exporter.Export(outPath, sheetPlacement, false);

        var exported = DxfFile.Load(outPath);
        exported.Entities.Should().Contain(e => e.Layer == DxfExporter.CommonLineLayer);

        var texts = exported.Entities.OfType<DxfText>().Where(t => t.Layer == DxfOutputLayers.Background.Name).ToList();
        texts.Should().HaveCount(2);
        texts[0].Value.Should().Be("PartCommon");
        texts[1].Value.Should().Be("PartCommon");
      }
      finally
      {
        if (Directory.Exists(tempDir))
        {
          try { Directory.Delete(tempDir, true); } catch { }
        }
      }
    }

    /// <summary>
    /// A real duct flat pattern (outline with bend notches on layer 0) with a bend line added on layer 9, the way
    /// the CAD exports it when bend lines are on. The bend line must not become part of the cut outline, and the
    /// sheet must carry the bend line and the file name, both turned with the part.
    /// </summary>
    [Theory]
    [InlineData(DxfPartNameExport.Background, DxfOutputLayers.BackgroundName)]
    [InlineData(DxfPartNameExport.Paint, DxfOutputLayers.PaintName)]
    public async Task AFlatPatternCarriesItsFileNameAndItsBendLineOntoTheSheet(DxfPartNameExport mode, string textLayer)
    {
      const string fileName = "Flat-Pattern - top^Tr_300x300.DXF";
      string original = SampleDrawing(fileName);
      File.Exists(original).Should().BeTrue("the sample drawing is in the repository: {0}", original);

      string dir = Path.Combine(Path.GetTempPath(), $"sheetnest_flat_{Guid.NewGuid():N}");
      Directory.CreateDirectory(dir);
      try
      {
        // The notches sit at x -2.4 .. 0.43; the bend runs between them, edge to edge.
        string part = Path.Combine(dir, fileName);
        var source = DxfFile.Load(original);
        source.Entities.Add(new DxfLine(new DxfPoint(-0.99, -30.4274333882, 0), new DxfPoint(-0.99, 480.4274333882, 0)) { Layer = "9" });
        source.Save(part);

        var raw = new NestExecutionHelper().LoadRawDetail(new FileInfo(part));
        raw.TryConvertToNfp(0, out INfp nfp).Should().BeTrue();
        (nfp.Children?.Count ?? 0).Should().Be(0, "a bend line is not a hole");
        (nfp.MaxX - nfp.MinX).Should().BeApproximately(527.38, 0.05, "the bend line must not change the outline");
        (nfp.MaxY - nfp.MinY).Should().BeApproximately(510.85, 0.05);

        var turned = nfp.Rotate(90);
        var placement = new PartPlacement(turned) { Id = 1, Source = 0, X = 600, Y = 100 };
        var sheet = new SheetPlacement(
          PlacementTypeEnum.BoundingBox, Sheet.NewSheet(1, 1250, 2500), new List<IPartPlacement> { placement }, 0, SvgNest.Config.ClipperScale);

        string outPath = Path.Combine(dir, "out.dxf");
        await new DxfExporter { PartNameExport = mode }.Export(outPath, sheet, false);
        var outDxf = DxfFile.Load(outPath);

        var placed = placement.PlacedPart;
        var text = outDxf.Entities.OfType<DxfText>().Should().ContainSingle().Subject;
        text.Value.Should().Be("top^Tr_300x300");
        text.Layer.Should().Be(textLayer);
        GeometryUtil.PointInPolygon(new SvgPoint(text.Location.X, text.Location.Y), placed).Should().BeTrue("the name is written on the part");

        var bend = outDxf.Entities.OfType<DxfLine>().Where(l => l.Layer == "9").Should().ContainSingle().Subject;
        bend.P1.Y.Should().BeApproximately(bend.P2.Y, 1e-3, "the part was turned a quarter, so its vertical bend now runs across");
        Math.Abs(bend.P1.X - bend.P2.X).Should().BeApproximately(510.85, 0.05);
        Math.Min(bend.P1.X, bend.P2.X).Should().BeApproximately(placed.MinX, 0.05);
        bend.P1.Y.Should().BeInRange(placed.MinY, placed.MaxY);
      }
      finally
      {
        try { Directory.Delete(dir, true); } catch { }
      }
    }

    /// <summary>
    /// The CAD's own export of a duct wrap: 28 cut lines and 10 bend lines, ALL on layer 0. Only the colour tells
    /// them apart (bends are grey, index 8), and the bends end on the outline mid-segment. The outline must still be
    /// the part's outline, and the bends must reach the sheet as bends, turned with the part.
    /// </summary>
    [Fact]
    public async Task BendLinesDrawnOnTheOutlinesLayerStayBendsAndReachTheSheet()
    {
      string part = SampleDrawing("Flat-Pattern - main^R2p_300x200_250x200.DXF");
      File.Exists(part).Should().BeTrue("the sample drawing is in the repository: {0}", part);

      var raw = new NestExecutionHelper().LoadRawDetail(new FileInfo(part));
      raw.TryConvertToNfp(0, out INfp nfp).Should().BeTrue();
      (nfp.Children?.Count ?? 0).Should().Be(0, "a bend line is not a hole");
      (nfp.MaxX - nfp.MinX).Should().BeApproximately(813.877, 0.05, "the bend lines must not change the outline");
      (nfp.MaxY - nfp.MinY).Should().BeApproximately(300.499, 0.05);

      string dir = Path.Combine(Path.GetTempPath(), $"sheetnest_bends_{Guid.NewGuid():N}");
      Directory.CreateDirectory(dir);
      try
      {
        var turned = nfp.Rotate(90);
        var placement = new PartPlacement(turned) { Id = 1, Source = 0, X = 400, Y = 100 };
        var sheet = new SheetPlacement(
          PlacementTypeEnum.BoundingBox, Sheet.NewSheet(1, 1250, 2500), new List<IPartPlacement> { placement }, 0, SvgNest.Config.ClipperScale);

        string outPath = Path.Combine(dir, "out.dxf");
        await new DxfExporter { PartNameExport = DxfPartNameExport.Paint }.Export(outPath, sheet, false);
        var outDxf = DxfFile.Load(outPath);
        var placed = placement.PlacedPart;

        var bends = outDxf.Entities.OfType<DxfLine>().Where(l => l.Layer == "9").ToList();
        bends.Should().HaveCount(10);
        bends.Should().OnlyContain(l =>
          Math.Min(l.P1.X, l.P2.X) >= placed.MinX - 0.05 && Math.Max(l.P1.X, l.P2.X) <= placed.MaxX + 0.05
          && Math.Min(l.P1.Y, l.P2.Y) >= placed.MinY - 0.05 && Math.Max(l.P1.Y, l.P2.Y) <= placed.MaxY + 0.05,
          "every bend lies on its part, turned with it");
        bends.Count(l => Math.Abs(l.P1.X - l.P2.X) < 1e-3).Should().Be(6, "the six long bends ran across the part and now run along the sheet");

        // The cut stays on the cut layer: nothing of the outline went to the bend layer.
        outDxf.Entities.Where(e => e.Layer != "9" && !(e is DxfText)).Should().NotBeEmpty();

        var text = outDxf.Entities.OfType<DxfText>().Should().ContainSingle().Subject;
        text.Value.Should().Be("main^R2p_300x200_250x200");
        text.Layer.Should().Be("9");
        GeometryUtil.PointInPolygon(new SvgPoint(text.Location.X, text.Location.Y), placed).Should().BeTrue("the name is written on the part");
      }
      finally
      {
        try { Directory.Delete(dir, true); } catch { }
      }
    }

    /// <summary>
    /// The laser's CAM sorts a file into operations by its colours. A hole used to come out red, the colour of
    /// the marking layer, so it was engraved instead of cut; on a common-line sheet the inner cuts, shared cuts,
    /// outer cuts and offcut each had a colour of their own. Now everything cut is on CUT in one colour, ahead
    /// of anything else, and the marking and display layers are the only others.
    /// </summary>
    [Fact]
    public async Task APartWithAHoleIsCutOnOneLayerInOneColour()
    {
      string dir = Path.Combine(Path.GetTempPath(), $"sheetnest_cut_{Guid.NewGuid():N}");
      Directory.CreateDirectory(dir);
      try
      {
        string part = Path.Combine(dir, "Plate.dxf");
        var source = new DxfFile();
        source.Header.Version = DxfAcadVersion.R2000;
        source.Entities.Add(new DxfLwPolyline(new[]
        {
          new DxfLwPolylineVertex { X = 0, Y = 0 },
          new DxfLwPolylineVertex { X = 80, Y = 0 },
          new DxfLwPolylineVertex { X = 80, Y = 60 },
          new DxfLwPolylineVertex { X = 0, Y = 60 },
        }) { IsClosed = true, Layer = "Contour", Color = DxfColor.FromIndex(3) });
        source.Entities.Add(new DxfCircle(new DxfPoint(40, 30, 0), 10) { Layer = "Contour", Color = DxfColor.FromIndex(5) });
        source.Save(part);

        var sheet = CreateTestSheetPlacement(part, 2);
        string outPath = Path.Combine(dir, "out.dxf");
        await new DxfExporter { PartNameExport = DxfPartNameExport.Paint }.Export(outPath, sheet, false);
        var outDxf = DxfFile.Load(outPath);

        var cut = outDxf.Entities.Where(e => !(e is DxfText)).ToList();
        cut.Should().NotBeEmpty();
        cut.Should().OnlyContain(e => e.Layer == DxfOutputLayers.CutName, "outlines and holes are both cut");
        outDxf.Entities.Should().OnlyContain(e => e.Color.IsByLayer, "the layer gives the colour, so a colour can't say otherwise");
        outDxf.Entities.OfType<DxfCircle>().Should().HaveCount(2, "one hole per part");
        outDxf.Entities.OfType<DxfText>().Should().OnlyContain(t => t.Layer == DxfOutputLayers.PaintName);
        outDxf.Entities.TakeWhile(e => e.Layer == DxfOutputLayers.CutName).Count().Should().Be(cut.Count, "the cut comes first");
        outDxf.Layers.Select(l => l.Name).Should().BeSubsetOf(new[] { DxfOutputLayers.CutName, DxfOutputLayers.PaintName, DxfOutputLayers.BackgroundName });
        outDxf.Layers.Single(l => l.Name == DxfOutputLayers.CutName).Color.Index.Should().Be((byte)DxfOutputLayers.Cut.ColorIndex);
      }
      finally
      {
        try { Directory.Delete(dir, true); } catch { }
      }
    }

    [Fact]
    public async Task ACommonLineSheetIsCutOnOneLayerInOneColour()
    {
      string dir = Path.Combine(Path.GetTempPath(), $"sheetnest_clcut_{Guid.NewGuid():N}");
      Directory.CreateDirectory(dir);
      try
      {
        string dxfPath = Path.Combine(dir, "PartCommon.dxf");
        WriteSquareDxf(dxfPath, 10);
        IRawDetail detail = new NestExecutionHelper().LoadRawDetail(new FileInfo(dxfPath));
        detail.TryConvertToNfp(0, out INfp p1).Should().BeTrue();
        detail.TryConvertToNfp(0, out INfp p2).Should().BeTrue();

        const double gap = 0.01;
        const double tol = 1e-4;
        var parts = new List<IPartPlacement>
        {
          new PartPlacement(p1) { Id = 0, Source = 0, X = 0D, Y = 0D },
          new PartPlacement(p2) { Id = 1, Source = 0, X = 10D + gap, Y = 0D },
        };
        var matches = new List<CommonEdge>();
        for (int a = 0; a < parts[0].Part.Points.Length; a++)
        {
          for (int b = 0; b < parts[1].Part.Points.Length; b++)
          {
            if (CommonLineGeometryResolver.TryCreateCommonEdge(parts[0], a, parts[1], b, gap, tol, out CommonEdge edge))
            {
              matches.Add(edge);
            }
          }
        }

        var sheetPlacement = new SheetPlacement(
          PlacementTypeEnum.BoundingBox, Sheet.NewSheet(0, 100, 100), parts, 0D, SvgNest.Config.ClipperScale,
          requiresMergedLines: true, commonEdges: matches, commonLineGap: gap, geometryTolerance: tol);

        string outPath = Path.Combine(dir, "out.dxf");
        await new DxfExporter { PartNameExport = DxfPartNameExport.Background }.Export(
          outPath, sheetPlacement, false, new[] { new OffcutLine { X1 = 0, Y1 = 50, X2 = 100, Y2 = 50 } });
        var outDxf = DxfFile.Load(outPath);

        outDxf.Entities.Where(e => !(e is DxfText)).Should().HaveCountGreaterThan(2)
          .And.OnlyContain(e => e.Layer == DxfOutputLayers.CutName, "inner, shared, outer and offcut cuts are all cut");
        outDxf.Entities.Should().OnlyContain(e => e.Color.IsByLayer);
        outDxf.Entities.OfType<DxfText>().Should().HaveCount(2).And.OnlyContain(t => t.Layer == DxfOutputLayers.BackgroundName);
        outDxf.Layers.Select(l => l.Name).Should().NotContain(new[] { DxfExporter.CutInnerLayer, DxfExporter.CommonLineLayer, DxfExporter.CutOuterLayer, DxfExporter.OffcutLayer });
      }
      finally
      {
        try { Directory.Delete(dir, true); } catch { }
      }
    }

    private static string SampleDrawing(string name, [System.Runtime.CompilerServices.CallerFilePath] string here = "")
      => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here), "..", "..", "test-data", "dxf", name));

    private static NoFitPolygon CreateRectangleNfp(double x, double y, double w, double h)
    {
      return new NoFitPolygon(new[]
      {
        new SvgPoint(x, y),
        new SvgPoint(x + w, y),
        new SvgPoint(x + w, y + h),
        new SvgPoint(x, y + h),
      });
    }

    private static void WriteSquareDxf(string path, double size)
    {
      var dxf = new DxfFile();
      dxf.Header.Version = DxfAcadVersion.R2000;
      var lw = new DxfLwPolyline(new[]
      {
        new DxfLwPolylineVertex { X = 0, Y = 0 },
        new DxfLwPolylineVertex { X = size, Y = 0 },
        new DxfLwPolylineVertex { X = size, Y = size },
        new DxfLwPolylineVertex { X = 0, Y = size },
      })
      {
        IsClosed = true,
        Layer = "0",
      };
      dxf.Entities.Add(lw);
      dxf.Save(path, true);
    }

    private static SheetPlacement CreateTestSheetPlacement(string dxfPath, int count)
    {
      var helper = new NestExecutionHelper();
      var raw = helper.LoadRawDetail(new FileInfo(dxfPath));
      raw.TryConvertToNfp(0, out INfp nfp);

      var sheet = Sheet.NewSheet(1, 1000, 1000);
      var placements = new List<IPartPlacement>();
      for (int i = 0; i < count; i++)
      {
        placements.Add(new PartPlacement(nfp)
        {
          Id = i + 1,
          Source = 0,
          X = 10 + (i * 90),
          Y = 10,
        });
      }

      return new SheetPlacement(PlacementTypeEnum.BoundingBox, sheet, placements, 0, SvgNest.Config.ClipperScale);
    }
  }
}
