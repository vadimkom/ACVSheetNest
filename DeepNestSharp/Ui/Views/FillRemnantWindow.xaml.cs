namespace DeepNestSharp.Ui.Views
{
  using System;
  using System.Collections.Generic;
  using System.Linq;
  using System.Windows;
  using System.Windows.Controls;
  using DeepNestLib.IO;
  using Localization = DeepNestSharp.Ui.Localization;

  /// <summary>
  /// What to fill: the size of the offcut (cut square), how many of it, and which groups of the parts
  /// library to fill it from. The parts are tried biggest first, the smallest ones not at all.
  /// </summary>
  public partial class FillRemnantWindow : Window
  {
    // The shop fills the same kind of offcut from the same groups over and over: keep the last answers.
    private static double lastWidth = 250;
    private static double lastLength = 1250;
    private static int lastPieces = 1;
    private static double lastMinSharePercent = 2;
    private static HashSet<string> lastGroups = new HashSet<string>(StringComparer.CurrentCultureIgnoreCase);

    private readonly string folder;

    public FillRemnantWindow(bool millimetres, string folder)
    {
      InitializeComponent();
      this.folder = folder;
      string unit = Localization.Get(millimetres ? "Unit_Mm" : "Unit_In");
      this.widthUnits.Text = unit;
      this.lengthUnits.Text = unit;
      this.widthUpDown.Value = lastWidth;
      this.lengthUpDown.Value = lastLength;
      this.piecesUpDown.Value = lastPieces;
      this.minShareUpDown.Value = lastMinSharePercent;
      this.folderText.Text = folder;
      this.folderText.ToolTip = folder;
      this.ShowGroups();
    }

    public double PieceWidth { get; private set; }

    public double PieceLength { get; private set; }

    public int Pieces { get; private set; }

    /// <summary>Parts under this share of the offcut are not tried (0.02 = 2 %).</summary>
    public double MinShare { get; private set; }

    public IReadOnlyList<LibraryGroup> ChosenGroups { get; private set; } = Array.Empty<LibraryGroup>();

    private void ShowGroups()
    {
      this.groupsPanel.Children.Clear();
      var groups = PartLibrary.Groups(this.folder, Localization.Get("Fill_Loose"));
      foreach (var group in groups)
      {
        this.groupsPanel.Children.Add(new CheckBox
        {
          Content = Localization.Format("Fill_GroupLine", group.Name, group.Items.Count),
          Tag = group,
          IsChecked = lastGroups.Contains(group.Name) || (lastGroups.Count == 0 && groups.Count == 1),
          Margin = new Thickness(0, 2, 0, 2),
        });
      }

      bool empty = groups.Count == 0;
      this.emptyText.Text = empty ? Localization.Format("Fill_Empty", this.folder) : string.Empty;
      this.emptyText.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
      this.fillButton.IsEnabled = !empty;
    }

    private void OnOpenFolder(object sender, RoutedEventArgs e)
    {
      try
      {
        System.IO.Directory.CreateDirectory(this.folder);
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(this.folder) { UseShellExecute = true });
      }
      catch (Exception ex)
      {
        MessageBox.Show(this, ex.GetBaseException().Message, "SheetNest", MessageBoxButton.OK, MessageBoxImage.Warning);
      }
    }

    // Back from the folder with drawings dropped in: show them without reopening the dialog.
    protected override void OnActivated(EventArgs e)
    {
      base.OnActivated(e);
      if (this.IsLoaded)
      {
        var ticked = this.groupsPanel.Children.OfType<CheckBox>().Where(c => c.IsChecked == true).Select(c => ((LibraryGroup)c.Tag).Name).ToList();
        if (ticked.Count > 0)
        {
          lastGroups = new HashSet<string>(ticked, StringComparer.CurrentCultureIgnoreCase);
        }

        this.ShowGroups();
      }
    }

    private void OnOk(object sender, RoutedEventArgs e)
    {
      this.widthUpDown.CommitInput();
      this.lengthUpDown.CommitInput();
      this.piecesUpDown.CommitInput();
      this.minShareUpDown.CommitInput();
      var chosen = this.groupsPanel.Children.OfType<CheckBox>().Where(c => c.IsChecked == true).Select(c => (LibraryGroup)c.Tag).ToList();
      if (chosen.Count == 0)
      {
        this.emptyText.Text = Localization.Get("Fill_PickGroup");
        this.emptyText.Visibility = Visibility.Visible;
        return;
      }

      this.PieceWidth = this.widthUpDown.Value ?? 0;
      this.PieceLength = this.lengthUpDown.Value ?? 0;
      this.Pieces = this.piecesUpDown.Value ?? 1;
      this.MinShare = (this.minShareUpDown.Value ?? 0) / 100;
      if (!(this.PieceWidth > 0) || !(this.PieceLength > 0))
      {
        return;
      }

      this.ChosenGroups = chosen;
      lastWidth = this.PieceWidth;
      lastLength = this.PieceLength;
      lastPieces = this.Pieces;
      lastMinSharePercent = this.minShareUpDown.Value ?? 0;
      lastGroups = new HashSet<string>(chosen.Select(g => g.Name), StringComparer.CurrentCultureIgnoreCase);
      this.DialogResult = true;
    }
  }
}
