namespace DeepNestLib.NestProject
{
  using System;
  using System.Collections.Generic;
  using System.Linq;

  /// <summary>One sheet material the shop cuts, by the key a project stores.</summary>
  public sealed class SheetMaterial
  {
    internal SheetMaterial(string key, double density)
    {
      this.Key = key;
      this.Density = density;
    }

    /// <summary>What a project stores; the name shown is translated by the app from it.</summary>
    public string Key { get; }

    /// <summary>Grams per cubic centimetre, which is kilograms per square metre per millimetre of thickness.</summary>
    public double Density { get; }
  }

  /// <summary>
  /// A project is cut from one material in one thickness: different metals or gauges are different jobs, nested
  /// apart. This is the list to choose from and what it weighs.
  /// </summary>
  public static class SheetMaterials
  {
    public const string Black = "black";
    public const string Galvanized = "galvanized";
    public const string Stainless = "stainless";
    public const string Aluminium = "aluminium";

    public static IReadOnlyList<SheetMaterial> All { get; } = new[]
    {
      new SheetMaterial(Black, 7.85),
      new SheetMaterial(Galvanized, 7.85),
      new SheetMaterial(Stainless, 7.93),
      new SheetMaterial(Aluminium, 2.70),
    };

    /// <summary>The shop's usual gauges, in millimetres; any other can be typed.</summary>
    public static IReadOnlyList<double> StandardThicknesses { get; } = new[] { 0.6, 0.7, 0.8, 1.0, 1.2, 1.5, 2.0, 3.0, 4.0, 5.0 };

    public static SheetMaterial Find(string key)
      => All.FirstOrDefault(m => string.Equals(m.Key, key, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Kilograms of a flat area of the material: square millimetres times millimetres of thickness times
    /// grams per cubic centimetre, over a million. Null when the material or thickness is not set.
    /// </summary>
    public static double? Kilograms(double areaSquareMillimetres, string materialKey, double thicknessMillimetres)
    {
      var material = Find(materialKey);
      if (material == null || !(thicknessMillimetres > 0) || !(areaSquareMillimetres >= 0))
      {
        return null;
      }

      return areaSquareMillimetres * thicknessMillimetres * material.Density / 1_000_000.0;
    }
  }
}
