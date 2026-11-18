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
  /// The shop's uprights (test-data/stoika, 2026-11-17): two tapered strips, 105.6 × 495 and 95.6 × 400, as a
  /// kit filling a 1250 × 2300 sheet from the right at 5 mm. The engines laid them loosely, 28 kits with up
  /// to seven pairs too close to cut; the operator lays such parts in rows, every other one turned round.
  /// Laid in rows they come out as 30 kits, none too close, in well under a second.
  /// </summary>
  public class RowsLayoutFixture
  {
    private readonly ITestOutputHelper output;

    public RowsLayoutFixture(ITestOutputHelper output) => this.output = output;

    [Fact]
    public void TaperedUprightsFillTheSheetInRows()
    {
      string exe = SparrowExe.Resolve();
      string data = FindData();
      if (exe == null || data == null)
      {
        return;
      }

      var parts = new List<RasterPartInfo>
      {
        new RasterPartInfo { Path = Path.Combine(data, "stoika-1.dxf"), Quantity = 1, Rotations = 4, Spacing = 5, Fill = true },
        new RasterPartInfo { Path = Path.Combine(data, "stoika-37.dxf"), Quantity = 1, Rotations = 4, Spacing = 5, Fill = true },
      };

      var clock = Stopwatch.StartNew();
      var result = SparrowNestService.Nest(parts, new[] { (1250, 2300, 1, false, 5) }, 4, 5, 5, 5, exe, out string error, effort: NestEffort.Fast, fromRight: true);
      clock.Stop();

      error.Should().BeNull();
      result.UsedSheets.Should().HaveCount(1);
      var placed = result.UsedSheets[0].PartPlacements.ToList();
      var bySource = placed.GroupBy(p => p.Source).ToDictionary(g => g.Key, g => g.Count());
      this.output.WriteLine($"{string.Join(", ", bySource.Select(kv => $"{kv.Key}: {kv.Value}"))} in {clock.Elapsed.TotalSeconds:F1} s");

      bySource.Values.Should().OnlyContain(n => n == bySource.Values.First(), "a kit is as many of each");
      bySource.Values.First().Should().BeGreaterOrEqualTo(30, "the engines placed 28 kits");
      PlacementAcceptance.CountUnfit(placed, 1250, 2300, 5, (a, b) => 5).Should().Be(0, "no part is too close to cut");
    }

    [Fact]
    public void CountedUprightsGoOnOneSheetInRows()
    {
      string exe = SparrowExe.Resolve();
      string data = FindData();
      if (exe == null || data == null)
      {
        return;
      }

      var parts = new List<RasterPartInfo>
      {
        new RasterPartInfo { Path = Path.Combine(data, "stoika-1.dxf"), Quantity = 28, Rotations = 4, Spacing = 5 },
        new RasterPartInfo { Path = Path.Combine(data, "stoika-37.dxf"), Quantity = 28, Rotations = 4, Spacing = 5 },
      };

      var result = SparrowNestService.Nest(parts, new[] { (1250, 2300, 1, false, 5) }, 4, 5, 5, 5, exe, out string error, effort: NestEffort.Fast);

      error.Should().BeNull();
      result.UnplacedParts.Should().BeEmpty();
      result.UsedSheets.Should().HaveCount(1);
      var placed = result.UsedSheets[0].PartPlacements.ToList();
      placed.Should().HaveCount(56);
      PlacementAcceptance.CountUnfit(placed, 1250, 2300, 5, (a, b) => 5).Should().Be(0);

      // From the left the rows start at the bottom-left corner and leave the far end of the sheet free.
      placed.Max(p => p.PlacedPart.Points.Max(q => q.Y)).Should().BeLessThan(2150);
    }

    private static string FindData()
    {
      for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
      {
        string candidate = Path.Combine(d.FullName, "test-data", "stoika");
        if (Directory.Exists(candidate))
        {
          return candidate;
        }
      }

      return null;
    }
  }
}
