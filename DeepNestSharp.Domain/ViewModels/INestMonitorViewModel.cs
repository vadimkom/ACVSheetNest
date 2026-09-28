namespace DeepNestSharp.Domain.ViewModels
{
  using System.ComponentModel;
  using DeepNestLib;
  using DeepNestLib.Placement;

  public interface INestMonitorViewModel : INotifyPropertyChanged
  {
    int SelectedIndex { get; set; }

    INestResult SelectedItem { get; set; }

    TopNestResultsCollection TopNestResults { get; }

    void Reset();

    void EnsureResultSelected();
  }
}
