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
  /// What the stock ran short of goes at the end of the strip of layouts, as red virtual sheets: browsed like
  /// the rest, but not in the cutting plan, the export or the check before cutting.
  /// </summary>
  public class VirtualSheetsInTheStripFixture
  {
    [Fact]
    public void VirtualSheetsAreShownLastAndKeptOutOfThePlan()
    {
      RunOnSta(() =>
      {
        var cut = Nest(2500, 1250, unplaced: 1, new[] { Placed(Rect(600, 400), 5, 5) });
        var shortfall = Nest(1250, 2500, unplaced: 0, new[] { Placed(Rect(600, 400), 5, 5) }, new[] { Placed(Rect(900, 900), 5, 5), Placed(Rect(900, 900), 5, 5) });
        var viewer = new DxfViewer { DefaultPartSpacing = 5, SheetEdgeMargin = 5, UnitsMm = true };
        viewer.VirtualSheetsFor = r => ReferenceEquals(r, cut) ? shortfall : null;
        viewer.Result = cut;

        var layouts = viewer.GetLayouts();
        layouts.Should().HaveCount(3);
        layouts[0].IsVirtual.Should().BeFalse();
        layouts.Skip(1).Should().OnlyContain(l => l.IsVirtual, "the sheets to buy come after every sheet that is cut");
        viewer.GetProductionPlan().Should().ContainSingle("the plan, report and export hold only the sheets in stock");
        viewer.GetDistinctLayoutSheets().Should().ContainSingle();
        viewer.FindUnfitLayouts().Should().BeEmpty("two parts on top of each other on a virtual sheet stop nothing being cut");
      });
    }

    private static int nextSource;

    private static NestResult Nest(int w, int h, int unplaced, params IPartPlacement[][] sheets)
    {
      var collection = new SheetPlacementCollection();
      for (int i = 0; i < sheets.Length; i++)
      {
        collection.Add(new SheetPlacement(PlacementTypeEnum.BoundingBox, Sheet.NewSheet(i + 1, w, h), sheets[i].ToList(), 0, 10000000));
      }

      var left = Enumerable.Range(0, unplaced).Select(_ => (INfp)Rect(10, 10)).ToList();
      return new NestResult(sheets.Sum(s => s.Length) + unplaced, collection, left, PlacementTypeEnum.BoundingBox, 0, 0);
    }

    private static NoFitPolygon Rect(double w, double h)
    {
      var r = new NoFitPolygon(new List<SvgPoint> { new SvgPoint(0, 0), new SvgPoint(w, 0), new SvgPoint(w, h), new SvgPoint(0, h) });
      r.Source = Interlocked.Increment(ref nextSource);
      r.Name = $"part{r.Source}.dxf";
      return r;
    }

    private static PartPlacement Placed(NoFitPolygon part, double x, double y) => new PartPlacement(part) { X = x, Y = y, Source = part.Source, Id = part.Source };

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
