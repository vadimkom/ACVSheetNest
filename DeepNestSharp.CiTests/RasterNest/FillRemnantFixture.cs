namespace DeepNestSharp.CiTests.RasterNest
{
  using System;
  using System.Collections.Generic;
  using System.IO;
  using System.Linq;
  using DeepNestLib.IO;
  using DeepNestSharp.RasterNest;
  using FluentAssertions;
  using Xunit;

  /// <summary>
  /// "What can this offcut make?": the biggest parts first, as many as go on, the smallest not tried, and
  /// the same layout on every piece. Needs the engine (SPARROW_EXE).
  /// </summary>
  public class FillRemnantFixture
  {
    private static string Part(string dir, string name, ShapeKind kind, params double[] values)
      => PartShapes.Write(dir, name, kind, values, millimetres: true);

    [Fact]
    public void AnOffcutTakesTheBigPartsFirstAndLeavesTheTinyOnesOut()
    {
      string exe = SparrowExe.Resolve();
      if (exe == null)
      {
        return;
      }

      string dir = Directory.CreateTempSubdirectory("sheetnest_fill").FullName;
      var parts = new List<RasterPartInfo>
      {
        new RasterPartInfo { Path = Part(dir, "small", PartShapes.Rectangle, 100, 100), Quantity = 1 },
        new RasterPartInfo { Path = Part(dir, "big", PartShapes.Rectangle, 300, 200), Quantity = 1 },
        new RasterPartInfo { Path = Part(dir, "tiny", PartShapes.Circle, 30), Quantity = 1 },
      };

      var result = SparrowNestService.Fill(
        parts, 1250, 250, 2, rotations: 4, spacing: 5, margin: 5, minShare: 0.02, perSheetBudgetSec: 5, exe, out string error, effort: NestEffort.Fast);

      error.Should().BeNull();
      result.Should().NotBeNull();
      result.UnplacedParts.Should().BeEmpty("a fill has no order to fall short of");
      result.UsedSheets.Should().HaveCount(2, "both pieces are filled");

      foreach (var sheet in result.UsedSheets)
      {
        var names = sheet.PartPlacements.Select(p => Path.GetFileNameWithoutExtension(p.Part.Name)).ToList();
        names.Count(n => n == "big").Should().BeGreaterOrEqualTo(3, "four 300 x 200 go along 1240 mm, the engine sets down at least three");
        names.Should().NotContain("tiny", "a part under 2 % of the piece is not tried");
        sheet.PartPlacements.Should().OnlyContain(p => p.PlacedPart.MinX >= 4.99 && p.PlacedPart.MaxX <= 1245.01 && p.PlacedPart.MinY >= 4.99 && p.PlacedPart.MaxY <= 245.01);
      }

      var counts = result.UsedSheets.Select(s => s.PartPlacements.Count).ToList();
      counts.Distinct().Should().ContainSingle("every piece is cut to the same layout");
    }

    [Fact]
    public void AnOffcutTooSmallForAnyPartSaysSo()
    {
      string exe = SparrowExe.Resolve();
      if (exe == null)
      {
        return;
      }

      string dir = Directory.CreateTempSubdirectory("sheetnest_fill").FullName;
      var parts = new List<RasterPartInfo> { new RasterPartInfo { Path = Part(dir, "big", PartShapes.Rectangle, 300, 200), Quantity = 1 } };

      var result = SparrowNestService.Fill(parts, 150, 150, 1, 4, 5, 5, 0.02, 5, exe, out string error, effort: NestEffort.Fast);

      result.Should().BeNull();
      error.Should().Be(SparrowNestService.NothingFits);
    }
  }
}
