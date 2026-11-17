namespace DeepNestSharp.Ui.Views
{
  using System;
  using System.Text.Json.Nodes;
  using System.Windows;
  using DeepNestLib;
  using DeepNestLib.Placement;
  using DeepNestSharp.Domain.ViewModels;
  using Localization = DeepNestSharp.Ui.Localization;

  /// <summary>The last project kept in _last.dnest and opened again from the File menu (2026-11-17).</summary>
  public partial class MainWindow
  {
    /// <summary>Keeps the job with the nest on screen, without touching the document.</summary>
    private void KeepLastProject(NestProjectViewModel doc)
    {
      if (doc?.ProjectInfo == null)
      {
        return;
      }

      try
      {
        string json = doc.ProjectInfo.ToJson();
        string nest = (ViewModel.NestMonitorViewModel.SelectedItem as NestResult)?.ToJson(false);
        if (!string.IsNullOrEmpty(nest) && JsonNode.Parse(json) is JsonObject root)
        {
          root["LastNestResultJson"] = nest;
          json = root.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        }

        LastProject.Write(json, doc.FilePath);
      }
      catch
      {
        // Never in the operator's way.
      }
    }

    private void OnFileMenuOpened(object sender, RoutedEventArgs e)
    {
      if (this.menuOpenLast == null || !ReferenceEquals(e.OriginalSource, this.menuFile))
      {
        return;
      }

      bool kept = LastProject.TryGet(out string original, out string savedAt);
      this.menuOpenLast.IsEnabled = kept;
      this.menuOpenLast.ToolTip = kept
        ? Localization.Format("Tip_OpenLast", string.IsNullOrEmpty(original) ? Localization.Get("Msg_RecoverUnsaved") : original, savedAt)
        : null;
    }

    /// <summary>Opens the kept project as the job it was: pointed at its own file (Save As when it never had
    /// one) and unsaved, like a recovered autosave.</summary>
    private void OnOpenLastProject(object sender, RoutedEventArgs e)
    {
      if (this.nestCts != null)
      {
        return;
      }

      if (!LastProject.TryGet(out string original, out _))
      {
        ViewModel.MessageService.DisplayMessageBox(Localization.Get("Msg_NoLastProject"), Localization.Get("Menu_OpenLast"), MessageBoxIcon.Information);
        return;
      }

      var before = ViewModel.ActiveDocument;
      ViewModel.LoadNestProjectInteractive(LastProject.FilePath);
      if (ViewModel.ActiveDocument is NestProjectViewModel opened && !ReferenceEquals(opened, before)
        && string.Equals(opened.FilePath, LastProject.FilePath, StringComparison.OrdinalIgnoreCase))
      {
        opened.FilePath = original ?? string.Empty;
        opened.IsDirty = true;
        string folder = string.IsNullOrEmpty(original) ? null : System.IO.Path.GetDirectoryName(original);
        if (!string.IsNullOrEmpty(folder) && System.IO.Directory.Exists(folder))
        {
          ViewModel.SvgNestConfigViewModel.SvgNestConfig.LastNestFilePath = folder;
        }
      }
    }
  }
}
