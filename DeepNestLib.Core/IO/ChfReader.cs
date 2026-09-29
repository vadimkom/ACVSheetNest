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
  /// <para>The format is plain text and undocumented; this reads what the CAM wrote for a DXF saved as CHF.
  /// The file opens with <c>scFlie</c>, then <c>&lt;Begin Graphs&gt;</c> and a count. Each <c>####graph</c>
  /// is one contour or line: two header values, then <c>&lt;Glyphs&gt;</c> with its length, box, start, end and
  /// the number of pieces, each piece a <c>####Gly</c> with a direction and a type:</para>
  /// <list type="bullet">
  /// <item><c>2</c> a line: two points.</item>
  /// <item><c>4</c> a circle: centre and radius.</item>
  /// <item><c>7</c> a B-spline: a rational flag, degree, control points, knots (the CAM writes arcs this way).</item>
  /// </list>
  /// <para>After <c>&lt;End Glyphs&gt;</c> come the graph's LAYER in the CAM (0 is cut) and whether it is an outer
  /// (1) or inner (2) contour, then machining settings (<c>&lt;Crafts&gt;</c>) that are not geometry. On the
  /// sample, holes and outlines were on layer 0 and the bend lines on layer 1, which is how the CAM showed
  /// them. Layer 0 comes in on layer "0" and anything else on the marking layer, as a DXF would carry it.</para>
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

      while (i < lines.Count && lines[i] != "<End Graphs>")
      {
        if (!lines[i].StartsWith("####graph", StringComparison.Ordinal))
        {
          i++;
          continue;
        }

        string graph = lines[i];
        while (i < lines.Count && lines[i] != "<Glyphs>")
        {
          i++;
        }

        i++; // <Glyphs>
        i += 5; // length, box min, box max, start, end
        int pieces = Int(lines, i++, graph);
        var entities = new List<DxfEntity>();
        for (int p = 0; p < pieces; p++)
        {
          if (!lines[i].StartsWith("####Gly", StringComparison.Ordinal))
          {
            throw Bad(graph, $"expected piece {p + 1}, found \"{lines[i]}\"");
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
            case 4:
              entities.Add(new DxfCircle(Point(lines, i, graph), Num(lines, i + 1, graph)));
              i += 2;
              break;
            case 7:
              {
                int rational = Int(lines, i++, graph);
                if (rational != 0)
                {
                  throw Bad(graph, "a rational spline, which SheetNest has not seen written by the CAM yet");
                }

                var spline = new DxfSpline { DegreeOfCurve = Int(lines, i++, graph) };
                int controls = Int(lines, i++, graph);
                for (int c = 0; c < controls; c++)
                {
                  spline.ControlPoints.Add(new DxfControlPoint(Point(lines, i++, graph)));
                }

                int knots = Int(lines, i++, graph);
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
        string layerName = layer == 0 ? DxfOutputLayers.CutName : DxfOutputLayers.PaintName;
        foreach (var entity in entities)
        {
          entity.Layer = layerName;
          file.Entities.Add(entity);
        }

        while (i < lines.Count && lines[i] != "<End Crafts>" && lines[i] != "<End Graphs>")
        {
          i++;
        }
      }

      return file;
    }

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
