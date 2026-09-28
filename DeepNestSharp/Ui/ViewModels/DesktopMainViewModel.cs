namespace DeepNestSharp.Ui.ViewModels
{
  using System.Windows;
  using DeepNestLib;
  using DeepNestSharp.Domain.Services;
  using DeepNestSharp.Domain.ViewModels;

  /// <summary>The main view model with the one thing the domain cannot do itself: close the window.</summary>
  public class DesktopMainViewModel : MainViewModel
  {
    public DesktopMainViewModel(IMessageService messageService, IDispatcherService dispatcherService, ISvgNestConfig config, IFileIoService fileIoService)
      : base(messageService, dispatcherService, config, fileIoService)
    {
    }

    protected override void OnExit()
    {
      Application.Current.MainWindow.Close();
    }
  }
}
