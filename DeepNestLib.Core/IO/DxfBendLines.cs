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
  /// A part's bend lines, read from its drawing the way the export picks them (open lines that are not the
  /// outline, a hole, a label or an inner cut), as polylines for drawing on screen. The export reads its own.
  /// </summary>
  public static class DxfBendLines
  {
    private static readonly ConcurrentDictionary<string, (DateTime Stamp, IReadOnlyList<DxfEntity> Entities)> Cache =
      new ConcurrentDictionary<string, (DateTime, IReadOnlyList<DxfEntity>)>(StringComparer.OrdinalIgnoreCase);

    /// <summary>The bend entities of a drawing in its own coordinates; none when it has none or cannot be read.</summary>
    public static IReadOnlyList<DxfEntity> EntitiesOf(string path)
    {
      if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
      {
        return Array.Empty<DxfEntity>();
      }

      try
      {
        var stamp = File.GetLastWriteTimeUtc(path);
        if (Cache.TryGetValue(path, out var cached) && cached.Stamp == stamp)
        {
          return cached.Entities;
        }

        var all = DrawingFile.Load(path).Entities.ToArray();
        var detail = DxfParser.ConvertDxfToRawDetail(path, all);
        var cut = new HashSet<DxfEntity>(detail.Outers.OfType<LocalContour<DxfEntity>>().SelectMany(c => c.Entities));
        IReadOnlyList<DxfEntity> bends = all
          .Where(e => !cut.Contains(e) && !DxfParser.IsAnnotation(e) && !DxfParser.IsInnerCut(e) && IsDrawable(e))
          .ToList();
        Cache[path] = (stamp, bends);
        return bends;
      }
      catch (Exception)
      {
        return Array.Empty<DxfEntity>();
      }
    }

    /// <summary>The bend lines in the drawing's own coordinates, as polylines.</summary>
    public static IReadOnlyList<IReadOnlyList<DxfPoint>> Of(string path)
      => EntitiesOf(path).Select(Tessellate).Where(p => p.Count >= 2).ToList();

    /// <summary>The bend lines where a placement puts them: the same move, turn and mirror as the export and
    /// <see cref="DxfInnerCuts.Placed"/>.</summary>
    public static IReadOnlyList<IReadOnlyList<DxfPoint>> Placed(string path, double x, double y, double rotation, bool mirror)
    {
      var lines = Of(path);
      if (lines.Count == 0)
      {
        return lines;
      }

      // The export's transform (DxfExporter.TransformLocation): mirror across Y, turn about the origin, move.
      double angle = rotation * Math.PI / 180.0;
      double cos = Math.Cos(angle);
      double sin = Math.Sin(angle);
      return lines
        .Select(line => (IReadOnlyList<DxfPoint>)line.Select(p =>
        {
          double px = mirror ? -p.X : p.X;
          return new DxfPoint((px * cos) - (p.Y * sin) + x, (px * sin) + (p.Y * cos) + y, 0);
        }).ToList())
        .ToList();
    }

    private static bool IsDrawable(DxfEntity e)
      => e is DxfLine || e is DxfLwPolyline || e is DxfPolyline || e is DxfArc || e is DxfCircle || e is DxfSpline || e is DxfEllipse;

    private static IReadOnlyList<DxfPoint> Tessellate(DxfEntity entity)
    {
      switch (entity)
      {
        case DxfLine line:
          return new[] { line.P1, line.P2 };
        case DxfLwPolyline poly:
          {
            var pts = poly.Vertices.Select(v => new DxfPoint(v.X, v.Y, 0)).ToList();
            if (poly.IsClosed && pts.Count > 2)
            {
              pts.Add(pts[0]);
            }

            return pts;
          }

        case DxfPolyline poly:
          {
            var pts = poly.Vertices.Select(v => v.Location).ToList();
            if (poly.IsClosed && pts.Count > 2)
            {
              pts.Add(pts[0]);
            }

            return pts;
          }

        case DxfArc arc:
          {
            double start = arc.StartAngle;
            double end = arc.EndAngle;
            if (end < start)
            {
              end += 360;
            }

            return ArcPoints(arc.Center, arc.Radius, start, end);
          }

        case DxfCircle circle:
          return ArcPoints(circle.Center, circle.Radius, 0, 360);
        case DxfSpline spline:
          return (spline.FitPoints.Count >= 2 ? spline.FitPoints : spline.ControlPoints.Select(c => c.Point)).ToList();
        case DxfEllipse ellipse:
          {
            var major = ellipse.MajorAxis;
            double a = Math.Sqrt((major.X * major.X) + (major.Y * major.Y));
            double b = a * ellipse.MinorAxisRatio;
            double rot = Math.Atan2(major.Y, major.X);
            double t0 = ellipse.StartParameter;
            double t1 = ellipse.EndParameter;
            if (t1 <= t0)
            {
              t1 += 2 * Math.PI;
            }

            const int n = 48;
            var pts = new List<DxfPoint>(n + 1);
            for (int i = 0; i <= n; i++)
            {
              double t = t0 + ((t1 - t0) * i / n);
              double ex = a * Math.Cos(t);
              double ey = b * Math.Sin(t);
              pts.Add(new DxfPoint(
                ellipse.Center.X + (ex * Math.Cos(rot)) - (ey * Math.Sin(rot)),
                ellipse.Center.Y + (ex * Math.Sin(rot)) + (ey * Math.Cos(rot)),
                0));
            }

            return pts;
          }

        default:
          return Array.Empty<DxfPoint>();
      }
    }

    private static IReadOnlyList<DxfPoint> ArcPoints(DxfPoint c, double r, double startDeg, double endDeg)
    {
      int n = Math.Max(4, (int)Math.Ceiling((endDeg - startDeg) / 7.5));
      var pts = new List<DxfPoint>(n + 1);
      for (int i = 0; i <= n; i++)
      {
        double t = (startDeg + ((endDeg - startDeg) * i / n)) * Math.PI / 180.0;
        pts.Add(new DxfPoint(c.X + (r * Math.Cos(t)), c.Y + (r * Math.Sin(t)), 0));
      }

      return pts;
    }
  }
}
