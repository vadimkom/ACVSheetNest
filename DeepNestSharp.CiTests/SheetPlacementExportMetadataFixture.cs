namespace DeepNestSharp.CiTests
{
  using System.Collections.Generic;
  using DeepNestLib;
  using DeepNestLib.Placement;
  using DeepNestSharp.Domain.ViewModels;
  using FluentAssertions;
  using Xunit;

  public class SheetPlacementExportMetadataFixture
  {
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, true, true)]
    public void ExportMergesWhenEitherThePreferenceOrLayoutRequiresIt(
      bool configured,
      bool requiredByLayout,
      bool expected)
    {
      var placement = Placement(requiredByLayout);

      MainViewModel.MergeLinesForExport(configured, placement).Should().Be(expected);
    }

    [Theory]
    [InlineData("test", 1, 1250, 2500, 3, ".dxf", "test_1_1250x2500_x3.dxf")]
    [InlineData("test", 2, 1250, 2500, 1, ".dxf", "test_2_1250x2500_x1.dxf")]
    [InlineData("Заказ 17", 3, 1000.5, 2000, 12, ".dxf", "Заказ 17_3_1000.5x2000_x12.dxf")]
    [InlineData("  ", 1, 48, 96, 0, ".dxf", "layout_1_48x96_x1.dxf")]
    public void EachLayoutFileNamesItsNumberSheetSizeAndRepeatCount(
      string baseName, int index, double width, double height, int count, string extension, string expected)
    {
      MainViewModel.LayoutFileName(baseName, index, width, height, count, extension).Should().Be(expected);
    }

    private static SheetPlacement Placement(bool requiresMergedLines)
      => new SheetPlacement(
        PlacementTypeEnum.Gravity,
        Sheet.NewSheet(1, 100, 50),
        new List<IPartPlacement>(),
        0,
        requiresMergedLines);
  }
}
