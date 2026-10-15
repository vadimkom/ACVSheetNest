namespace DeepNestSharp.CiTests.RasterNest
{
  using System.Collections.Generic;
  using System.IO;
  using System.Linq;
  using DeepNestLib.IO;
  using DeepNestLib.Placement;
  using DeepNestSharp.RasterNest;
  using FluentAssertions;
  using Xunit;

  /// <summary>
  /// A part with no count ("fill the sheets", a sheet of washers): the counted parts go on first, the filler
  /// takes the room left, and what did not fit is not reported as missing. Needs the engine (SPARROW_EXE).
  /// </summary>
  public class FillSheetsFixture
  {
    private static string Part(string dir, string name, ShapeKind kind, params double[] values)
      => PartShapes.Write(dir, name, kind, values, millimetres: true);

    private static INestResult Nest(List<RasterPartInfo> parts, (int W, int H, int Qty, bool Unlimited, int Priority) sheet, out string error)
      => SparrowNestService.Nest(
        parts,
        new[] { sheet },
        rotations: 4,
        spacing: 5,
        margin: 5,
        perSheetBudgetSec: 5,
        SparrowExe.Resolve(),
        out error,
        effort: NestEffort.Fast);

    [Fact]
    public void WashersFillTheSheetAndNoneAreMissing()
    {
      if (SparrowExe.Resolve() == null)
      {
        return;
      }

      string dir = Directory.CreateTempSubdirectory("sheetnest_fillsheets").FullName;
      var parts = new List<RasterPartInfo> { new RasterPartInfo { Path = Part(dir, "m12", PartShapes.Washer, 24, 13), Quantity = 1, Fill = true } };

      var result = Nest(parts, (500, 250, 1, false, 5), out string error);

      error.Should().BeNull();
      result.UsedSheets.Should().HaveCount(1);
      result.UnplacedParts.Should().BeEmpty("what did not fit was never ordered");

      // 29 mm apart on a square grid is 16 x 8 = 128 in 490 x 240; a staggered layout gets more.
      result.UsedSheets[0].PartPlacements.Count.Should().BeGreaterOrEqualTo(128);
    }

    [Fact]
    public void CountedPartsGoOnFirstAndWashersTakeTheRest()
    {
      if (SparrowExe.Resolve() == null)
      {
        return;
      }

      string dir = Directory.CreateTempSubdirectory("sheetnest_fillsheets").FullName;
      var parts = new List<RasterPartInfo>
      {
        new RasterPartInfo { Path = Part(dir, "m12", PartShapes.Washer, 24, 13), Quantity = 1, Fill = true },
        new RasterPartInfo { Path = Part(dir, "plate", PartShapes.Rectangle, 300, 200), Quantity = 1 },
      };

      // Unlimited stock would never run out of room for washers: in a filling job it is the one sheet.
      var result = Nest(parts, (500, 250, 0, true, 5), out string error);

      error.Should().BeNull();
      result.UsedSheets.Should().HaveCount(1);
      result.UnplacedParts.Should().BeEmpty();
      var names = result.UsedSheets[0].PartPlacements.Select(p => Path.GetFileNameWithoutExtension(p.Part.Name)).ToList();
      names.Should().ContainSingle(n => n == "plate");
      names.Count(n => n == "m12").Should().BeGreaterThan(30, "the 190 x 240 strip beside the plate alone takes 6 x 8");
    }

    [Fact]
    public void ACountedPartThatDoesNotFitIsStillMissing()
    {
      if (SparrowExe.Resolve() == null)
      {
        return;
      }

      string dir = Directory.CreateTempSubdirectory("sheetnest_fillsheets").FullName;
      var parts = new List<RasterPartInfo>
      {
        new RasterPartInfo { Path = Part(dir, "m12", PartShapes.Washer, 24, 13), Quantity = 1, Fill = true },
        new RasterPartInfo { Path = Part(dir, "plate", PartShapes.Rectangle, 300, 200), Quantity = 2 },
      };

      var result = Nest(parts, (500, 250, 1, false, 5), out _);

      result.UnplacedParts.Should().HaveCount(1, "the second plate was ordered and there is no sheet for it");
      result.UsedSheets[0].PartPlacements.Should().Contain(p => Path.GetFileNameWithoutExtension(p.Part.Name) == "plate");
    }
  }
}
