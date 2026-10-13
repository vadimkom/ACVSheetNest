namespace DeepNestSharp.CiTests
{
  using System;
  using System.IO;
  using System.Threading;
  using System.Threading.Tasks;
  using System.Windows.Threading;
  using DeepNestLib.NestProject;
  using DeepNestSharp.Domain.Models;
  using FluentAssertions;
  using Xunit;

  /// <summary>
  /// The parts list shows each part's size once its drawing has loaded in the background. A project opened
  /// through the Open dialog makes its rows off the UI thread, and the rows then never heard the size was in.
  /// </summary>
  public class PartSizeLabelFixture
  {
    private static string Drawing([System.Runtime.CompilerServices.CallerFilePath] string here = "")
      => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here), "..", "test-data", "dxf", "inhole.dxf"));

    [Fact]
    public void ARowMadeOffTheUiThreadStillHearsItsSize()
    {
      // Made on the thread pool, as opening a project does.
      var row = Task.Run(() => new ObservableDetailLoadInfo(new DetailLoadInfo { Path = Drawing() })).Result;

      string heard = null;
      bool onAsker = false;
      Exception failure = null;
      var ui = new Thread(() =>
      {
        try
        {
          var dispatcher = Dispatcher.CurrentDispatcher;
          SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
          var frame = new DispatcherFrame();
          row.PropertyChanged += (_, e) =>
          {
            if (e.PropertyName == nameof(ObservableDetailLoadInfo.SizeLabel))
            {
              onAsker = dispatcher.CheckAccess();
              heard = row.SizeLabel;
              frame.Continue = false;
            }
          };

          row.SizeLabel.Should().BeEmpty("the drawing has not loaded yet; asking starts it");
          var timer = new DispatcherTimer(TimeSpan.FromSeconds(30), DispatcherPriority.Normal, (_, __) => frame.Continue = false, dispatcher);
          Dispatcher.PushFrame(frame);
          timer.Stop();
        }
        catch (Exception ex)
        {
          failure = ex;
        }
      });
      ui.SetApartmentState(ApartmentState.STA);
      ui.Start();
      ui.Join();

      failure.Should().BeNull();
      heard.Should().NotBeNullOrEmpty("the row is told the size once the drawing is in");
      onAsker.Should().BeTrue("the row hears it on the thread that asked, where the binding lives");
    }
  }
}
