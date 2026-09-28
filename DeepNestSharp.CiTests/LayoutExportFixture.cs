namespace DeepNestSharp.CiTests
{
  using System;
  using System.Collections.Generic;
  using System.IO;
  using System.Linq;
  using System.Threading.Tasks;
  using DeepNestLib;
  using DeepNestLib.IO;
  using DeepNestLib.Placement;
  using DeepNestSharp.Domain.Services;
  using DeepNestSharp.Ui.ViewModels;
  using FakeItEasy;
  using FluentAssertions;
  using Xunit;

  /// <summary>
  /// Export asks for ONE name and writes every distinct layout beside it as
  /// <c>name_N_WxH_xCount.dxf</c>: the layout's number in the report, its sheet and how many to cut.
  /// </summary>
  public class LayoutExportFixture
  {
    private static readonly string Part = SampleDrawing();

    [Fact]
    public async Task EveryLayoutIsWrittenUnderTheOneNameWithItsNumberSizeAndCount()
    {
      File.Exists(Part).Should().BeTrue("the sample drawing is in the repository: {0}", Part);

      string dir = Path.Combine(Path.GetTempPath(), "sheetnest-export-" + Guid.NewGuid().ToString("N"));
      Directory.CreateDirectory(dir);
      try
      {
        var fileIo = A.Fake<IFileIoService>();
        A.CallTo(() => fileIo.GetSaveFilePath(A<string>._, A<string>._, A<string>._)).Returns(Path.Combine(dir, "test.dxf"));
        var messages = A.Fake<IMessageService>();
        A.CallTo(() => messages.DisplayOkCancel(A<string>._, A<string>._, A<MessageBoxIcon>._)).Returns(MessageBoxResult.OK);
        var vm = new DesktopMainViewModel(messages, A.Fake<IDispatcherService>(), new SvgNestConfig(), fileIo);

        var layouts = new List<(ISheetPlacement Sheet, int Count, IReadOnlyCollection<OffcutLine> Offcut)>
        {
          (Layout(1250, 2500), 3, null),
          (Layout(1250, 2500), 1, null),
        };

        await vm.ExportLayoutsAsync(layouts);

        Directory.GetFiles(dir).Select(Path.GetFileName).Should().BeEquivalentTo(
          "test_1_1250x2500_x3.dxf",
          "test_2_1250x2500_x1.dxf");
        A.CallTo(() => messages.DisplayMessageBox(A<string>._, A<string>._, A<MessageBoxIcon>._)).MustNotHaveHappened();
        A.CallTo(() => messages.DisplayOkCancel(A<string>._, A<string>._, A<MessageBoxIcon>._)).MustNotHaveHappened();

        // Exporting again over the same files asks once, for all of them.
        await vm.ExportLayoutsAsync(layouts);
        A.CallTo(() => messages.DisplayOkCancel(A<string>._, A<string>._, A<MessageBoxIcon>._)).MustHaveHappenedOnceExactly();
      }
      finally
      {
        Directory.Delete(dir, true);
      }
    }

    /// <summary>test-data sits beside this source file's folder, wherever the binaries were built to.</summary>
    private static string SampleDrawing([System.Runtime.CompilerServices.CallerFilePath] string here = "")
      => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here), "..", "test-data", "dxf", "test2.dxf"));

    private static ISheetPlacement Layout(int width, int height)
    {
      var raw = new NestExecutionHelper().LoadRawDetail(new FileInfo(Part));
      raw.TryConvertToNfp(0, out INfp nfp).Should().BeTrue();
      nfp.Name = Part;
      var placements = new List<IPartPlacement> { new PartPlacement(nfp) { X = 10, Y = 10 } };
      return new SheetPlacement(PlacementTypeEnum.Gravity, Sheet.NewSheet(1, width, height), placements, 0, false);
    }
  }
}
