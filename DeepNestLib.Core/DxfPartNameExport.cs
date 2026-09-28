namespace DeepNestLib
{
  /// <summary>
  /// Specifies the destination layer for exporting part file names as text in nested DXF layouts.
  /// </summary>
  public enum DxfPartNameExport
  {
    /// <summary>Do not export part names.</summary>
    None = 0,

    /// <summary>Export part names to Layer 9 (laser engraving/marking alongside bend lines).</summary>
    Layer9 = 1,

    /// <summary>Export part names to the Background layer (visible in CAM/CAD for reference without cutting or engraving).</summary>
    Background = 2,
  }
}
