namespace DeepNestSharp.Ui.Views
{
  using System.Collections.Generic;
  using System.Windows;
  using DeepNestSharp.Domain.ViewModels;
  using DeepNestSharp.Ui.Models;
  using Localization = DeepNestSharp.Ui.Localization;

  /// <summary>The job's pictures for the report (the user, 2026-11-23), kept in the project.</summary>
  public partial class MainWindow
  {
    /// <summary>The pictures the report prints after the layouts; empty when the job has none.</summary>
    private List<Reports.ReportPicture> CurrentReportPictures()
      => ViewModel?.ActiveDocument is NestProjectViewModel doc
        ? Reports.ReportPicture.FromProject(doc.ProjectInfo.Pictures)
        : new List<Reports.ReportPicture>();

    private void OnReportPictures(object sender, RoutedEventArgs e)
    {
      if (!(ViewModel?.ActiveDocument is NestProjectViewModel doc) || !(doc.ProjectInfo is ObservableProjectInfo info))
      {
        return;
      }

      var dialog = new PicturesWindow(info.Pictures) { Owner = this };
      if (dialog.ShowDialog() != true)
      {
        return;
      }

      info.Pictures = dialog.Pictures;
      this.ShowPictureCount();
      if (this.reportPreviewGrid?.Visibility == Visibility.Visible)
      {
        this.UpdateReportPreview();
      }
    }

    /// <summary>The button says how many pictures the job has.</summary>
    private void ShowPictureCount()
    {
      if (this.btnReportPictures == null)
      {
        return;
      }

      int n = ViewModel?.ActiveDocument is NestProjectViewModel doc ? doc.ProjectInfo.Pictures?.Count ?? 0 : 0;
      this.btnReportPictures.Content = n > 0 ? Localization.Format("Btn_PicturesN", n) : Localization.Get("Btn_Pictures");
    }
  }
}
