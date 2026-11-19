namespace DeepNestSharp.Ui.Views
{
  using System;
  using System.Globalization;
  using System.Linq;
  using System.Windows;
  using System.Windows.Controls;
  using System.Windows.Data;
  using DeepNestSharp.RasterNest;
  using Localization = DeepNestSharp.Ui.Localization;

  /// <summary>The engines' statistics window (Nest ▾ → Engine statistics), from <see cref="EngineStats"/>.</summary>
  public partial class MainWindow
  {
    /// <summary>One engine's line in the statistics window.</summary>
    public sealed class EngineStatsRow
    {
      public string Engine { get; init; } = string.Empty;

      public string Runs { get; init; } = string.Empty;

      public string Best { get; init; } = string.Empty;

      public string Dxf { get; init; } = string.Empty;

      public string Failed { get; init; } = string.Empty;

      public string Pieces { get; init; } = string.Empty;

      public string Kinds { get; init; } = string.Empty;

      public string MaxPieces { get; init; } = string.Empty;

      public string Seconds { get; init; } = string.Empty;

      public string LastRun { get; init; } = string.Empty;
    }

    /// <summary>The engine's name as the table shows it: "Sparrow", or "Sparrow · черновик" for a Draft.</summary>
    internal static string EngineStatsName(string key)
      => key.EndsWith(EngineStats.DraftSuffix, StringComparison.OrdinalIgnoreCase)
        ? key.Substring(0, key.Length - EngineStats.DraftSuffix.Length) + " · " + Localization.Get("Run_Draft")
        : key;

    private void ShowEngineStats()
    {
      // "Best" counts a job once it is done with (list cleared, another job, closing), so the one on screen is not in yet.
      var totals = EngineStats.Load();
      var inv = CultureInfo.InvariantCulture;
      var rows = totals
        .OrderByDescending(t => t.Value.Runs)
        .Select(t =>
        {
          var e = t.Value;
          double n = Math.Max(1, e.Runs);
          return new EngineStatsRow
          {
            Engine = EngineStatsName(t.Key),
            Runs = e.Runs.ToString(inv),
            Best = e.Compared > 0 ? $"{e.Best.ToString(inv)} / {e.Compared.ToString(inv)}" : "—",
            Dxf = e.DxfSaved.ToString(inv),
            Failed = e.Failed.ToString(inv),
            Pieces = e.Runs > 0 ? (e.Pieces / n).ToString("0", inv) : "—",
            Kinds = e.Runs > 0 ? (e.Kinds / n).ToString("0.#", inv) : "—",
            MaxPieces = e.MaxPieces.ToString(inv),
            Seconds = e.Runs > 0 ? (e.Seconds / n).ToString("0.#", inv) : "—",
            LastRun = e.LastRun?.ToString("yyyy-MM-dd HH:mm", inv) ?? string.Empty,
          };
        })
        .ToList();

      var grid = new GridView { ColumnHeaderContainerStyle = this.TryFindResource("CompactColumnHeader") as Style };
      void Column(string key, string path, double width)
        => grid.Columns.Add(new GridViewColumn { Header = Localization.Get(key), Width = width, DisplayMemberBinding = new Binding(path) });
      Column("Col_RunEngine", nameof(EngineStatsRow.Engine), 170);
      Column("Stat_Runs", nameof(EngineStatsRow.Runs), 70);
      Column("Stat_Best", nameof(EngineStatsRow.Best), 80);
      Column("Stat_Dxf", nameof(EngineStatsRow.Dxf), 70);
      Column("Stat_Failed", nameof(EngineStatsRow.Failed), 70);
      Column("Stat_Pieces", nameof(EngineStatsRow.Pieces), 80);
      Column("Stat_Kinds", nameof(EngineStatsRow.Kinds), 70);
      Column("Stat_MaxPieces", nameof(EngineStatsRow.MaxPieces), 80);
      Column("Stat_Seconds", nameof(EngineStatsRow.Seconds), 70);
      Column("Stat_LastRun", nameof(EngineStatsRow.LastRun), 120);

      var list = new ListView { View = grid, ItemsSource = rows, Margin = new Thickness(0, 0, 0, 8), MinHeight = 120 };
      var note = new TextBlock
      {
        Text = rows.Count == 0 ? Localization.Get("Stat_Empty") : Localization.Get("Stat_Note"),
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 0, 0, 8),
        MaxWidth = 860,
        HorizontalAlignment = HorizontalAlignment.Left,
      };

      var open = new Button { Content = Localization.Get("Stat_OpenFolder"), Padding = new Thickness(12, 3, 12, 3), Margin = new Thickness(0, 0, 8, 0) };
      open.Click += (s, a) =>
      {
        try
        {
          System.IO.Directory.CreateDirectory(EngineStats.Folder);
          System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", "\"" + EngineStats.Folder + "\"") { UseShellExecute = true });
        }
        catch (Exception)
        {
          // Nothing to do: the folder is named in the note.
        }
      };
      var close = new Button { Content = Localization.Get("Btn_Close"), Padding = new Thickness(12, 3, 12, 3), IsCancel = true, IsDefault = true };
      var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
      buttons.Children.Add(open);
      buttons.Children.Add(close);

      var panel = new DockPanel { Margin = new Thickness(12) };
      DockPanel.SetDock(buttons, Dock.Bottom);
      DockPanel.SetDock(note, Dock.Bottom);
      panel.Children.Add(buttons);
      panel.Children.Add(note);
      panel.Children.Add(list);

      var window = new Window
      {
        Title = Localization.Get("Title_EngineStats"),
        Owner = this,
        Content = panel,
        Width = 920,
        Height = 380,
        WindowStartupLocation = WindowStartupLocation.CenterOwner,
        ShowInTaskbar = false,
      };
      window.SetResourceReference(Window.BackgroundProperty, "Brush.Window");
      close.Click += (s, a) => window.Close();
      window.ShowDialog();
    }
  }
}
