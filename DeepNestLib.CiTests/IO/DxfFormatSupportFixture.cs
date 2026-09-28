namespace DeepNestLib.CiTests.IO
{
  using System.Collections.Generic;
  using System.IO;
  using System.Linq;
  using System.Threading.Tasks;
  using DeepNestLib.IO;
  using DeepNestLib.Placement;
  using FluentAssertions;
  using Xunit;

  public class DxfFormatSupportFixture
  {
    [Theory]
    [InlineData("part.dxf", true)]
    [InlineData("part.DXF", true)]
    [InlineData("part.dfx", true)]
    [InlineData("part.DFX", true)]
    [InlineData("C:\\path\\to\\file.Dxf", true)]
    [InlineData("C:\\path\\to\\file.Dfx", true)]
    [InlineData("part.step", false)]
    [InlineData("part.dnpoly", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsDxfRecognizesDxfAndDfxVariations(string path, bool expected)
    {
      DxfFileExtensions.IsDxf(path).Should().Be(expected);
    }

    [Fact]
    public void LoadRawDetailLoadsUppercaseDxf()
    {
      string tempDxf = Path.Combine(Path.GetTempPath(), "test_part.DXF");
      try
      {
        File.WriteAllText(tempDxf, "0\nSECTION\n2\nENTITIES\n0\nLINE\n8\n0\n10\n0.0\n20\n0.0\n11\n10.0\n21\n0.0\n0\nLINE\n8\n0\n10\n10.0\n20\n0.0\n11\n10.0\n21\n10.0\n0\nLINE\n8\n0\n10\n10.0\n20\n10.0\n11\n0.0\n21\n10.0\n0\nLINE\n8\n0\n10\n0.0\n20\n10.0\n11\n0.0\n21\n0.0\n0\nENDSEC\n0\nEOF\n");
        var helper = new NestExecutionHelper();
        var raw = helper.LoadRawDetail(new FileInfo(tempDxf));
        raw.Should().NotBeNull();
        raw.TryConvertToNfp(0, out INfp nfp).Should().BeTrue();
        nfp.Points.Length.Should().BeGreaterThanOrEqualTo(4);
      }
      finally
      {
        if (File.Exists(tempDxf))
        {
          File.Delete(tempDxf);
        }
      }
    }

    [Fact]
    public void LoadRawDetailLoadsDfxFile()
    {
      string tempDfx = Path.Combine(Path.GetTempPath(), "test_part.dfx");
      try
      {
        File.WriteAllText(tempDfx, "0\nSECTION\n2\nENTITIES\n0\nLINE\n8\n0\n10\n0.0\n20\n0.0\n11\n10.0\n21\n0.0\n0\nLINE\n8\n0\n10\n10.0\n20\n0.0\n11\n10.0\n21\n10.0\n0\nLINE\n8\n0\n10\n10.0\n20\n10.0\n11\n0.0\n21\n10.0\n0\nLINE\n8\n0\n10\n0.0\n20\n10.0\n11\n0.0\n21\n0.0\n0\nENDSEC\n0\nEOF\n");
        var helper = new NestExecutionHelper();
        var raw = helper.LoadRawDetail(new FileInfo(tempDfx));
        raw.Should().NotBeNull();
        raw.TryConvertToNfp(0, out INfp nfp).Should().BeTrue();
        nfp.Points.Length.Should().BeGreaterThanOrEqualTo(4);
      }
      finally
      {
        if (File.Exists(tempDfx))
        {
          File.Delete(tempDfx);
        }
      }
    }

    [Fact]
    public void LoadRawDetailLoadsFlatPatternPlankWhenPresent()
    {
      string localPath = Path.Combine(Directory.GetCurrentDirectory(), "Flat-Pattern - plank.DXF");
      if (!File.Exists(localPath))
      {
        localPath = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "..", "..", "Flat-Pattern - plank.DXF"));
      }

      if (File.Exists(localPath))
      {
        var helper = new NestExecutionHelper();
        var raw = helper.LoadRawDetail(new FileInfo(localPath));
        raw.Should().NotBeNull();
        raw.TryConvertToNfp(0, out INfp nfp).Should().BeTrue();
        nfp.Points.Length.Should().BeGreaterThan(0);
      }
    }

    [Fact]
    public void LoadRawDetailLoadsFlatPatternMain()
    {
      string path = @"c:\WORKS\Поръчки\2026.09.25-Rusi\0.6\R_300x200_250x200_L300\Flat-Pattern - main^R2p_300x200_250x200.DXF";
      if (File.Exists(path))
      {
        var helper = new NestExecutionHelper();
        var raw = helper.LoadRawDetail(new FileInfo(path));
        raw.Should().NotBeNull();
        raw.TryConvertToNfp(0, out INfp nfp).Should().BeTrue();
        nfp.Points.Length.Should().BeGreaterThanOrEqualTo(28);
        nfp.WidthCalculated.Should().BeGreaterThan(700);
        nfp.HeightCalculated.Should().BeGreaterThan(250);
      }
    }

    [Fact]
    public void LoadRawDetailLoadsFlatPatternSmall()
    {
      string path = @"c:\WORKS\Поръчки\2026.09.25-Rusi\0.6\R_300x200_250x200_L300\Flat-Pattern - small^R2p_300x200_250x200.DXF";
      if (File.Exists(path))
      {
        var helper = new NestExecutionHelper();
        var raw = helper.LoadRawDetail(new FileInfo(path));
        raw.Should().NotBeNull();
        raw.TryConvertToNfp(0, out INfp nfp).Should().BeTrue();
        nfp.Points.Length.Should().BeGreaterThanOrEqualTo(12);
      }
    }

    [Fact]
    public async Task ExportPreservesBendLinesOnSecondLayerFromSyntheticDxf()
    {
      string tempDxf = Path.Combine(Path.GetTempPath(), "test_bend_part.DXF");
      string tempOut = Path.Combine(Path.GetTempPath(), "test_bend_out.dxf");
      try
      {
        var src = new IxMilia.Dxf.DxfFile();
        src.Header.Version = IxMilia.Dxf.DxfAcadVersion.R2000;
        var bendLayer = new IxMilia.Dxf.DxfLayer("9", IxMilia.Dxf.DxfColor.FromIndex(1))
        {
          LineTypeName = "PHANTOM",
        };
        src.Layers.Add(bendLayer);

        var phantomLt = new IxMilia.Dxf.DxfLineType("PHANTOM")
        {
          Description = "Phantom linetype",
        };
        phantomLt.Elements.Add(new IxMilia.Dxf.DxfLineTypeElement { DashDotSpaceLength = 12.7 });
        phantomLt.Elements.Add(new IxMilia.Dxf.DxfLineTypeElement { DashDotSpaceLength = -6.35 });
        src.LineTypes.Add(phantomLt);

        // Outer 100x100 contour (Layer 0)
        src.Entities.Add(new IxMilia.Dxf.Entities.DxfLine(new IxMilia.Dxf.DxfPoint(0, 0, 0), new IxMilia.Dxf.DxfPoint(100, 0, 0)) { Layer = "0" });
        src.Entities.Add(new IxMilia.Dxf.Entities.DxfLine(new IxMilia.Dxf.DxfPoint(100, 0, 0), new IxMilia.Dxf.DxfPoint(100, 100, 0)) { Layer = "0" });
        src.Entities.Add(new IxMilia.Dxf.Entities.DxfLine(new IxMilia.Dxf.DxfPoint(100, 100, 0), new IxMilia.Dxf.DxfPoint(0, 100, 0)) { Layer = "0" });
        src.Entities.Add(new IxMilia.Dxf.Entities.DxfLine(new IxMilia.Dxf.DxfPoint(0, 100, 0), new IxMilia.Dxf.DxfPoint(0, 0, 0)) { Layer = "0" });

        // Internal bend line at Y = 50
        src.Entities.Add(new IxMilia.Dxf.Entities.DxfLine(new IxMilia.Dxf.DxfPoint(0, 50, 0), new IxMilia.Dxf.DxfPoint(100, 50, 0)) { Layer = "0", Color = IxMilia.Dxf.DxfColor.FromIndex(8) });

        src.Save(tempDxf, true);

        var helper = new NestExecutionHelper();
        var raw = helper.LoadRawDetail(new FileInfo(tempDxf));
        raw.Should().NotBeNull();
        raw.TryConvertToNfp(0, out INfp nfp).Should().BeTrue();

        var sheet = Sheet.NewSheet(1, 500, 500);
        var placements = new List<IPartPlacement>
        {
          new PartPlacement(nfp) { X = 20, Y = 30, Rotation = 0, Id = 0, Source = 0 },
        };
        var sheetPlacement = new SheetPlacement(PlacementTypeEnum.BoundingBox, sheet, placements, 0, SvgNest.Config.ClipperScale);

        var exporter = new DxfExporter();
        await exporter.Export(tempOut, sheetPlacement, false, false);

        File.Exists(tempOut).Should().BeTrue();
        var outDxf = IxMilia.Dxf.DxfFile.Load(tempOut);

        // Check Layer 9 exists with PHANTOM
        outDxf.Layers.Should().Contain(l => l.Name == "9");
        var layer9 = outDxf.Layers.Single(l => l.Name == "9");
        layer9.LineTypeName.Should().Be("PHANTOM");

        // Check PHANTOM linetype was copied
        outDxf.LineTypes.Should().Contain(lt => lt.Name == "PHANTOM");

        // Check bend line was exported on Layer 9 and transformed to (20, 80) -> (120, 80)
        var bendLines = outDxf.Entities.Where(e => e.Layer == "9").OfType<IxMilia.Dxf.Entities.DxfLine>().ToList();
        bendLines.Should().HaveCount(1);
        bendLines[0].P1.X.Should().BeApproximately(20, 1e-4);
        bendLines[0].P1.Y.Should().BeApproximately(80, 1e-4);
        bendLines[0].P2.X.Should().BeApproximately(120, 1e-4);
        bendLines[0].P2.Y.Should().BeApproximately(80, 1e-4);
      }
      finally
      {
        if (File.Exists(tempDxf)) File.Delete(tempDxf);
        if (File.Exists(tempOut)) File.Delete(tempOut);
      }
    }

    [Fact]
    public async Task ExportPreservesBendLinesForFlatPatternMainWhenPresent()
    {
      string path = @"c:\WORKS\Поръчки\2026.09.25-Rusi\0.6\R_300x200_250x200_L300\Flat-Pattern - main^R2p_300x200_250x200.DXF";
      if (!File.Exists(path))
      {
        return;
      }

      string tempOut = Path.Combine(Path.GetTempPath(), "test_main_bend_out.dxf");
      try
      {
        var helper = new NestExecutionHelper();
        var raw = helper.LoadRawDetail(new FileInfo(path));
        raw.TryConvertToNfp(0, out INfp nfp).Should().BeTrue();

        var sheet = Sheet.NewSheet(1, 1000, 500);
        var placements = new List<IPartPlacement>
        {
          new PartPlacement(nfp) { X = 10, Y = 10, Rotation = 0, Id = 0, Source = 0 },
        };
        var sheetPlacement = new SheetPlacement(PlacementTypeEnum.BoundingBox, sheet, placements, 0, SvgNest.Config.ClipperScale);

        var exporter = new DxfExporter();
        await exporter.Export(tempOut, sheetPlacement, false, false);

        var outDxf = IxMilia.Dxf.DxfFile.Load(tempOut);
        outDxf.Layers.Should().Contain(l => l.Name == "9");
        var bendLines = outDxf.Entities.Where(e => e.Layer == "9").ToList();
        bendLines.Should().HaveCount(10);
      }
      finally
      {
        if (File.Exists(tempOut)) File.Delete(tempOut);
      }
    }

    [Fact]
    public void LoadTestDataDxfFiles_AllParseSuccessfully()
    {
      string dir = Path.Combine(Directory.GetCurrentDirectory(), "test-data", "dxf");
      if (!Directory.Exists(dir))
      {
        dir = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "..", "..", "test-data", "dxf"));
      }

      if (!Directory.Exists(dir))
      {
        return;
      }

      var dxfFiles = Directory.GetFiles(dir, "Flat-Pattern*.dxf", SearchOption.TopDirectoryOnly)
        .Concat(Directory.GetFiles(dir, "Flat-Pattern*.DXF", SearchOption.TopDirectoryOnly))
        .Distinct()
        .ToList();

      dxfFiles.Should().NotBeEmpty();

      var helper = new NestExecutionHelper();
      foreach (var file in dxfFiles)
      {
        var raw = helper.LoadRawDetail(new FileInfo(file));
        raw.Should().NotBeNull($"File {Path.GetFileName(file)} should load");
        bool converted = raw.TryConvertToNfp(0, out INfp nfp);
        converted.Should().BeTrue($"File {Path.GetFileName(file)} should convert to Nfp");
        nfp.Should().NotBeNull();
        nfp.Points.Length.Should().BeGreaterThan(2, $"File {Path.GetFileName(file)} should have points");
        System.Math.Abs(nfp.Area).Should().BeGreaterThan(0, $"File {Path.GetFileName(file)} should have non-zero area");
      }
    }
  }
}
