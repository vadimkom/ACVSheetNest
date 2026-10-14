namespace DeepNestLib.IO
{
  using System;
  using System.Collections.Generic;
  using System.Globalization;
  using System.IO;
  using IxMilia.Dxf;
  using IxMilia.Dxf.Entities;

  /// <summary>The plain shapes an operator can add without drawing them.</summary>
  public enum PartShape
  {
    Rectangle,
    Circle,
    Washer,
  }

  /// <summary>A metric flat washer: the hole and the outside diameter of the standard and the wide one, mm.</summary>
  public sealed record WasherSize(string Thread, double Hole, double Outer, double WideOuter);

  /// <summary>
  /// Draws a rectangle, a circle or a washer as a DXF of its own, so fifty 100 x 100 squares are a row like any
  /// other part rather than a trip to the CAD program. The file is the part: its name is the part's name.
  /// </summary>
  public static class PartShapes
  {
    /// <summary>Where the shapes live. Not temp: a saved project points at them and nothing else could
    /// put them back.</summary>
    public static string DefaultFolder => Path.Combine(
      Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SheetNest", "shapes");

    /// <summary>Flat washers M8 to M12: DIN 125-A / ISO 7089 for the standard one, DIN 9021 / ISO 7093 for the
    /// wide one. The thickness is the sheet's.</summary>
    public static readonly IReadOnlyList<WasherSize> Washers = new[]
    {
      new WasherSize("M8", 8.4, 16, 24),
      new WasherSize("M10", 10.5, 20, 30),
      new WasherSize("M12", 13, 24, 37),
    };

    /// <summary>
    /// Writes the shape on the cut layer, its lower left corner on the origin, and returns the file.
    /// <paramref name="width"/> is the diameter of a circle or the outside of a washer; <paramref name="height"/>
    /// is a washer's hole, and a circle ignores it.
    /// </summary>
    public static string Write(string folder, string name, PartShape shape, double width, double height, bool millimetres)
    {
      if (!(width > 0) || (shape != PartShape.Circle && !(height > 0)))
      {
        throw new ArgumentOutOfRangeException(nameof(width), "A shape needs a size greater than zero.");
      }

      if (shape == PartShape.Washer && !(height < width))
      {
        throw new ArgumentOutOfRangeException(nameof(height), "A washer's hole has to be smaller than its outside.");
      }

      var file = new DxfFile();
      file.Header.Version = DxfAcadVersion.R2000;
      file.Header.DefaultDrawingUnits = millimetres ? DxfUnits.Millimeters : DxfUnits.Inches;
      file.Header.DrawingUnits = millimetres ? DxfDrawingUnits.Metric : DxfDrawingUnits.English;

      var centre = new DxfPoint(width / 2, width / 2, 0);
      var entities = new List<DxfEntity>();
      switch (shape)
      {
        case PartShape.Circle:
          entities.Add(new DxfCircle(centre, width / 2));
          break;
        case PartShape.Washer:
          entities.Add(new DxfCircle(centre, width / 2));
          entities.Add(new DxfCircle(centre, height / 2));
          break;
        default:
          entities.Add(new DxfLwPolyline(new[]
          {
            new DxfLwPolylineVertex { X = 0, Y = 0 },
            new DxfLwPolylineVertex { X = width, Y = 0 },
            new DxfLwPolylineVertex { X = width, Y = height },
            new DxfLwPolylineVertex { X = 0, Y = height },
          })
          { IsClosed = true });
          break;
      }

      foreach (var entity in entities)
      {
        entity.Layer = DxfOutputLayers.CutName;
        file.Entities.Add(entity);
      }

      Directory.CreateDirectory(folder);
      string path = Path.Combine(folder, Sanitise(name) + ".dxf");
      file.Save(path);
      return path;
    }

    /// <summary>The size as a name would carry it: "100x50", "D100", a washer "D24-8.4", without trailing
    /// zeros.</summary>
    public static string SizeText(PartShape shape, double width, double height)
    {
      string N(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);
      return shape switch
      {
        PartShape.Circle => "D" + N(width),
        PartShape.Washer => "D" + N(width) + "-" + N(height),
        _ => N(width) + "x" + N(height),
      };
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
