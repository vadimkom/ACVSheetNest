namespace DeepNestLib.IO
{
  using System;
  using System.Collections.Generic;
  using System.Globalization;
  using System.IO;
  using System.Text;
  using IxMilia.Dxf;
  using IxMilia.Dxf.Entities;

  /// <summary>
  /// Reads a CHF drawing (the laser CAM's own format, SC2000) into the same DXF entities a DXF drawing gives,
  /// so a part drawn or kept in the CAM nests, exports and shows like any other.
  /// </summary>
  /// <remarks>
  /// <para>The format is plain text and undocumented; this reads what the CAM wrote in 572 files an operator
  /// kept (parts and whole nested sheets), and the length the CAM writes over every contour confirms each
  /// reading. The file opens with <c>scFlie</c>, then <c>&lt;Begin Graphs&gt;</c> and a count. Each
  /// <c>####graph</c> is of a kind: 8 one contour, 9 a group of contours (a part on a nested sheet), 10 a
  /// text with its letters as contours. A contour is <c>&lt;Glyphs&gt;</c> with its length, box, start, end and
  /// the number of pieces, each piece a <c>####Gly</c> with a direction and a type:</para>
  /// <list type="bullet">
  /// <item><c>2</c> a line: two points.</item>
  /// <item><c>3</c> an arc: centre, radius, from and to angles in radians.</item>
  /// <item><c>4</c> a circle: centre and radius.</item>
  /// <item><c>6</c> a polyline: closed flag, count, then each vertex and its bulge.</item>
  /// <item><c>7</c> a B-spline: two numbers of no meaning, control points, knots.</item>
  /// </list>
  /// <para>After <c>&lt;End Glyphs&gt;</c> come the contour's LAYER in the CAM and its kind (1 outer, 2 inner,
  /// others open or lead-ins), then machining settings (<c>&lt;Crafts&gt;</c>) that are not geometry. Which CAM
  /// layers are cut and which engraved is <see cref="LayerFor"/>. A group or a text ends with two more
  /// numbers, the group's own layer and kind, which its members already carry.</para>
  /// <para>Anything else is refused by name rather than guessed at: a piece of a type not seen yet would
  /// otherwise be cut as whatever it happened to resemble.</para>
  /// </remarks>
  public static class ChfReader
  {
    public const string Signature = "scFlie";

    /// <summary>Whether these bytes are a CHF drawing (it opens with <see cref="Signature"/>).</summary>
    public static bool IsChf(byte[] bytes)
    {
      if (bytes == null || bytes.Length < Signature.Length)
      {
        return false;
      }

      int start = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF ? 3 : 0;
      return bytes.Length - start >= Signature.Length
        && Encoding.ASCII.GetString(bytes, start, Signature.Length) == Signature;
    }

    public static DxfFile Read(byte[] bytes)
    {
      using var reader = new StreamReader(new MemoryStream(bytes, writable: false), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
      return Read(reader);
    }

    public static DxfFile Read(TextReader reader)
    {
      var lines = new List<string>();
      string line;
      while ((line = reader.ReadLine()) != null)
      {
        lines.Add(line.Trim());
      }

      var file = new DxfFile();
      file.Header.Version = DxfAcadVersion.R2000;

      // SC2000 works in millimetres and the file does not say so. Left unitless, the drawing reads as
      // inches wherever a tolerance is scaled by units, which is 25 times too tight for common cutting.
      file.Header.DefaultDrawingUnits = DxfUnits.Millimeters;
      file.Header.DrawingUnits = DxfDrawingUnits.Metric;
      int i = 0;
      if (lines.Count == 0 || lines[0] != Signature)
      {
        throw new InvalidDataException("This is not a CHF drawing (it does not open with scFlie).");
      }

      while (i < lines.Count && lines[i] != "<Begin Graphs>")
      {
        i++;
      }

      i++; // <Begin Graphs>
      int graphs = Int(lines, i++, "the drawing");
      for (int g = 0; g < graphs; g++)
      {
        if (i >= lines.Count || !lines[i].StartsWith("####graph", StringComparison.Ordinal))
        {
          throw Bad("the drawing", $"expected contour {g + 1} of {graphs} at line {i + 1}");
        }

        string graph = lines[i++];
        int kind = Int(lines, i++, graph);
        switch (kind)
        {
          case GraphContour:
            i++; // tolerance
            i = ReadGlyphs(lines, i, graph, file);
            break;

          case GraphGroup:
            i += 5; // length, box min, box max, start, end
            i = ReadMembers(lines, i, graph, file);
            break;

          case GraphText:
            {
              // Position, angle, two scales, the text itself and its font; then the letters, already
              // turned into outlines, as a group.
              i += 5;
              while (i < lines.Count && lines[i] != "<End Text para>")
              {
                i++;
              }

              i++;
              i += 5; // length, box min, box max, start, end
              i = ReadMembers(lines, i, graph, file);
              break;
            }

          default:
            throw Bad(graph, $"a contour of kind {kind}, which SheetNest cannot read yet. Send the file so it can be added");
        }
      }

      return file;
    }

    // What a ####graph is: one contour, a group of them (a part of a nested sheet), or a text.
    private const int GraphContour = 8;
    private const int GraphGroup = 9;
    private const int GraphText = 10;

    /// <summary>The contours of a group or a text, then the group's own layer and kind, which its
    /// members already carry.</summary>
    private static int ReadMembers(List<string> lines, int i, string graph, DxfFile file)
    {
      int members = Int(lines, i++, graph);
      for (int m = 0; m < members; m++)
      {
        if (i >= lines.Count || !lines[i].StartsWith("####Group elem", StringComparison.Ordinal))
        {
          throw Bad(graph, $"expected member {m + 1} of {members} at line {i + 1}");
        }

        i += 2; // the member's heading and its tolerance
        i = ReadGlyphs(lines, i, graph, file);
      }

      return i + 2;
    }

    /// <summary>One contour's pieces, from <c>&lt;Glyphs&gt;</c> to the end of its machining settings; returns
    /// the line after them.</summary>
    private static int ReadGlyphs(List<string> lines, int i, string graph, DxfFile file)
    {
      if (i >= lines.Count || lines[i] != "<Glyphs>")
      {
        throw Bad(graph, $"expected its pieces at line {i + 1}");
      }

      i++;
      i += 5; // length, box min, box max, start, end
      int pieces = Int(lines, i++, graph);
      var entities = new List<DxfEntity>();
      for (int p = 0; p < pieces; p++)
      {
        if (i >= lines.Count || !lines[i].StartsWith("####Gly", StringComparison.Ordinal))
        {
          throw Bad(graph, $"expected piece {p + 1}, found \"{(i < lines.Count ? lines[i] : "the end of the file")}\"");
        }

        i++;
        i++; // direction: which way the machine runs it, not geometry
        int type = Int(lines, i++, graph);
        switch (type)
        {
          case 2:
            entities.Add(new DxfLine(Point(lines, i, graph), Point(lines, i + 1, graph)));
            i += 2;
            break;

          case 3:
            entities.Add(Arc(Point(lines, i, graph), Num(lines, i + 1, graph), Num(lines, i + 2, graph), Num(lines, i + 3, graph)));
            i += 4;
            break;

          case 4:
            entities.Add(new DxfCircle(Point(lines, i, graph), Num(lines, i + 1, graph)));
            i += 2;
            break;

          case 6:
            {
              // A polyline: 1 closes it (0 and 128 leave it open), then each vertex with the bulge of the
              // segment after it, as DXF has them.
              bool closed = Int(lines, i++, graph) == 1;
              int count = Int(lines, i++, graph);
              var vertices = new List<DxfLwPolylineVertex>();
              for (int v = 0; v < count; v++)
              {
                DxfPoint at = Point(lines, i++, graph);
                vertices.Add(new DxfLwPolylineVertex { X = at.X, Y = at.Y, Bulge = Num(lines, i++, graph) });
              }

              entities.Add(new DxfLwPolyline(vertices) { IsClosed = closed });
              break;
            }

          case 7:
            {
              // A B-spline. The two numbers before the control points are whatever was in the CAM's memory
              // (the degree among them only sometimes); the knots are what say the degree.
              i += 2;
              int controls = Int(lines, i++, graph);
              var points = new List<DxfPoint>();
              for (int c = 0; c < controls; c++)
              {
                points.Add(Point(lines, i++, graph));
              }

              int knots = Int(lines, i++, graph);
              int degree = knots - controls - 1;
              if (degree < 1 || degree >= controls)
              {
                throw Bad(graph, $"a spline with {controls} control points and {knots} knots, which is not a curve");
              }

              var spline = new DxfSpline { DegreeOfCurve = degree };
              points.ForEach(pt => spline.ControlPoints.Add(new DxfControlPoint(pt)));
              for (int k = 0; k < knots; k++)
              {
                spline.KnotValues.Add(Num(lines, i++, graph));
              }

              entities.Add(spline);
              break;
            }

          default:
            throw Bad(graph, $"a piece of type {type}, which SheetNest cannot read yet. Send the file so it can be added");
        }
      }

      if (i >= lines.Count || lines[i] != "<End Glyphs>")
      {
        throw Bad(graph, "its pieces do not end where they should");
      }

      int layer = Int(lines, i + 1, graph);
      string layerName = LayerFor(layer);
      foreach (var entity in layerName == null ? new List<DxfEntity>() : entities)
      {
        entity.Layer = layerName;
        file.Entities.Add(entity);
      }

      i += 3; // <End Glyphs>, layer, outer or inner
      if (i >= lines.Count || lines[i] != "<Crafts>")
      {
        throw Bad(graph, $"expected its machining settings at line {i + 1}");
      }

      while (i < lines.Count && lines[i] != "<End Crafts>")
      {
        i++;
      }

      return i + 1;
    }

    /// <summary>
    /// An arc as the CAM writes it: centre, radius, and the angles (radians) it runs from and to. It turns
    /// anticlockwise when the second is larger, clockwise otherwise; checked against the length the CAM
    /// writes over every contour, on 578 of its files.
    /// </summary>
    private static DxfEntity Arc(DxfPoint centre, double radius, double from, double to)
    {
      double sweep = to - from;
      if (Math.Abs(sweep) >= (2 * Math.PI) - 1e-9)
      {
        return new DxfCircle(centre, radius);
      }

      // DXF arcs always run anticlockwise, so a clockwise one is the same arc from its other end.
      double start = sweep > 0 ? from : to;
      double end = sweep > 0 ? to : from;
      return new DxfArc(centre, radius, Degrees(start), Degrees(end));
    }

    private static double Degrees(double radians)
    {
      double d = (radians * 180.0 / Math.PI) % 360.0;
      return d < 0 ? d + 360.0 : d;
    }

    /// <summary>
    /// The DXF layer a CAM layer comes in on. SC2000 has ten layers, each with its own settings, and the
    /// operator uses them this way: the first (0, green) is cut, the second (1, blue) engraves the bend
    /// lines, and the red ninth (8) engraves too, which is where nearly every text in the operator's 572
    /// files sits. The others are cut with other settings (5 mm black steel, aluminium): on 27 files parts
    /// sit only on 2 to 7. Layer 10, past the ten, is the background: the sheet frame and part numbers the
    /// CAM writes itself in tiny letters, sometimes off the sheet. It is left out (null), because every
    /// closed shape read in becomes part geometry: the frame would make the whole sheet one part and each
    /// letter a hole or a part of its own.
    /// </summary>
    internal static string LayerFor(int camLayer) => camLayer switch
    {
      1 or 8 => DxfOutputLayers.PaintName,
      Background => null,
      _ => DxfOutputLayers.CutName,
    };

    private const int Background = 10;

    private static int Int(List<string> lines, int i, string graph)
      => i < lines.Count && int.TryParse(lines[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out int v)
        ? v
        : throw Bad(graph, $"expected a whole number at line {i + 1}");

    private static double Num(List<string> lines, int i, string graph)
      => i < lines.Count && double.TryParse(lines[i], NumberStyles.Float, CultureInfo.InvariantCulture, out double v)
        ? v
        : throw Bad(graph, $"expected a number at line {i + 1}");

    private static DxfPoint Point(List<string> lines, int i, string graph)
    {
      string[] xy = i < lines.Count ? lines[i].Split(',') : Array.Empty<string>();
      if (xy.Length == 2
          && double.TryParse(xy[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double x)
          && double.TryParse(xy[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double y))
      {
        return new DxfPoint(x, y, 0);
      }

      throw Bad(graph, $"expected a point \"x,y\" at line {i + 1}");
    }

    private static InvalidDataException Bad(string graph, string what)
      => new InvalidDataException($"CHF {graph.TrimStart('#').Trim()}: {what}.");
  }

  /// <summary>Opens a part drawing, DXF or CHF, as DXF entities. Every place that reads a part's own file
  /// goes through here, so a CHF part nests, exports its bends and shares cuts like a DXF one.</summary>
  public static class DrawingFile
  {
    public static DxfFile Load(string path) => Load(File.ReadAllBytes(path));

    public static DxfFile Load(byte[] bytes)
    {
      if (bytes.Length == 0)
      {
        // Six of the operator's 578 CAM files were empty; say so rather than fail somewhere in the DXF reader.
        throw new InvalidDataException("The file is empty (0 bytes).");
      }

      if (ChfReader.IsChf(bytes))
      {
        return ChfReader.Read(bytes);
      }

      using var stream = new MemoryStream(bytes, writable: false);
      return DxfFile.Load(stream);
    }

    public static DxfFile Load(Stream stream)
    {
      using var copy = new MemoryStream();
      stream.CopyTo(copy);
      return Load(copy.ToArray());
    }
  }
}
