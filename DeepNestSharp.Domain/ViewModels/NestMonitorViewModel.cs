namespace DeepNestSharp.Domain.ViewModels
{
  using System.Linq;
  using System.Threading.Tasks;
  using DeepNestLib;
  using DeepNestLib.Placement;
  using Microsoft.Toolkit.Mvvm.ComponentModel;

  /// <summary>
  /// The nest results on screen and which one is selected. The window runs the nest itself (sparrow) and
  /// hands each result in through <see cref="TopNestResults"/>; this only holds and selects them.
  /// </summary>
  public class NestMonitorViewModel : ObservableObject, INestMonitorViewModel
  {
    private int selectedIndex;
    private INestResult selectedItem;

    public NestMonitorViewModel(IMainViewModel mainViewModel)
    {
      this.TopNestResults = new TopNestResultsCollection(
        mainViewModel.SvgNestConfigViewModel.SvgNestConfig,
        mainViewModel.DispatcherService);
    }

    public int SelectedIndex
    {
      get => selectedIndex;
      set => SetProperty(ref selectedIndex, value);
    }

    public INestResult SelectedItem
    {
      get => selectedItem;
      set
      {
        if (value == null && !this.TopNestResults.IsEmpty)
        {
          _ = Task.Factory.StartNew(() =>
            {
              SelectedIndex = 0;
            });
        }
        else
        {
          // No results left (e.g. after New Project): null the field and notify so the bound viewer clears.
          SetProperty(ref selectedItem, value);
        }
      }
    }

    public TopNestResultsCollection TopNestResults { get; }

    /// <summary>Clear any displayed nest result + preview (e.g. when a new project is created).</summary>
    public void Reset()
    {
      this.TopNestResults.SetSingleResult(null); // empty the results list
      this.SelectedItem = null;                  // list now empty → nulls the field + notifies → viewer clears
    }

    /// <summary>
    /// Auto-select the best (top) result so its preview shows without a manual click — but never override a
    /// result the user deliberately selected.
    /// </summary>
    public void EnsureResultSelected()
    {
      if (this.TopNestResults.Count > 0
          && (this.SelectedItem == null || !this.TopNestResults.Contains(this.SelectedItem)))
      {
        // Assign the ITEM, not just the index: SelectedIndex = 0 can be a no-op when it is already 0, and the
        // TwoWay-bound ListView would never apply the selection.
        this.SelectedIndex = 0;
        this.SelectedItem = this.TopNestResults.First();
      }
    }
  }
}
