namespace DeepNestSharp.Ui.Views
{
  using DeepNestSharp.Domain.Services;
  using System;

  public class AboutDialogService : IAboutDialogService
  {
    private readonly Func<IAboutDialogService> aboutDialogFactory;

    public AboutDialogService(Func<IAboutDialogService> aboutDialogFactory)
    {
      this.aboutDialogFactory = aboutDialogFactory;
    }

    public bool? ShowDialog()
    {
      var dialog = aboutDialogFactory();
      if (dialog is System.Windows.Window window)
      {
        window.Owner = System.Windows.Application.Current?.MainWindow;
      }

      return dialog.ShowDialog();
    }
  }
}