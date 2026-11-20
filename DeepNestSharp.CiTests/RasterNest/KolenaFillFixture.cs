namespace DeepNestSharp.CiTests.RasterNest
{
  using System;
  using System.Collections.Generic;
  using System.IO;
  using System.Linq;
  using DeepNestSharp.RasterNest;
  using FluentAssertions;
  using Xunit;

  /// <summary>
  /// The shop's elbow sides (test-data/kolena, 2026-11-12): a quarter ring R306 / R94, filling a 320 × 2000
  /// strip at 5 mm, turned in 10° steps. Rows of one orientation seat 6; the operator put 7 on by hand,
  /// each part tucked into the hollow of the next ("я руками вместил 7 штук"). A single filler was laid in
  /// rows only and never reached the engine, so every engine came out with the same 6 (1.1.82 on the shop
  /// PC). Needs the engine (SPARROW_EXE).
  /// </summary>
  public class KolenaFillFixture
  {
    [SparrowTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void SevenElbowSidesFillTheStrip(bool draft)
    {
      var parts = new List<RasterPartInfo> { new RasterPartInfo { Path = Drawing(), Quantity = 1, Fill = true, Rotations = 36 } };

      var result = SparrowNestService.Nest(
        parts,
        new[] { (320, 2000, 1, false, 5) },
        rotations: 36,
        spacing: 5,
        margin: 5,
        perSheetBudgetSec: 5,
        SparrowExe.Resolve(),
        out string error,
        effort: draft ? NestEffort.Draft : NestEffort.Fast);

      error.Should().BeNull();
      result.UsedSheets.Should().HaveCount(1);
      var sheet = result.UsedSheets[0];
      sheet.PartPlacements.Count.Should().BeGreaterOrEqualTo(7);
      PlacementAcceptance.CountUnfit(sheet.PartPlacements.ToList(), sheet.Sheet.WidthCalculated, sheet.Sheet.HeightCalculated, 5, (a, b) => 5 * 0.999)
        .Should().Be(0, "every part on the metal, none closer than the spacing");
    }

    private static string Drawing()
    {
      for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
      {
        string candidate = Path.Combine(d.FullName, "test-data", "kolena", "side-E90-200-R100.dxf");
        if (File.Exists(candidate))
        {
          return candidate;
        }
      }

      throw new FileNotFoundException("test-data/kolena/side-E90-200-R100.dxf");
    }
  }
}
