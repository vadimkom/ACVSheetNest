namespace DeepNestSharp.CiTests.RasterNest
{
  using System;
  using System.Collections.Generic;
  using System.IO;
  using System.Linq;
  using DeepNestLib.Placement;
  using DeepNestSharp.RasterNest;
  using FluentAssertions;
  using Xunit;
  using Xunit.Abstractions;

  /// <summary>
  /// A sheet is packed from the bottom-left corner: left to right, and every part as low as it goes. The shop's
  /// job (`test-data/alexi`, 2026-11-03: a case, two frames of each kind, four lamellas on a 360 × 1890 strip)
  /// came out with parts of a column at the bottom and at the top and nothing between them.
  /// </summary>
  [Collection(SparrowEngineCollection.Name)]
  public class CornerPackFixture
  {
    private const double Spacing = 5;
    private const double Margin = 5;
    private readonly ITestOutputHelper output;

    public CornerPackFixture(ITestOutputHelper output)
    {
      this.output = output;
    }

    [SparrowTheory]
    [InlineData(true)]
    [InlineData(false)]
    public void NoPartHangsOverEmptyMetal(bool nest)
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

      var hanging = new List<string>();
      for (int seed = 0; seed < 6; seed++)
      {
        var result = SparrowNestService.Nest(
          parts,
          new[] { (360, 1890, 1, false, 5) },
          rotations: 4,
          spacing: Spacing,
          margin: Margin,
          perSheetBudgetSec: 5,
          SparrowExe.Resolve(),
          out string error,
          effort: nest ? NestEffort.Fast : NestEffort.Draft,
          seedOffset: seed * 100);

        result.Should().NotBeNull(error);
        var placed = result.UsedSheets.Single().PartPlacements.ToList();
        placed.Should().HaveCount(9);
        this.output.WriteLine($"seed {seed}: up to x {placed.Max(p => p.MaxX):0}, y {placed.Max(p => p.MaxY):0}");
        placed.Max(p => p.MaxY).Should().BeLessThan(1000, "a standing sheet is packed bottom-up: nine parts of under 900 mm go in four columns on 360 mm, the rest of the strip stays whole");
        placed.Min(p => p.MinX).Should().BeApproximately(Margin, 0.5, "the pack starts in the bottom-left corner");
        foreach (var p in placed.OrderBy(p => p.MinX).ThenBy(p => p.MinY))
        {
          double gap = GapBelow(p, placed);
          this.output.WriteLine($"  {Path.GetFileNameWithoutExtension(p.Part.Name)}: x {p.MinX:0.#}..{p.MaxX:0.#}, y {p.MinY:0.#}..{p.MaxY:0.#}{(gap > 1 ? $"  hangs {gap:0}" : string.Empty)}");
          if (gap > 1)
          {
            hanging.Add($"seed {seed} {Path.GetFileNameWithoutExtension(p.Part.Name)} {gap:0} mm");
          }
        }
      }

      hanging.Should().BeEmpty("every part comes down onto the part or the margin below it");
    }

    /// <summary>
    /// How far a part could drop straight down, judged by boxes: the space under it to the box of the nearest
    /// part beneath (less the spacing) or to the margin. Boxes never overlap where the shapes do not, so a gap
    /// found this way is real metal left empty.
    /// </summary>
    private static double GapBelow(IPartPlacement p, List<IPartPlacement> placed)
    {
      double floor = Margin;
      foreach (var q in placed)
      {
        if (!ReferenceEquals(q, p) && q.MaxX > p.MinX - Spacing && q.MinX < p.MaxX + Spacing && q.MaxY <= p.MinY + 1e-6)
        {
          floor = Math.Max(floor, q.MaxY + Spacing);
        }
        else if (!ReferenceEquals(q, p) && q.MaxX > p.MinX - Spacing && q.MinX < p.MaxX + Spacing && q.MinY < p.MinY)
        {
          return 0; // boxes overlap in height: the shapes interlock, the box says nothing
        }
      }

      return p.MinY - floor;
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
