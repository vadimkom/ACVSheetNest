namespace DeepNestSharp.Reports
{
  /// <summary>
  /// What the stock ran short of: how many sheets of a standard size the parts left over took when laid on
  /// virtual sheets. The report says how many to buy; those sheets are not cut and not in its totals.
  /// </summary>
  public sealed class ReportShortage
  {
    public ReportShortage(int sheets, double width, double height)
    {
      this.Sheets = sheets;
      this.Width = width;
      this.Height = height;
    }

    public int Sheets { get; }

    public double Width { get; }

    public double Height { get; }
  }
}
