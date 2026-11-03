namespace DeepNestSharp.CiTests.RasterNest
{
  using System;
  using System.Collections.Generic;
  using System.IO;
  using System.Linq;
  using DeepNestSharp.RasterNest;
  using FluentAssertions;
  using Xunit;
  using Xunit.Abstractions;

  /// <summary>
  /// A sheet with room to spare is packed from the bottom-left corner up, the free metal left in one piece at
  /// the far end. The shop's job (`test-data/alexi`, 2026-11-03: a case, two frames of each kind, four lamellas
  /// on a 360 × 1890 strip) came out spread over the whole length with gaps between the parts.
  /// </summary>
  [Collection(SparrowEngineCollection.Name)]
  public class CornerPackFixture
  {
    private readonly ITestOutputHelper output;

    public CornerPackFixture(ITestOutputHelper output)
    {
      this.output = output;
    }

    [SparrowTheory]
    [InlineData(true)]
    [InlineData(false)]
    public void PartsGatherAtTheStartOfASheetWithRoomToSpare(bool nest)
    {
      string data = FindData();
      data.Should().NotBeNull("test-data/alexi holds the job's drawings");
      string Part(string name) => Path.Combine(data, $"{name}^njr_alexi.DXF");
      var parts = new List<RasterPartInfo>
      {
        new RasterPartInfo { Path = Part("case"), Quantity = 1, Priority = 5 },
        new RasterPartInfo { Path = Part("ramkaH"), Quantity = 2, Priority = 5 },
        new RasterPartInfo { Path = Part("ramkaV"), Quantity = 2, Priority = 5 },
        new RasterPartInfo { Path = Part("lamel_top"), Quantity = 1, Priority = 5 },
        new RasterPartInfo { Path = Part("lamel"), Quantity = 3, Priority = 5 },
      };

      var result = SparrowNestService.Nest(
        parts,
        new[] { (360, 1890, 1, false, 5) },
        rotations: 4,
        spacing: 5,
        margin: 5,
        perSheetBudgetSec: 5,
        SparrowExe.Resolve(),
        out string error,
        effort: nest ? NestEffort.Fast : NestEffort.Draft);

      result.Should().NotBeNull(error);
      var sheet = result.UsedSheets.Single();
      var placed = sheet.PartPlacements.ToList();
      placed.Should().HaveCount(9);
      foreach (var p in placed.OrderBy(p => p.MinY))
      {
        this.output.WriteLine($"{Path.GetFileNameWithoutExtension(p.Part.Name)}: x {p.MinX:0.#}..{p.MaxX:0.#}, y {p.MinY:0.#}..{p.MaxY:0.#}");
      }

      double top = placed.Max(p => p.MaxY);
      this.output.WriteLine($"top {top:0.#}");
      top.Should().BeLessThan(1000, "nine parts of under 900 mm fit in four columns on 360 mm: the rest of the strip stays whole");
      placed.Min(p => p.MinX).Should().BeApproximately(5, 0.5, "the pack starts in the bottom-left corner, at the margin");
      placed.Min(p => p.MinY).Should().BeApproximately(5, 0.5, "the pack starts in the bottom-left corner, at the margin");
      placed.Max(p => p.MaxX).Should().BeLessThanOrEqualTo(355.01, "the parts stay inside the margin");
      placed.Select(p => p.Rotation).Should().OnlyContain(r => Math.Abs(r % 90) < 1e-6, "a part keeps one of its four allowed angles");
    }

    private static string FindData()
    {
      for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
      {
        string candidate = Path.Combine(d.FullName, "test-data", "alexi");
        if (Directory.Exists(candidate))
        {
          return candidate;
        }
      }

      return null;
    }
  }
}
