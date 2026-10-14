namespace DeepNestLib.IO
{
  /// <summary>One layer of an exported sheet: its name and the colour the layer is declared with.</summary>
  public sealed class DxfOutputLayer
  {
    public DxfOutputLayer(string name, short colorIndex)
    {
      this.Name = name;
      this.ColorIndex = colorIndex;
    }

    public string Name { get; }

    /// <summary>AutoCAD colour index; every entity on the layer is written ByLayer, so this is its colour.</summary>
    public short ColorIndex { get; }
  }

  /// <summary>
  /// The ONLY layers a sheet is written on, agreed with the shop: the laser's CAM (SC2000) sorts a file into
  /// its own layers by these, so everything that is cut goes on <see cref="Cut"/> in one colour, whatever the
  /// source drawing used and whether it is an outline, a hole, a shared cut or the offcut line. A hole used to
  /// come out red and a shared cut green or blue, and the CAM took each colour for a different operation.
  /// Change a name or a colour here and every export follows.
  /// </summary>
  public static class DxfOutputLayers
  {
    // Names as constants too, for the places (attributes, switch labels) that need one.
    public const string CutName = "0";
    public const string PaintName = "9";
    public const string BackgroundName = "Background";

    /// <summary>Everything the laser cuts through. Layer 0, white: what SolidWorks and Solid Edge Nesting write
    /// and the CAM already cuts.</summary>
    public static readonly DxfOutputLayer Cut = new DxfOutputLayer(CutName, 7);

    /// <summary>Marked, not cut: bend lines and part names written to be engraved. Layer 9, as in SolidWorks.</summary>
    public static readonly DxfOutputLayer Paint = new DxfOutputLayer(PaintName, 1);

    /// <summary>For the eye only, never processed: part names written for display.</summary>
    public static readonly DxfOutputLayer Background = new DxfOutputLayer(BackgroundName, 8);

    /// <summary>
    /// A layer of a PART drawing, never of an exported sheet: open lines on it are cuts inside the part that
    /// enclose nothing, such as the shared cut between strips of perforated tape drawn as one block. The
    /// nest ignores them (the block's outline and holes are the part) and the export writes them on
    /// <see cref="Cut"/>. Any other open line in a part drawing is a bend and is marked, not cut.
    /// </summary>
    public const string InnerCutSourceName = "SHEETNEST_INNER_CUT";
  }
}
