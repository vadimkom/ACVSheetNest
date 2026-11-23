namespace DeepNestSharp.CiTests.RasterNest
{
  using DeepNestLib;
  using DeepNestSharp.RasterNest;
  using DeepNestSharp.Ui.Views;
  using FluentAssertions;
  using Xunit;

  /// <summary>
  /// The variants list's offcut and ranking (2026-11-23, the user: utilisation does not show how tight a layout
  /// is, the time did not count, the columns did not sort; picked "the largest offcut").
  /// </summary>
  public class LargestOffcutFixture
  {
    private static SvgPoint[] Rect(double x0, double y0, double x1, double y1)
      => new[] { new SvgPoint(x0, y0), new SvgPoint(x1, y0), new SvgPoint(x1, y1), new SvgPoint(x0, y1) };

    [Fact]
    public void AnEmptySheetIsAllOffcut()
    {
      var (w, h) = LargestOffcut.Of(new SvgPoint[0][], 1250, 2500, 4);
      w.Should().BeApproximately(1250, 0.01);
      h.Should().BeApproximately(2500, 0.01);
    }

    [Fact]
    public void ABandAcrossTheSheetLeavesTheRestLessTheSpacing()
    {
      var (w, h) = LargestOffcut.Of(new[] { Rect(0, 0, 1250, 1000) }, 1250, 2500, 4);
      w.Should().BeApproximately(1250, 0.01);
      h.Should().BeInRange(1500 - 4 - 10, 1500 - 4, "a cell is 2500 / 512 ≈ 4.9 mm, never more than the real room");
    }

    [Fact]
    public void TheRealOutlineCountsNotItsBox()
    {
      // An L along two sides of a 1000 square: its box is the whole sheet, the metal inside the L is free.
      var l = new[]
      {
        new SvgPoint(0, 0), new SvgPoint(1000, 0), new SvgPoint(1000, 200), new SvgPoint(200, 200),
        new SvgPoint(200, 1000), new SvgPoint(0, 1000),
      };
      var (w, h) = LargestOffcut.Of(new[] { l }, 1000, 1000, 4);
      w.Should().BeInRange(780, 800);
      h.Should().BeInRange(780, 800);
    }

    [Fact]
    public void TheLargerOffcutWinsThenTheQuickerRun()
    {
      var small = new MainWindow.NestRunHistoryItem { Offcut = (1250, 500), Seconds = 3 };
      var large = new MainWindow.NestRunHistoryItem { Offcut = (1250, 900), Seconds = 20 };
      MainWindow.CompareRuns(large, small).Should().BePositive("a bigger piece goes back on the rack");

      var quick = new MainWindow.NestRunHistoryItem { Offcut = (1250, 900), Seconds = 7 };
      MainWindow.CompareRuns(quick, large).Should().BePositive("the same layout found sooner");

      var saved = new MainWindow.NestRunHistoryItem { Offcut = (1250, 900), IsSaved = true };
      MainWindow.CompareRuns(saved, large).Should().BeNegative("a saved nest has no time of its own");
    }
  }
}
