namespace DeepNestSharp.CiTests.RasterNest
{
  using System;
  using System.Collections.Generic;
  using System.Diagnostics;
  using System.IO;
  using System.Linq;
  using ClipperLib;
  using DeepNestLib;
  using DeepNestLib.IO;
  using DeepNestSharp.RasterNest;
  using FluentAssertions;
  using IxMilia.Dxf;
  using IxMilia.Dxf.Entities;
  using Xunit;
  using Xunit.Abstractions;

  /// <summary>
  /// A sheet read from a drawing: an L-shaped offcut with a hole in it. Parts go only where there is metal,
  /// keep the margin from its edge and from the hole, and keep their spacing from each other.
  /// </summary>
  public class ShapedSheetFixture : IDisposable
  {
    private readonly ITestOutputHelper output;
    private readonly string dir = Path.Combine(Path.GetTempPath(), "SheetNestShapedSheet", Guid.NewGuid().ToString("N"));

    public ShapedSheetFixture(ITestOutputHelper output)
    {
      this.output = output;
      Directory.CreateDirectory(this.dir);
    }

    public void Dispose()
    {
      try
      {
        Directory.Delete(this.dir, true);
      }
      catch (IOException)
      {
      }
    }

    [Fact]
    public void ReadsTheOutlineAndTheHoleAtTheOrigin()
    {
      string sheetPath = this.WriteLSheet();
      var shape = SheetShape.Load(sheetPath);

      shape.MinX.Should().BeApproximately(0, 1e-9);
      shape.MinY.Should().BeApproximately(0, 1e-9);
      shape.MaxX.Should().BeApproximately(1000, 1e-9);
      shape.MaxY.Should().BeApproximately(600, 1e-9);
      shape.Children.Should().HaveCount(1, "the square inside the outline is a hole; the one beside it is not part of the sheet");
      SheetShape.IsShaped(shape).Should().BeTrue();
      SheetShape.IsShaped(Sheet.NewSheet(1, 1000, 600)).Should().BeFalse();
    }

    [Fact]
    public void PartsStayOnTheMetal()
    {
      string exe = Environment.GetEnvironmentVariable("SPARROW_EXE");
      if (string.IsNullOrWhiteSpace(exe) || !File.Exists(exe))
      {
        // The shaped sheet does not run the engine, but the service will not start without one.
        exe = Path.Combine(this.dir, "sparrow.exe");
        File.WriteAllText(exe, string.Empty);
      }

      var shape = SheetShape.Load(this.WriteLSheet());
      string partPath = Path.Combine(this.dir, "square.dxf");
      WriteDxf(partPath, Rect(0, 0, 180, 180));
      var parts = new List<RasterPartInfo> { new RasterPartInfo { Path = partPath, Quantity = 20 } };
      const double margin = 5, spacing = 4;

      var watch = Stopwatch.StartNew();
      var result = SparrowNestService.Nest(
        parts,
        new[] { (1000, 600, 1, false, 1) },
        rotations: 4,
        spacing: spacing,
        margin: margin,
        perSheetBudgetSec: 5,
        sparrowExePath: exe,
        error: out string error,
        stockShapes: new[] { shape });
      this.output.WriteLine($"{watch.ElapsedMilliseconds} ms");

      result.Should().NotBeNull(error);
      result.UsedSheets.Should().HaveCount(1);
      var sheet = result.UsedSheets[0];
      SheetShape.IsShaped(sheet.Sheet).Should().BeTrue("the layout is drawn and saved on the offcut's own outline");
      var reopened = DeepNestLib.Placement.SheetPlacement.FromJson(((DeepNestLib.Placement.SheetPlacement)sheet).ToJson());
      SheetShape.IsShaped(reopened.Sheet).Should().BeTrue("a saved project opens on the same outline");
      reopened.Sheet.Children.Should().HaveCount(1);

      // The cut file carries the offcut's outline and hole on Background, shown and never cut.
      var dxf = new DxfFile();
      DxfExporter.AppendSheetOutline(dxf, sheet.Sheet, 0);
      dxf.Entities.Should().HaveCount(2);
      dxf.Entities.Should().OnlyContain(e => e.Layer == DxfOutputLayers.BackgroundName);
      DxfExporter.AppendSheetOutline(dxf, Sheet.NewSheet(1, 1000, 600), 0);
      dxf.Entities.Should().HaveCount(2, "a rectangle writes no outline, as before");
      var placed = sheet.PartPlacements.Select(p => p.PlacedPart).ToList();
      this.output.WriteLine($"placed {placed.Count}");

      // The hole keeps squares off the bottom left. Settling along the short side as well finds eight.
      placed.Count.Should().Be(8);
      (placed.Count + result.UnplacedParts.Count).Should().Be(20);

      const double scale = 1e4;
      var metal = new List<List<IntPoint>>
      {
        ScaledPath(new[] { (margin, margin), (1000 - margin, margin), (1000 - margin, 300 - margin), (600 - margin, 300 - margin), (600 - margin, 600 - margin), (margin, 600 - margin) }, scale),
      };
      var hole = ScaledPath(new[] { (150 - margin, 150 - margin), (250 + margin, 150 - margin), (250 + margin, 250 + margin), (150 - margin, 250 + margin) }, scale);
      foreach (var part in placed)
      {
        var outline = Ccw(DeepNestClipper.ScaleUpPath(part.Points, scale));
        Area(Clip(new List<List<IntPoint>> { outline }, metal, ClipType.ctDifference)).Should().BeLessThan(1e-6, "a part must not leave the metal or come within the margin of its edge");
        Area(Clip(new List<List<IntPoint>> { outline }, new List<List<IntPoint>> { hole }, ClipType.ctIntersection)).Should().BeLessThan(1e-6, "a part must keep the margin from the hole");
      }

      for (int i = 0; i < placed.Count; i++)
      {
        for (int j = i + 1; j < placed.Count; j++)
        {
          var a = placed[i];
          var b = placed[j];
          double gapX = Math.Max(a.MinX - b.MaxX, b.MinX - a.MaxX);
          double gapY = Math.Max(a.MinY - b.MaxY, b.MinY - a.MaxY);
          Math.Max(gapX, gapY).Should().BeGreaterOrEqualTo(spacing - 1e-3, "squares keep the spacing between them");
        }
      }
    }

    /// <summary>Real flat patterns and a heap of small round blanks: everything on the metal, nothing
    /// overlapping, and in seconds rather than minutes.</summary>
    [Fact]
    public void RealPartsAndManySmallOnesInGoodTime()
    {
      string exe = Path.Combine(this.dir, "sparrow.exe");
      File.WriteAllText(exe, string.Empty);
      string data = FindTestData();
      if (data == null)
      {
        this.output.WriteLine("test-data/dxf not found — skipping.");
        return;
      }

      string discPath = Path.Combine(this.dir, "disc.dxf");
      WriteDxf(discPath, new DxfCircle(new DxfPoint(0, 0, 0), 15) { Layer = "0" });
      var parts = Directory.GetFiles(data, "Flat-Pattern*.DXF")
        .Select(p => new RasterPartInfo { Path = p, Quantity = 2 })
        .Append(new RasterPartInfo { Path = discPath, Quantity = 300 })
        .ToList();

      var shape = SheetShape.Load(this.WriteLSheet());
      var watch = Stopwatch.StartNew();
      var result = SparrowNestService.Nest(
        parts,
        new[] { (1000, 600, 1, false, 1) },
        rotations: 4,
        spacing: 4,
        margin: 5,
        perSheetBudgetSec: 5,
        sparrowExePath: exe,
        error: out string error,
        stockShapes: new[] { shape });
      long ms = watch.ElapsedMilliseconds;

      result.Should().NotBeNull(error);
      var sheet = result.UsedSheets.Single();
      this.output.WriteLine($"{sheet.PartPlacements.Count} placed in {ms} ms");
      sheet.PartPlacements.Should().OnlyContain(pp => !SparrowNestService.IsOutsideShape(pp, sheet.Sheet, 5));
      sheet.PartPlacements.Count.Should().BeGreaterThan(100);
      ms.Should().BeLessThan(60000);

      // Each grown by a hair under half the spacing: two that keep the spacing cannot meet.
      const double scale = 1e4;
      var grown = sheet.PartPlacements.Select(pp =>
      {
        var co = new ClipperOffset();
        co.AddPath(Ccw(DeepNestClipper.ScaleUpPath(pp.PlacedPart.Points, scale)), JoinType.jtRound, EndType.etClosedPolygon);
        var solution = new List<List<IntPoint>>();
        co.Execute(ref solution, (2 - 0.01) * scale);
        return solution;
      }).ToList();
      for (int i = 0; i < grown.Count; i++)
      {
        for (int j = i + 1; j < grown.Count; j++)
        {
          Area(Clip(grown[i], grown[j], ClipType.ctIntersection)).Should().BeLessThan(1e-6, $"parts {i} and {j} must keep the spacing");
        }
      }
    }

    /// <summary>
    /// The shop's round offcut, Ø795, and elbow sides (test-data/bad, 2026-10-23). The first version put three
    /// of the four ordered parts on it and left half the circle empty: a sliver of zero area where two no-fit
    /// polygons met read as free room, a part landed on top of another and the final check took it off. The
    /// operator's own layout holds four of the big sides and one small; so must ours, and in seconds.
    /// </summary>
    [Theory]
    [InlineData(2, 2)]
    [InlineData(4, 1)]
    public void RoundOffcutHoldsWhatTheOperatorLaidByHand(int big, int small)
    {
      string exe = Path.Combine(this.dir, "sparrow.exe");
      File.WriteAllText(exe, string.Empty);
      string data = FindTestData();
      if (data == null)
      {
        this.output.WriteLine("test-data not found — skipping.");
        return;
      }

      string bad = Path.Combine(Path.GetDirectoryName(data), "bad");
      var parts = new List<RasterPartInfo>
      {
        new RasterPartInfo { Path = Path.Combine(bad, "side-E90_200_R100.dxf"), Quantity = big, Rotations = 36, Spacing = 5.5 },
        new RasterPartInfo { Path = Path.Combine(bad, "side-E90_150_R100.dxf"), Quantity = small, Rotations = 36, Spacing = 5.5 },
      };
      var shape = SheetShape.Load(Path.Combine(bad, "round-offcut-795.dxf"));
      var watch = Stopwatch.StartNew();
      var result = SparrowNestService.Nest(parts, new[] { (795, 795, 1, false, 1) }, 4, 5.5, 6, 5, exe, out string error, stockShapes: new[] { shape });
      this.output.WriteLine($"{watch.ElapsedMilliseconds} ms");

      result.Should().NotBeNull(error);
      result.UnplacedParts.Should().BeEmpty();
      var sheet = result.UsedSheets.Single();
      sheet.PartPlacements.Should().HaveCount(big + small);
      sheet.PartPlacements.Should().OnlyContain(pp => !SparrowNestService.IsOutsideShape(pp, sheet.Sheet, 6));
      watch.ElapsedMilliseconds.Should().BeLessThan(30000);
    }

    /// <summary>
    /// The same offcut and sides at the shop's 6.35 spacing (bad2, bad2a). Filling as a kit gave one of each
    /// while the same two parts by count went on three and two: the big side took all the room first and the
    /// kit was evened down. A kit now goes on one of each in turn, so it holds at least what a count does.
    /// </summary>
    [Fact]
    public void RoundOffcutFilledAsAKitHoldsWhatACountDoes()
    {
      string exe = Path.Combine(this.dir, "sparrow.exe");
      File.WriteAllText(exe, string.Empty);
      string data = FindTestData();
      if (data == null)
      {
        return;
      }

      string bad = Path.Combine(Path.GetDirectoryName(data), "bad");
      var shape = SheetShape.Load(Path.Combine(bad, "round-offcut-795.dxf"));
      DeepNestLib.Placement.INestResult Run(int qty, bool fill) => SparrowNestService.Nest(
        new List<RasterPartInfo>
        {
          new RasterPartInfo { Path = Path.Combine(bad, "side-E90_150_R100.dxf"), Quantity = qty, Fill = fill, Rotations = 36, Spacing = 6.35 },
          new RasterPartInfo { Path = Path.Combine(bad, "side-E90_200_R100.dxf"), Quantity = qty, Fill = fill, Rotations = 36, Spacing = 6.35 },
        },
        new[] { (795, 795, 1, false, 1) },
        4,
        6.35,
        6,
        5,
        exe,
        out _,
        stockShapes: new[] { shape });

      int Count(DeepNestLib.Placement.INestResult r, string name) => r.UsedSheets.Sum(s => s.PartPlacements.Count(p => p.Part.Name.Contains(name)));
      var byCount = Run(3, false);
      var filled = Run(1, true);
      this.output.WriteLine($"by count {Count(byCount, "E90_200")}+{Count(byCount, "E90_150")}, filled {Count(filled, "E90_200")}+{Count(filled, "E90_150")}");

      int kits = Math.Min(Count(byCount, "E90_200"), Count(byCount, "E90_150"));
      Count(filled, "E90_150").Should().Be(Count(filled, "E90_200"), "a kit comes out even");
      Count(filled, "E90_150").Should().BeGreaterOrEqualTo(Math.Max(2, kits));
      (Count(byCount, "E90_200") + Count(byCount, "E90_150")).Should().BeGreaterOrEqualTo(5);
    }

    /// <summary>
    /// Washers M8 (Ø16) filling the shop's Ø638 round offcut at 5.5 spacing and margin (BAD3, bad4.chf). The
    /// part-by-part pack scattered 665; the operator's straight rows held 659; rows shifted half a step hold
    /// about 745, and that is what the sheet gets now, in rows.
    /// </summary>
    [Fact]
    public void WashersFillARoundOffcutInShiftedRows()
    {
      string exe = Path.Combine(this.dir, "sparrow.exe");
      File.WriteAllText(exe, string.Empty);
      string data = FindTestData();
      if (data == null)
      {
        return;
      }

      string bad = Path.Combine(Path.GetDirectoryName(data), "bad");
      var shape = SheetShape.Load(Path.Combine(bad, "round-offcut-638.dxf"));
      var watch = Stopwatch.StartNew();
      var result = SparrowNestService.Nest(
        new List<RasterPartInfo> { new RasterPartInfo { Path = Path.Combine(bad, "washer-M8-D16.dxf"), Quantity = 1, Fill = true } },
        new[] { (638, 638, 1, false, 1) },
        4,
        5.5,
        5.5,
        5,
        exe,
        out string error,
        stockShapes: new[] { shape });
      this.output.WriteLine($"{watch.ElapsedMilliseconds} ms");

      result.Should().NotBeNull(error);
      var sheet = result.UsedSheets.Single();
      this.output.WriteLine($"{sheet.PartPlacements.Count} washers");
      sheet.PartPlacements.Count.Should().BeGreaterOrEqualTo(730);
      sheet.PartPlacements.Should().OnlyContain(pp => !SparrowNestService.IsOutsideShape(pp, sheet.Sheet, 5.5));

      // In rows: a handful of distinct heights, not one per washer.
      sheet.PartPlacements.Select(p => Math.Round(p.Y, 2)).Distinct().Count().Should().BeLessThan(40);
      var centres = sheet.PartPlacements.Select(p => (X: p.X, Y: p.Y)).ToList();
      double nearest = centres.Take(100).Min(a => centres.Where(b => b != a).Min(b => Math.Sqrt(Math.Pow(a.X - b.X, 2) + Math.Pow(a.Y - b.Y, 2))));
      nearest.Should().BeGreaterOrEqualTo(21.5 - 1e-3, "16 mm washers keep 5.5 mm apart");
      watch.ElapsedMilliseconds.Should().BeLessThan(30000);
    }

    private static string FindTestData()
    {
      for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
      {
        string candidate = Path.Combine(d.FullName, "test-data", "dxf");
        if (Directory.Exists(candidate))
        {
          return candidate;
        }
      }

      return null;
    }

    private static List<IntPoint> ScaledPath((double X, double Y)[] points, double scale)
      => points.Select(p => new IntPoint((long)Math.Round(p.X * scale), (long)Math.Round(p.Y * scale))).ToList();

    private static List<IntPoint> Ccw(List<IntPoint> path)
    {
      if (Clipper.Area(path) < 0)
      {
        path.Reverse();
      }

      return path;
    }

    private static List<List<IntPoint>> Clip(List<List<IntPoint>> subject, List<List<IntPoint>> clip, ClipType type)
    {
      var c = new Clipper();
      c.AddPaths(subject, PolyType.ptSubject, true);
      c.AddPaths(clip, PolyType.ptClip, true);
      var result = new List<List<IntPoint>>();
      c.Execute(type, result, PolyFillType.pftNonZero, PolyFillType.pftNonZero);
      return result;
    }

    private static double Area(List<List<IntPoint>> paths) => paths.Sum(p => Math.Abs(Clipper.Area(p))) / 1e8;

    private static DxfLwPolyline Rect(double x, double y, double w, double h)
      => new DxfLwPolyline(new[]
      {
        new DxfLwPolylineVertex { X = x, Y = y },
        new DxfLwPolylineVertex { X = x + w, Y = y },
        new DxfLwPolylineVertex { X = x + w, Y = y + h },
        new DxfLwPolylineVertex { X = x, Y = y + h },
      })
      {
        IsClosed = true,
        Layer = "0",
      };

    private static void WriteDxf(string path, params DxfEntity[] entities)
    {
      var file = new DxfFile();
      file.Header.Version = DxfAcadVersion.R2007;
      foreach (var entity in entities)
      {
        file.Entities.Add(entity);
      }

      file.Save(path, true);
    }

    /// <summary>1000 x 600 with the top right 400 x 300 gone and a 100 x 100 hole, drawn away from the origin,
    /// plus a stray square beside it that is not part of the sheet.</summary>
    private string WriteLSheet()
    {
      const double ox = 5000, oy = -200;
      var outline = new DxfLwPolyline(new[]
      {
        new DxfLwPolylineVertex { X = ox, Y = oy },
        new DxfLwPolylineVertex { X = ox + 1000, Y = oy },
        new DxfLwPolylineVertex { X = ox + 1000, Y = oy + 300 },
        new DxfLwPolylineVertex { X = ox + 600, Y = oy + 300 },
        new DxfLwPolylineVertex { X = ox + 600, Y = oy + 600 },
        new DxfLwPolylineVertex { X = ox, Y = oy + 600 },
      })
      {
        IsClosed = true,
        Layer = "0",
      };
      string path = System.IO.Path.Combine(this.dir, "offcut.dxf");
      WriteDxf(path, outline, Rect(ox + 150, oy + 150, 100, 100), Rect(ox + 1200, oy, 50, 50));
      return path;
    }
  }
}
