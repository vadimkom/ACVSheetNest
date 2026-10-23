namespace DeepNestSharp.CiTests
{
  using System.Collections.Generic;
  using DeepNestLib;
  using DeepNestLib.NestProject;
  using DeepNestSharp.Ui.Views;
  using FluentAssertions;
  using Xunit;

  /// <summary>
  /// What a finished nest used of the stock in the Sheets step. Since 1.1.51 a nest only REPORTS what it used
  /// (the "used / in stock" badge) and leaves the quantities as entered: taking them off summed up over several
  /// nests, so a sheet added after one nest came on top of the ones that nest still held and the operator saw
  /// "2/3" with no idea where the third came from. Sheets leave the stock only when cut (CutSheetsFixture).
  /// <para><see cref="MainWindow"/> is a WPF Window and cannot be instantiated here, but these are static.</para>
  /// </summary>
  public class SheetStockAccountingFixture
  {
    [Fact]
    public void ANestReportsWhatItUsedAndLeavesTheQuantity()
    {
      var rows = new List<ISheetLoadInfo> { new SheetLoadInfo(120, 60, 5) };

      var used = MainWindow.ConsumeStock(rows, Used((120, 60, 3)));

      rows[0].Quantity.Should().Be(5, "the stock as entered stays");
      used[(120, 60)].Should().Be(3);
    }

    [Fact]
    public void NestingAgainStartsFromTheSameStock()
    {
      var rows = new List<ISheetLoadInfo> { new SheetLoadInfo(120, 60, 5) };

      MainWindow.ConsumeStock(rows, Used((120, 60, 3)));
      var second = MainWindow.ConsumeStock(rows, Used((120, 60, 4)));

      rows[0].Quantity.Should().Be(5, "nothing sums up from one nest to the next");
      second[(120, 60)].Should().Be(4);
    }

    /// <summary>A counted row reports at most what it has; a second row of the size reports the rest.</summary>
    [Fact]
    public void TwoRowsOfTheSameSizeShareWhatWasUsed()
    {
      var rows = new List<ISheetLoadInfo>
      {
        new SheetLoadInfo(120, 60, 2),
        new SheetLoadInfo(120, 60, 5),
      };

      var used = MainWindow.ConsumeStock(rows, Used((120, 60, 4)));

      used[(120, 60)].Should().Be(4);
      rows[0].Quantity.Should().Be(2);
      rows[1].Quantity.Should().Be(5);
    }

    [Fact]
    public void AnUnlimitedRowReportsWhatItUsed()
    {
      var rows = new List<ISheetLoadInfo> { new SheetLoadInfo(120, 60, 4) { Unlimited = true } };

      var used = MainWindow.ConsumeStock(rows, Used((120, 60, 7)));

      rows[0].Quantity.Should().Be(4);
      used[(120, 60)].Should().Be(7, "the badge has nothing else to count");
    }

    /// <summary>The caller's tally is theirs: reporting must not empty the dictionary it was handed.</summary>
    [Fact]
    public void ReportingLeavesTheCallersTallyAlone()
    {
      var rows = new List<ISheetLoadInfo> { new SheetLoadInfo(120, 60, 5) };
      var used = Used((120, 60, 3));

      MainWindow.ConsumeStock(rows, used);

      used[(120, 60)].Should().Be(3);
    }

    /// <summary>A project saved by 1.1.50 or older has its nest's sheets taken off the quantities; opening it
    /// puts them back once.</summary>
    [Fact]
    public void AnOldProjectGetsItsSheetsBack()
    {
      var rows = new List<ISheetLoadInfo> { new SheetLoadInfo(120, 60, 2) };

      MainWindow.ReturnStock(rows, Used((120, 60, 3)));

      rows[0].Quantity.Should().Be(5);
    }

    [Fact]
    public void AnOldProjectsUnlimitedRowGetsNothing()
    {
      var rows = new List<ISheetLoadInfo> { new SheetLoadInfo(120, 60, 4) { Unlimited = true } };

      MainWindow.ReturnStock(rows, Used((120, 60, 7)));

      rows[0].Quantity.Should().Be(4);
    }

    /// <summary>An old project whose row was deleted after nesting gets the size back.</summary>
    [Fact]
    public void AnOldProjectGetsBackASizeWhoseRowWasDeleted()
    {
      var rows = new List<ISheetLoadInfo>();

      MainWindow.ReturnStock(rows, Used((120, 60, 3)));

      rows.Should().HaveCount(1);
      rows[0].Width.Should().Be(120);
      rows[0].Quantity.Should().Be(3);
    }

    private static Dictionary<(int W, int H), int> Used(params (int W, int H, int Count)[] entries)
    {
      var used = new Dictionary<(int W, int H), int>();
      foreach (var e in entries)
      {
        used[(e.W, e.H)] = e.Count;
      }

      return used;
    }
  }
}
