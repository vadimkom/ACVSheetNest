namespace DeepNestSharp
{
  using System;
  using System.IO;
  using System.Linq;
  using System.Windows;
  using DeepNestLib;
  using DeepNestSharp.Domain.Services;
  using DeepNestSharp.Domain.ViewModels;
  using DeepNestSharp.Ui.Services;
  using DeepNestSharp.Ui.ViewModels;
  using DeepNestSharp.Ui.Views;
  using Microsoft.Extensions.DependencyInjection;
  using Microsoft.Extensions.Hosting;

  /// <summary>
  /// Interaction logic for App.xaml.
  /// </summary>
  public partial class App : Application
  {
    private readonly IHost host;

    public App()
    {
      // The generated Main calls InitializeComponent only AFTER this constructor, and this constructor opens
      // the main window. Until then the theme (App.xaml) is not loaded, so a window reaching a theme style
      // with StaticResource could fail to find it ("PrimaryButton" on the About dialog). Loaded here, first;
      // the generated call afterwards sees it done and returns.
      this.InitializeComponent();

      // The domain's messages come from the same dictionaries as the window's.
      UiText.Resolve = DeepNestSharp.Ui.Localization.Get;

      // Consent-based crash reporting: log locally, offer a pre-filled GitHub issue, never send
      // anything by itself. The nest path has its own non-fatal catch in MainWindow.OnNest; these
      // are the last-resort nets for everything else.
      this.DispatcherUnhandledException += (s, e) =>
      {
        e.Handled = true; // keep WPF from tearing down before the dialog shows
        CrashReporter.Show(e.Exception, "ui", Current?.MainWindow);
        Environment.Exit(1); // state is unknown — exit without running half-broken save prompts
      };
      AppDomain.CurrentDomain.UnhandledException += (s, e) =>
      {
        var ex = e.ExceptionObject as Exception;
        CrashReporter.Save(ex, "background");
        try
        {
          this.Dispatcher.Invoke(() => CrashReporter.Show(ex, "background", Current?.MainWindow));
        }
        catch
        {
          // The process is dying; the log on disk is what matters.
        }
      };
      System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (s, e) =>
      {
        CrashReporter.Save(e.Exception, "task");
        e.SetObserved();
      };

      host = new HostBuilder()
           .ConfigureServices((hostContext, services) =>
           {
             services.AddScoped<IFileIoService, FileIoService>();
             services.AddScoped<IMessageService, MessageBoxService>();
             services.AddSingleton(SvgNest.Config);
             services.AddSingleton<IDispatcherService>(new DispatcherService(this.Dispatcher));
             services.AddSingleton<IMainViewModel, DesktopMainViewModel>();
             services.AddSingleton<MainWindow>();
           }).Build();

      using (var serviceScope = host.Services.CreateScope())
      {
        var services = serviceScope.ServiceProvider;
        try
        {
          var mainWindow = services.GetRequiredService<MainWindow>();
          mainWindow?.Show();

          // Files named on the command line (a double-clicked .dnest, DXFs dropped on the icon) take the
          // same path as files dropped on the window. [0] is the program itself.
          var files = Environment.GetCommandLineArgs()
            .Skip(1)
            .Where(arg => !arg.StartsWith("--", StringComparison.Ordinal) && File.Exists(arg))
            .ToArray();
          var unsupported = files
            .Where(f => !f.EndsWith(".dnest", StringComparison.OrdinalIgnoreCase) && !DeepNestLib.IO.DxfFileExtensions.IsDxf(f))
            .ToArray();
          if (unsupported.Length > 0)
          {
            MessageBox.Show(
              DeepNestSharp.Ui.Localization.Format("Msg_UnsupportedFiles", string.Join(", ", unsupported.Select(Path.GetFileName))),
              "SheetNest",
              MessageBoxButton.OK,
              MessageBoxImage.Information);
          }

          if (files.Length > unsupported.Length)
          {
            // Not now: this constructor runs before the dispatcher does, so there is no UI context yet and
            // every await in the add would resume on a pool thread and touch the parts list from there.
            // Queued at idle it runs on the UI thread once the window has loaded, restored the session and
            // made the project the parts belong in.
            var supported = files.Except(unsupported).ToArray();
            mainWindow.Dispatcher.BeginInvoke(
              System.Windows.Threading.DispatcherPriority.ApplicationIdle,
              new Action(() => mainWindow.OpenFiles(supported)));
          }
        }
        catch (Exception ex)
        {
          Console.WriteLine(ex.Message);
          throw;
        }
      }
    }
  }
}
