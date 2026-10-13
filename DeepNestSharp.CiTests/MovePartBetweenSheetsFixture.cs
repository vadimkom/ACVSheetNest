namespace DeepNestSharp.CiTests
{
  using System;
  using System.Collections.Generic;
  using System.Linq;
  using System.Threading;
  using DeepNestLib;
  using DeepNestLib.Placement;
  using DeepNestSharp.Ui.UserControls;
  using FluentAssertions;
  using Xunit;

  /// <summary>
  /// A part can be moved by hand to another sheet. The operator's order came out with a side alone on its
  /// third sheet while the first had a side's worth of room, and dragging could not leave the sheet.
  /// </summary>
  public class MovePartBetweenSheetsFixture
  {
    [Fact]
    public void APartMovesIntoTheRoomOnAnotherSheetAndTheSheetItLeftEmptyGoes()
    {
      RunOnSta(() =>
      {
        // The operator's sizes: a 1354 x 1210 front alone on sheet 1, a top and a side on sheet 2, the
        // other side alone on sheet 3, all 2500 x 1250.
        var front = Placed(Rect(1354, 1210), 5, 5);
        var top = Placed(Rect(1354, 688), 5, 5);
        var side = Placed(Rect(649, 1209), 1370, 5);
        var lonely = Placed(Rect(649, 1209), 5, 5);
        var result = Nest(new[] { front }, new[] { top, side }, new[] { lonely });

        var viewer = new DxfViewer { DefaultPartSpacing = 5, SheetEdgeMargin = 5, UnitsMm = true };
        var removed = new List<bool>();
        viewer.SheetRemoved += (_, gone) => removed.Add(gone);
        viewer.Result = result;
        viewer.LayoutCount.Should().Be(3);

        viewer.SheetIndex = 2;
        viewer.SelectForEdit(lonely);
        viewer.MoveSelectedTo(0);

        viewer.LayoutCount.Should().Be(2, "the third sheet had nothing left on it");
        result.UsedSheets.Should().HaveCount(2);
        removed.Should().Equal(true);
        var first = result.UsedSheets[0].PartPlacements;
        first.Should().HaveCount(2);
        var moved = first[1].PlacedPart;
        moved.MinX.Should().BeGreaterThanOrEqualTo(front.PlacedPart.MaxX + 5 - 1e-6, "it goes into the room beside the front, clear of it");
        moved.MaxX.Should().BeLessThanOrEqualTo(2500 - 5 + 1e-6);

        viewer.UndoLastEdit();

        viewer.LayoutCount.Should().Be(3);
        result.UsedSheets.Should().HaveCount(3);
        result.UsedSheets[0].PartPlacements.Should().HaveCount(1);
        result.UsedSheets[2].PartPlacements.Should().HaveCount(1);
        removed.Should().Equal(true, false);
      });
    }

    [Fact]
    public void APartWithNoRoomOnTheOtherSheetKeepsItsPlaceForTheOperatorToDrag()
    {
      RunOnSta(() =>
      {
        var full = Placed(Rect(2400, 1200), 5, 5);
        var other = Placed(Rect(649, 1209), 5, 5);
        var keep = Placed(Rect(300, 300), 1000, 5);
        var result = Nest(new[] { full }, new[] { other, keep });

        var viewer = new DxfViewer { DefaultPartSpacing = 5, SheetEdgeMargin = 5, UnitsMm = true };
        viewer.Result = result;
        viewer.SheetIndex = 1;
        viewer.SelectForEdit(other);
        viewer.MoveSelectedTo(0);

        result.UsedSheets.Should().HaveCount(2, "the sheet it left still holds a part");
        result.UsedSheets[0].PartPlacements.Should().HaveCount(2);
        result.UsedSheets[0].PartPlacements[1].X.Should().Be(5, "with nowhere free it stays where it was, shown red");
      });
    }

    private static NestResult Nest(params IPartPlacement[][] sheets)
    {
      var collection = new SheetPlacementCollection();
      for (int i = 0; i < sheets.Length; i++)
      {
        collection.Add(new SheetPlacement(
          PlacementTypeEnum.BoundingBox,
          Sheet.NewSheet(i + 1, 2500, 1250),
          sheets[i].ToList(),
          0,
          10000000));
      }

      return new NestResult(sheets.Sum(s => s.Length), collection, new List<INfp>(), PlacementTypeEnum.BoundingBox, 0, 0);
    }

    private static PartPlacement Placed(NoFitPolygon part, double x, double y) => new PartPlacement(part) { X = x, Y = y, Source = part.Source, Id = part.Source };

    private static int nextSource;

    private static NoFitPolygon Rect(double w, double h)
    {
      var r = new NoFitPolygon(new List<SvgPoint>
      {
        new SvgPoint(0, 0),
        new SvgPoint(w, 0),
        new SvgPoint(w, h),
        new SvgPoint(0, h),
      });
      r.Source = Interlocked.Increment(ref nextSource);
      r.Name = $"part{r.Source}.dxf";
      return r;
    }

    private static void RunOnSta(Action body)
    {
      Exception failure = null;
      var thread = new Thread(() =>
      {
        try
        {
          body();
        }
        catch (Exception ex)
        {
          failure = ex;
        }
      });
      thread.SetApartmentState(ApartmentState.STA);
      thread.Start();
      thread.Join();
      if (failure != null)
      {
        System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
      }
    }
  }
}
