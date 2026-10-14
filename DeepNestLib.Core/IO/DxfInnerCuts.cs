namespace DeepNestLib.IO
{
  using System;
  using System.Collections.Concurrent;
  using System.Collections.Generic;
  using System.IO;
  using System.Linq;
  using IxMilia.Dxf;
  using IxMilia.Dxf.Entities;

  /// <summary>
  /// The cuts inside a part that enclose nothing (<see cref="DxfOutputLayers.InnerCutSourceName"/>), read from
  /// its drawing: for the report's cut length and pierces and for drawing them on the layout. The export reads
  /// them itself, with the bends.
  /// </summary>
  public static class DxfInnerCuts
  {
    private static readonly ConcurrentDictionary<string, (DateTime Stamp, IReadOnlyList<(DxfPoint A, DxfPoint B)> Segments)> Cache =
      new ConcurrentDictionary<string, (DateTime, IReadOnlyList<(DxfPoint, DxfPoint)>)>(StringComparer.OrdinalIgnoreCase);

    /// <summary>The straight inner cuts of a drawing in its own coordinates; none for a drawing without them,
    /// or one that is not a DXF or cannot be read.</summary>
    public static IReadOnlyList<(DxfPoint A, DxfPoint B)> Of(string path)
    {
      if (string.IsNullOrWhiteSpace(path) || !path.EndsWith(".dxf", StringComparison.OrdinalIgnoreCase))
      {
        return Array.Empty<(DxfPoint, DxfPoint)>();
      }

      try
      {
        var stamp = File.GetLastWriteTimeUtc(path);
        if (Cache.TryGetValue(path, out var cached) && cached.Stamp == stamp)
        {
          return cached.Segments;
        }

        var segments = new List<(DxfPoint, DxfPoint)>();
        foreach (var entity in DxfFile.Load(path).Entities.Where(DxfParser.IsInnerCut))
        {
          switch (entity)
          {
            case DxfLine line:
              segments.Add((line.P1, line.P2));
              break;
            case DxfLwPolyline poly:
              var v = poly.Vertices.ToList();
              for (int i = 0; i + 1 < v.Count; i++)
              {
                segments.Add((new DxfPoint(v[i].X, v[i].Y, 0), new DxfPoint(v[i + 1].X, v[i + 1].Y, 0)));
              }

              break;
          }
        }

        Cache[path] = (stamp, segments);
        return segments;
      }
      catch (Exception)
      {
        return Array.Empty<(DxfPoint, DxfPoint)>();
      }
    }

    /// <summary>How long the inner cuts are and how many pieces they are cut in (each one a pierce).</summary>
    public static (double Length, int Pieces) Measure(string path)
    {
      var segments = Of(path);
      return (segments.Sum(s => Math.Sqrt(Math.Pow(s.B.X - s.A.X, 2) + Math.Pow(s.B.Y - s.A.Y, 2))), segments.Count);
    }

    /// <summary>The inner cuts where a placement puts them: the same move, turn (the part's own rotation, as the
    /// export takes it) and mirror the export applies, so the layout draws them where they will be cut.</summary>
    public static IReadOnlyList<(DxfPoint A, DxfPoint B)> Placed(string path, double x, double y, double rotation, bool mirror)
    {
      var segments = Of(path);
      if (segments.Count == 0)
      {
        return segments;
      }

      var lines = segments.Select(s => (DxfEntity)new DxfLine(s.A, s.B)).ToList();
      return DxfExporter.PlaceEntities(lines, new DxfPoint(x, y, 0), rotation, mirror)
        .OfType<DxfLine>()
        .Select(l => (l.P1, l.P2))
        .ToList();
    }
  }
}
