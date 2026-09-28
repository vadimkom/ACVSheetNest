namespace DeepNestSharp.Ui.Views
{
  using System.Diagnostics;
  using System.Reflection;
  using System.Windows;
  using System.Windows.Navigation;
  using DeepNestSharp.Domain.Services;
  using Localization = DeepNestSharp.Ui.Localization;

  /// <summary>
  /// Interaction logic for AboutDialog.xaml
  /// </summary>
  public partial class AboutDialog : Window, IAboutDialogService
  {
    public AboutDialog()
    {
      InitializeComponent();
      Title = Localization.Format("About_Title", InformationalVersion);
      this.versionText.Text = Localization.Format("About_Version", InformationalVersion);
    }

    /// <summary>The version as released ("1.1.13"), without the "+commit" the build appends.</summary>
    public string InformationalVersion => (this.GetType()
                                              .Assembly
                                              .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
                                              .InformationalVersion ?? string.Empty).Split('+')[0];

    private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
      // .NET no longer opens a URL through the shell unless asked to; without this every link threw.
      Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
      e.Handled = true;
    }

    private void OkClick(object sender, RoutedEventArgs e)
    {
      DialogResult = true;
    }
  }
}
