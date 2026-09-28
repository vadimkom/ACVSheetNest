namespace DeepNestLib
{
  /// <summary>
  /// Specifies the destination layer for exporting part file names as text in nested DXF layouts.
  /// The values are stored in the user's settings; do not renumber them.
  /// </summary>
  public enum DxfPartNameExport
  {
    /// <summary>Do not export part names.</summary>
    None = 0,

    /// <summary>Export part names to the PAINT layer, engraved together with the bend lines.</summary>
    Paint = 1,

    /// <summary>Export part names to the BACKGROUND layer (visible in CAM/CAD for reference without cutting or engraving).</summary>
    Background = 2,
  }
}
