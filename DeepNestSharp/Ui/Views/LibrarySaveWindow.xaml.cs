namespace DeepNestSharp.Ui.Views
{
  using System.Collections.Generic;
  using System.Windows;

  /// <summary>Which group of the parts library a shape goes into, and under what name.</summary>
  public partial class LibrarySaveWindow : Window
  {
    public LibrarySaveWindow(IEnumerable<string> groups, string group, string name, string note)
    {
      InitializeComponent();
      foreach (string g in groups)
      {
        this.groupCombo.Items.Add(g);
      }

      this.groupCombo.Text = group ?? string.Empty;
      this.nameText.Text = name ?? string.Empty;
      this.noteText.Text = note ?? string.Empty;
      this.noteText.Visibility = string.IsNullOrEmpty(note) ? Visibility.Collapsed : Visibility.Visible;
      this.Loaded += (_, __) =>
      {
        this.nameText.Focus();
        this.nameText.SelectAll();
      };
    }

    public string GroupName => this.groupCombo.Text?.Trim() ?? string.Empty;

    public string ItemName => this.nameText.Text?.Trim() ?? string.Empty;

    private void OnOk(object sender, RoutedEventArgs e)
    {
      if (this.GroupName.Length == 0)
      {
        this.groupCombo.Focus();
        return;
      }

      if (this.ItemName.Length == 0)
      {
        this.nameText.Focus();
        return;
      }

      this.DialogResult = true;
    }
  }
}
