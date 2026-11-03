namespace DeepNestLib.NestProject
{
  public interface ISheetLoadInfo
  {
    int Height { get; set; }

    int Quantity { get; set; }

    /// <summary>Gets or sets a value indicating whether there is as much of this size as the job needs.
    /// <see cref="Quantity"/> keeps whatever it held and is ignored while this is set.</summary>
    bool Unlimited { get; set; }

    int Width { get; set; }
 
    /// <summary>Gets or sets the sheet priority (1-10, lower number nests first; 5 = normal).</summary>
    int Priority { get; set; }

    /// <summary>Gets or sets the drawing (DXF or CHF) the sheet's outline is read from: an offcut of any shape.
    /// Null or empty for a plain rectangle. <see cref="Width"/> and <see cref="Height"/> are then the box round it.</summary>
    string ShapePath { get; set; }

    /// <summary>Gets or sets how many quarter turns, counter-clockwise, the drawing of a sheet of any shape is
    /// turned by: an offcut lying the other way round. 0 for a drawing as drawn, and for a rectangle.</summary>
    int ShapeTurns { get; set; }
  }
}