namespace DeepNestSharp.CiTests.RasterNest
{
  using System;
  using System.Collections.Generic;
  using System.Diagnostics;
  using System.IO;
  using System.Linq;
  using DeepNestLib.Placement;
  using DeepNestSharp.RasterNest;
  using FluentAssertions;
  using Xunit;
  using Xunit.Abstractions;

  /// <summary>
  /// The user's circles (test-data/circles, 2026-11-25): 500 discs Ø20 and 200 Ø40 on one 1250 × 2500 sheet at
  /// 5 mm. Sparrow took a minute over them, dsh 105 s, RNest did not answer in 20 min.
  /// </summary>
  public class CirclesFixture
  {
    private readonly ITestOutputHelper output;

    public CirclesFixture(ITestOutputHelper output) => this.output = output;

    [Fact]
    public void SevenHundredCirclesGoOnQuickly()
    {
      string exe = SparrowExe.Resolve();
      string data = FindData();
      if (exe == null || data == null)
      {
        return;
      }

      var parts = new List<RasterPartInfo>
      {
        new RasterPartInfo { Path = Path.Combine(data, "D20.dxf"), Quantity = 500, Rotations = 4, Spacing = 5 },
        new RasterPartInfo { Path = Path.Combine(data, "D40.dxf"), Quantity = 200, Rotations = 4, Spacing = 5 },
      };

      var clock = Stopwatch.StartNew();
      double firstAt = -1;
      INestResult first = null;
      var result = SparrowNestService.Nest(
        parts, new[] { (1250, 2500, 1, false, 5) }, 4, 5, 1, 5, exe, out string error, effort: NestEffort.Fast,
        candidateCallback: c =>
        {
          if (first == null)
          {
            first = c;
            firstAt = clock.Elapsed.TotalSeconds;
          }
        });
      clock.Stop();

      error.Should().BeNull();
      result.UnplacedParts.Should().BeEmpty();
      result.UsedSheets.Should().HaveCount(1);
      var placed = result.UsedSheets[0].PartPlacements.ToList();
      placed.Should().HaveCount(700);
      PlacementAcceptance.CountUnfit(placed, 1250, 2500, 1, (a, b) => 5 * 0.999).Should().Be(0);

      static double Span(INestResult r) => r.UsedSheets[0].PartPlacements.Max(p => p.MaxY) - r.UsedSheets[0].PartPlacements.Min(p => p.MinY);
      this.output.WriteLine($"first candidate at {firstAt:F1} s, span {(first == null ? 0 : Span(first)):F1}; result span {Span(result):F1} in {clock.Elapsed.TotalSeconds:F1} s");
      clock.Elapsed.TotalSeconds.Should().BeLessThan(10);
      SparrowNestService.IsRowsOnly(result).Should().BeTrue("discs in shifted rows are as tight as the engine gets them");
    }

    /// <summary>
    /// Right triangles take a square each in rows (one step for the pair turned round and the next), half
    /// what the engine gets pairing them on the long side: the engine must still be run (review of 1.1.91).
    /// </summary>
    [Fact]
    public void RightTrianglesStillGoToTheEngine()
    {
      string exe = SparrowExe.Resolve();
      if (exe == null)
      {
        return;
      }

      string dir = Path.Combine(Path.GetTempPath(), "sheetnest_triangles_" + Guid.NewGuid().ToString("N"));
      Directory.CreateDirectory(dir);
      try
      {
        string path = Path.Combine(dir, "triangle.dxf");
        var file = new IxMilia.Dxf.DxfFile();
        file.Header.Version = IxMilia.Dxf.DxfAcadVersion.R2007;
        file.Entities.Add(new IxMilia.Dxf.Entities.DxfLwPolyline(new[]
        {
          new IxMilia.Dxf.Entities.DxfLwPolylineVertex { X = 0, Y = 0 },
          new IxMilia.Dxf.Entities.DxfLwPolylineVertex { X = 100, Y = 0 },
          new IxMilia.Dxf.Entities.DxfLwPolylineVertex { X = 0, Y = 100 },
        })
        { IsClosed = true, Layer = "0" });
        file.Save(path, true);

        var parts = new List<RasterPartInfo> { new RasterPartInfo { Path = path, Quantity = 100, Rotations = 4, Spacing = 5 } };
        var result = SparrowNestService.Nest(parts, new[] { (1250, 2500, 1, false, 5) }, 4, 5, 5, 5, exe, out string error, effort: NestEffort.Draft);

        error.Should().BeNull();
        result.UnplacedParts.Should().BeEmpty();
        SparrowNestService.IsRowsOnly(result).Should().BeFalse("rows of triangles are loose");
      }
      finally
      {
        try
        {
          Directory.Delete(dir, true);
        }
        catch (IOException)
        {
        }
      }
    }

    private static string FindData()
    {
      for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
      {
        string candidate = Path.Combine(d.FullName, "test-data", "circles");
        if (Directory.Exists(candidate))
        {
          return candidate;
        }
      }

      return null;
    }
  }
}
