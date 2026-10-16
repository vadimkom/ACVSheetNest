namespace DeepNestLib.IO
{
  using System;
  using System.Globalization;
  using System.IO;
  using System.Linq;
  using System.Text;
  using DeepNestLib.NestProject;
  using IxMilia.Dxf;
  using IxMilia.Dxf.Entities;

  /// <summary>
  /// Flips a part itself, for a drawing that came out the wrong hand. The flipped part is a drawing of its
  /// own, written to temp beside the split parts, so everything downstream (thumbnail, nest, export, bend
  /// lines, the label) reads an ordinary DXF and needs to know nothing about it.
  /// </summary>
  public static class PartMirror
  {
    /// <summary>
    /// Flips the part across one more axis, or back: pressing the same flip twice gives the drawing the
    /// row started with, and that original path, not another copy of it.
    /// </summary>
    public static void Toggle(IDetailLoadInfo part, PartFlip axis)
    {
      PartFlip next = part.Flip ^ axis;
      string original = part.Flip == PartFlip.None ? part.Path : part.UnflippedPath;
      if (next == PartFlip.None)
      {
        part.UnflippedPath = null;
        part.Path = original;
      }
      else
      {
        // Written before the row changes, so a drawing that cannot be flipped leaves the row as it was.
        string flipped = Write(original, next);
        part.UnflippedPath = original;
        part.Path = flipped;
      }

      part.Flip = next;
    }

    /// <summary>
    /// Writes a flipped part's copy again when temp has been cleaned since the project was saved. False
    /// when it is not missing a copy, or its original has gone too.
    /// </summary>
    public static bool Restore(IDetailLoadInfo part)
    {
      if (part.Flip == PartFlip.None || part.IsExists || string.IsNullOrEmpty(part.UnflippedPath) || !File.Exists(part.UnflippedPath))
      {
        return false;
      }

      try
      {
        part.Path = Write(part.UnflippedPath, part.Flip);
        return true;
      }
      catch (Exception)
      {
        // The original has changed into something that no longer reads; the row shows as missing.
        return false;
      }
    }

    /// <summary>Writes the drawing flipped as asked and returns where. The file keeps the drawing's name,
    /// which is the part's name on the row and on the cut sheet.</summary>
    public static string Write(string sourcePath, PartFlip flip)
    {
      byte[] bytes;
      using (var stream = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
      using (var buffer = new MemoryStream())
      {
        stream.CopyTo(buffer);
        bytes = buffer.ToArray();
      }

      // Reusing the drawing rather than building a new one keeps its units and layer table, for the same
      // reason DxfPartSplitter does.
      DxfFile file = DrawingFile.Load(bytes);
      Reflect(file, flip);

      string directory = CacheDir(sourcePath, flip);
      Directory.CreateDirectory(directory);
      string extension = Path.GetExtension(sourcePath);
      if (!string.Equals(extension, ".dxf", StringComparison.OrdinalIgnoreCase))
      {
        extension = ".dxf"; // a CHF is written out as the DXF it was read into
      }

      string path = Path.Combine(directory, Path.GetFileNameWithoutExtension(sourcePath) + extension);
      file.Save(path);
      return path;
    }

    /// <summary>Flips every entity in the drawing about the origin, in place.</summary>
    public static void Reflect(DxfFile file, PartFlip flip)
    {
      foreach (DxfEntity entity in file.Entities.ToList())
      {
        if (!Reflect(entity, flip))
        {
          file.Entities.Remove(entity);
        }
      }
    }

    /// <summary>
    /// Flips one entity. False for an annotation that nothing reads (a dimension, a hatch), which the caller
    /// drops rather than leave behind unflipped; anything else unknown is refused, as the reader refuses it.
    /// </summary>
    private static bool Reflect(DxfEntity entity, PartFlip flip)
    {
      bool leftRight = flip.HasFlag(PartFlip.LeftRight);
      bool upDown = flip.HasFlag(PartFlip.UpDown);
      if (leftRight == upDown)
      {
        // Both is a half turn, which keeps every curve's direction; neither changes nothing.
        return leftRight ? Turn(entity) : true;
      }

      double sx = leftRight ? -1 : 1;
      double sy = upDown ? -1 : 1;
      DxfPoint P(DxfPoint p) => new DxfPoint(sx * p.X, sy * p.Y, p.Z);
      DxfVector V(DxfVector v) => new DxfVector(sx * v.X, sy * v.Y, v.Z);

      // An angle measured from +X: across a vertical line it becomes 180 - a, across a horizontal one -a.
      double A(double degrees) => Normalise(leftRight ? 180.0 - degrees : -degrees, 360.0);

      switch (entity)
      {
        case DxfLine line:
          line.P1 = P(line.P1);
          line.P2 = P(line.P2);
          return true;

        case DxfArc arc:
          {
            // A reflection reverses the arc's turn, so its ends swap.
            double start = arc.StartAngle;
            arc.Center = P(arc.Center);
            arc.StartAngle = A(arc.EndAngle);
            arc.EndAngle = A(start);
            return true;
          }

        case DxfCircle circle:
          circle.Center = P(circle.Center);
          return true;

        case DxfEllipse ellipse:
          {
            // The minor axis follows the major one anticlockwise, so after a reflection the parameter
            // runs the other way: t becomes -t and the ends swap. A whole ellipse stays whole.
            ellipse.Center = P(ellipse.Center);
            ellipse.MajorAxis = V(ellipse.MajorAxis);
            double start = ellipse.StartParameter;
            double end = ellipse.EndParameter;
            if (Math.Abs(end - start - (2 * Math.PI)) > 1e-9)
            {
              ellipse.StartParameter = Normalise(-end, 2 * Math.PI);
              ellipse.EndParameter = Normalise(-start, 2 * Math.PI);
            }

            return true;
          }

        case DxfLwPolyline lw:
          foreach (DxfLwPolylineVertex v in lw.Vertices)
          {
            v.X *= sx;
            v.Y *= sy;
            v.Bulge = -v.Bulge;
          }

          return true;

        case DxfPolyline poly:
          foreach (DxfVertex v in poly.Vertices)
          {
            v.Location = P(v.Location);
            v.Bulge = -v.Bulge;
          }

          return true;

        case DxfSpline spline:
          {
            // A reflected NURBS is the NURBS of the reflected control points; knots and weights stay.
            var controls = spline.ControlPoints.Select(c => new DxfControlPoint(P(c.Point), c.Weight)).ToList();
            var fits = spline.FitPoints.Select(P).ToList();
            spline.ControlPoints.Clear();
            controls.ForEach(spline.ControlPoints.Add);
            spline.FitPoints.Clear();
            fits.ForEach(spline.FitPoints.Add);
            spline.StartTangent = V(spline.StartTangent);
            spline.EndTangent = V(spline.EndTangent);
            return true;
          }

        case DxfText text:
          // A note moves with the part but stays readable.
          text.Location = P(text.Location);
          text.SecondAlignmentPoint = P(text.SecondAlignmentPoint);
          return true;

        case DxfMText mtext:
          mtext.InsertionPoint = P(mtext.InsertionPoint);
          return true;

        case DxfModelPoint point:
          point.Location = P(point.Location);
          return true;

        default:
          if (DxfParser.IsAnnotation(entity))
          {
            return false;
          }

          throw new InvalidDataException($"A {entity.EntityType} cannot be flipped.");
      }
    }

    /// <summary>A half turn about the origin: every point negated, every angle plus 180.</summary>
    private static bool Turn(DxfEntity entity)
    {
      DxfPoint P(DxfPoint p) => new DxfPoint(-p.X, -p.Y, p.Z);
      DxfVector V(DxfVector v) => new DxfVector(-v.X, -v.Y, v.Z);

      switch (entity)
      {
        case DxfLine line:
          line.P1 = P(line.P1);
          line.P2 = P(line.P2);
          return true;

        case DxfArc arc:
          arc.Center = P(arc.Center);
          arc.StartAngle = Normalise(arc.StartAngle + 180.0, 360.0);
          arc.EndAngle = Normalise(arc.EndAngle + 180.0, 360.0);
          return true;

        case DxfCircle circle:
          circle.Center = P(circle.Center);
          return true;

        case DxfEllipse ellipse:
          // The major axis turns with the part and the parameters are measured from it, so they stay.
          ellipse.Center = P(ellipse.Center);
          ellipse.MajorAxis = V(ellipse.MajorAxis);
          return true;

        case DxfLwPolyline lw:
          foreach (DxfLwPolylineVertex v in lw.Vertices)
          {
            v.X = -v.X;
            v.Y = -v.Y;
          }

          return true;

        case DxfPolyline poly:
          foreach (DxfVertex v in poly.Vertices)
          {
            v.Location = P(v.Location);
          }

          return true;

        case DxfSpline spline:
          {
            var controls = spline.ControlPoints.Select(c => new DxfControlPoint(P(c.Point), c.Weight)).ToList();
            var fits = spline.FitPoints.Select(P).ToList();
            spline.ControlPoints.Clear();
            controls.ForEach(spline.ControlPoints.Add);
            spline.FitPoints.Clear();
            fits.ForEach(spline.FitPoints.Add);
            spline.StartTangent = V(spline.StartTangent);
            spline.EndTangent = V(spline.EndTangent);
            return true;
          }

        case DxfText text:
          text.Location = P(text.Location);
          text.SecondAlignmentPoint = P(text.SecondAlignmentPoint);
          return true;

        case DxfMText mtext:
          mtext.InsertionPoint = P(mtext.InsertionPoint);
          return true;

        case DxfModelPoint point:
          point.Location = P(point.Location);
          return true;

        default:
          if (DxfParser.IsAnnotation(entity))
          {
            return false;
          }

          throw new InvalidDataException($"A {entity.EntityType} cannot be flipped.");
      }
    }

    private static double Normalise(double value, double period)
    {
      double r = value % period;
      return r < 0 ? r + period : r;
    }

    // Keyed on the drawing, its write time and the flip, so an edited drawing is flipped afresh and the
    // same flip of the same drawing lands on the same file.
    private static string CacheDir(string sourcePath, PartFlip flip)
    {
      long ticks = 0;
      try
      {
        ticks = new FileInfo(sourcePath).LastWriteTimeUtc.Ticks;
      }
      catch (IOException)
      {
        // An unreadable timestamp only costs a fresh copy.
      }

      string key = sourcePath + "|" + ticks.ToString(CultureInfo.InvariantCulture) + "|" + ((int)flip).ToString(CultureInfo.InvariantCulture);
      string hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(key))).Substring(0, 16);
      return Path.Combine(Path.GetTempPath(), "sheetnest_flip", hash);
    }
  }
}
