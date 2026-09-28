namespace DeepNestSharp.Domain.ViewModels
{
  using DeepNestLib;
  using DeepNestSharp.Domain.Models;

  public class SvgNestConfigViewModel : ISvgNestConfigViewModel
  {
    /// <summary>
    /// Initializes a new instance of the <see cref="SvgNestConfigViewModel"/> class.
    /// </summary>
    public SvgNestConfigViewModel(ISvgNestConfig config)
    {
      this.SvgNestConfig = new ObservableSvgNestConfig(config);
    }

    public ISvgNestConfig SvgNestConfig { get; }
  }
}
