namespace DeepNestSharp.Reports
{
  /// <summary>
  /// What the job is cut from, for the report's summary: the material's name as shown, the sheet thickness in
  /// millimetres and the density in grams per cubic centimetre, which make the weights.
  /// </summary>
  public sealed record ReportMaterial(string Name, double Thickness, double Density)
  {
    /// <summary>Kilograms of a flat area in square millimetres.</summary>
    public double Kilograms(double areaSquareMillimetres) => areaSquareMillimetres * this.Thickness * this.Density / 1_000_000.0;
  }
}
