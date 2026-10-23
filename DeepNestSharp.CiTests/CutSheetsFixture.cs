namespace DeepNestSharp.CiTests
{
  using System.Collections.Generic;
  using System.Linq;
  using DeepNestLib;
  using DeepNestLib.NestProject;
  using DeepNestLib.Placement;
  using FakeItEasy;
  using FluentAssertions;
  using Xunit;

  /// <summary>
  /// Ticking sheets as cut takes their parts off the order and keeps the sheets off the stock, so the next
  /// nest is of what is left.
  /// </summary>
  public class CutSheetsFixture
  {
    [Fact]
    public void ThePartsOnCutSheetsComeOffTheirRows()
    {
      var bracket = new DetailLoadInfo { Path = @"C:\jobs\bracket.dxf", Quantity = 5, Extra = 1, MirrorQuantity = 2 };
      var plate = new DetailLoadInfo { Path = @"C:\jobs\plate.dxf", Quantity = 3 };
      var sheet = Sheet(2500, 1250,
        (bracket.Path, false), (bracket.Path, false), (bracket.Path, true), (plate.Path, false));

      int taken = CutSheets.DeductParts(new IDetailLoadInfo[] { bracket, plate }, new[] { sheet });

      taken.Should().Be(4);
      bracket.Quantity.Should().Be(3, "two plain brackets were cut");
      bracket.MirrorQuantity.Should().Be(1, "one mirrored bracket was cut");
      bracket.Extra.Should().Be(1, "spares go only once the required count is done");
      plate.Quantity.Should().Be(2);
    }

    [Fact]
    public void SparesGoOnceTheRequiredCountIsDone()
    {
      var part = new DetailLoadInfo { Path = @"C:\jobs\a.dxf", Quantity = 1, Extra = 1 };
      CutSheets.DeductParts(new IDetailLoadInfo[] { part }, new[] { Sheet(1000, 500, (part.Path, false), (part.Path, false)) });

      part.Quantity.Should().Be(0);
      part.Extra.Should().Be(0);
    }

    [Fact]
    public void APartListedTwiceIsTakenOffTheFirstRowUntilItIsDone()
    {
      var first = new DetailLoadInfo { Path = @"C:\jobs\a.dxf", Quantity = 1 };
      var second = new DetailLoadInfo { Path = @"C:\jobs\a.dxf", Quantity = 4 };
      CutSheets.DeductParts(new IDetailLoadInfo[] { first, second }, new[] { Sheet(1000, 500, (first.Path, false), (first.Path, false), (first.Path, false)) });

      first.Quantity.Should().Be(0);
      second.Quantity.Should().Be(2);
    }

    [Fact]
    public void APartNoRowAsksForAnyMoreIsLeftAlone()
    {
      var part = new DetailLoadInfo { Path = @"C:\jobs\a.dxf", Quantity = 1 };
      int taken = CutSheets.DeductParts(new IDetailLoadInfo[] { part }, new[] { Sheet(1000, 500, (part.Path, false), (part.Path, false), (@"C:\jobs\gone.dxf", false)) });

      taken.Should().Be(1);
      part.Quantity.Should().Be(0, "never below zero");
    }

    [Fact]
    public void CutSheetsLeaveTheStockForGood()
    {
      var big = new SheetLoadInfo(2500, 1250, 3);
      var small = new SheetLoadInfo(1250, 500, 1);
      var endless = new SheetLoadInfo(2000, 1000, 5) { Unlimited = true };
      CutSheets.TakeCutSheets(
        new ISheetLoadInfo[] { big, small, endless },
        new[] { Sheet(2500, 1250), Sheet(2500, 1250), Sheet(1250, 500), Sheet(1250, 500), Sheet(2000, 1000) });

      big.Quantity.Should().Be(1);
      small.Quantity.Should().Be(0, "never below zero");
      endless.Quantity.Should().Be(5, "an unlimited size has no count to lower");
    }

    private static ISheetPlacement Sheet(double w, double h, params (string Path, bool Mirrored)[] parts)
    {
      var stock = A.Fake<ISheet>();
      A.CallTo(() => stock.WidthCalculated).Returns(w);
      A.CallTo(() => stock.HeightCalculated).Returns(h);
      var placements = parts.Select(p =>
      {
        var part = A.Fake<INfp>();
        A.CallTo(() => part.Name).Returns(p.Path);
        var placement = A.Fake<IPartPlacement>();
        A.CallTo(() => placement.Part).Returns(part);
        A.CallTo(() => placement.IsMirrored).Returns(p.Mirrored);
        return placement;
      }).ToList();
      var sheet = A.Fake<ISheetPlacement>();
      A.CallTo(() => sheet.Sheet).Returns(stock);
      A.CallTo(() => sheet.PartPlacements).Returns(placements);
      return sheet;
    }
  }
}
