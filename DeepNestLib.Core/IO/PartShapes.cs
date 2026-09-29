namespace DeepNestLib.IO
{
  using System;
  using System.Globalization;
  using System.IO;
  using IxMilia.Dxf;
  using IxMilia.Dxf.Entities;

  /// <summary>The plain shapes an operator can add without drawing them.</summary>
  public enum PartShape
  {
    Rectangle,
    Circle,
  }

  /// <summary>
  /// Draws a rectangle or a circle as a DXF of its own, so fifty 100 x 100 squares are a row like any other
  /// part rather than a trip to the CAD program. The file is the part: its name is the part's name.
  /// </summary>
  public static class PartShapes
  {
    /// <summary>Where the shapes live. Not temp: a saved project points at them and nothing else could
    /// put them back.</summary>
    public static string DefaultFolder => Path.Combine(
      Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SheetNest", "shapes");

    /// <summary>
    /// Writes the shape on the cut layer, its lower left corner on the origin, and returns the file.
    /// <paramref name="width"/> is the diameter of a circle; its height is ignored.
    /// </summary>
    public static string Write(string folder, string name, PartShape shape, double width, double height, bool millimetres)
    {
      if (!(width > 0) || (shape == PartShape.Rectangle && !(height > 0)))
      {
        throw new ArgumentOutOfRangeException(nameof(width), "A shape needs a size greater than zero.");
      }

      var file = new DxfFile();
      file.Header.Version = DxfAcadVersion.R2000;
      file.Header.DefaultDrawingUnits = millimetres ? DxfUnits.Millimeters : DxfUnits.Inches;
      file.Header.DrawingUnits = millimetres ? DxfDrawingUnits.Metric : DxfDrawingUnits.English;

      DxfEntity entity = shape == PartShape.Circle
        ? new DxfCircle(new DxfPoint(width / 2, width / 2, 0), width / 2)
        : new DxfLwPolyline(new[]
          {
            new DxfLwPolylineVertex { X = 0, Y = 0 },
            new DxfLwPolylineVertex { X = width, Y = 0 },
            new DxfLwPolylineVertex { X = width, Y = height },
            new DxfLwPolylineVertex { X = 0, Y = height },
          })
        { IsClosed = true };
      entity.Layer = DxfOutputLayers.CutName;
      file.Entities.Add(entity);

      Directory.CreateDirectory(folder);
      string path = Path.Combine(folder, Sanitise(name) + ".dxf");
      file.Save(path);
      return path;
    }

    /// <summary>The size as a name would carry it: "100x50", "D100", without trailing zeros.</summary>
    public static string SizeText(PartShape shape, double width, double height)
    {
      string N(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);
      return shape == PartShape.Circle ? "D" + N(width) : N(width) + "x" + N(height);
    }

    private static string Sanitise(string name)
    {
      var invalid = Path.GetInvalidFileNameChars();
      var chars = name.Trim().ToCharArray();
      for (int i = 0; i < chars.Length; i++)
      {
        if (Array.IndexOf(invalid, chars[i]) >= 0)
        {
          chars[i] = '_';
        }
      }

      string cleaned = new string(chars).Trim('.', ' ');
      return cleaned.Length == 0 ? "shape" : cleaned;
    }
  }
}
