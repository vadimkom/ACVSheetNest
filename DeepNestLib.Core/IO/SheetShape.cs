namespace DeepNestLib.IO
{
  using System;
  using System.Collections.Generic;
  using System.IO;
  using System.Linq;
  using ClipperLib;

  /// <summary>
  /// A sheet that is not a rectangle, read from a drawing (DXF or CHF): an offcut of any shape. The largest
  /// closed contour is the sheet's edge; closed contours inside it are holes, where nothing may go. Anything
  /// lying outside the edge is not part of the sheet and is left out.
  /// </summary>
  public static class SheetShape
  {
    /// <summary>
    /// Reads the sheet's outline from <paramref name="path"/>, moved so the box round it starts at the origin.
    /// Throws when the file cannot be read or has no closed contour.
    /// </summary>
    public static INfp Load(string path)
    {
      var detail = new NestExecutionHelper().LoadRawDetail(new FileInfo(path));
      if (detail == null || !detail.TryConvertToNfp(0, out INfp raw) || raw?.Points == null || raw.Points.Length < 3)
      {
        throw new InvalidDataException("The drawing has no closed contour to take as the sheet's edge.");
      }

      return Normalise(raw);
    }

    /// <summary>The outline with its holes, the box round it moved to the origin; outside contours dropped.</summary>
    public static INfp Normalise(INfp raw)
    {
      double dx = -raw.Points.Min(p => p.X);
      double dy = -raw.Points.Min(p => p.Y);
      var outer = new NoFitPolygon(raw.Points.Select(p => new SvgPoint(p.X + dx, p.Y + dy)));
      const double scale = 1e4;
      var outerPath = DeepNestClipper.ScaleUpPath(outer.Points, scale);
      foreach (var child in raw.Children ?? new List<INfp>())
      {
        if (child?.Points == null || child.Points.Length < 3)
        {
          continue;
        }

        var moved = new NoFitPolygon(child.Points.Select(p => new SvgPoint(p.X + dx, p.Y + dy)));
        var probe = moved.Points[0];
        if (Clipper.PointInPolygon(new IntPoint((long)Math.Round(probe.X * scale), (long)Math.Round(probe.Y * scale)), outerPath) != 0)
        {
          outer.Children.Add(moved);
        }
      }

      return outer;
    }

    /// <summary>
    /// The drawing of a round sheet of <paramref name="diameter"/> (an offcut, a disc left from a bigger job),
    /// written once to <c>%LOCALAPPDATA%\SheetNest\sheets</c> and reused: <c>Ø500 mm.dxf</c>. Such a sheet is
    /// then a sheet from a drawing like any other.
    /// </summary>
    public static string RoundSheetFile(double diameter, string unit)
    {
      string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SheetNest", "sheets");
      Directory.CreateDirectory(folder);
      string size = diameter.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
      string path = Path.Combine(folder, $"Ø{size} {unit}.dxf");
      if (!File.Exists(path))
      {
        var dxf = new IxMilia.Dxf.DxfFile();
        dxf.Header.Version = IxMilia.Dxf.DxfAcadVersion.R2007;
        double r = diameter / 2.0;
        dxf.Entities.Add(new IxMilia.Dxf.Entities.DxfCircle(new IxMilia.Dxf.DxfPoint(r, r, 0), r) { Layer = "0" });
        dxf.Save(path, true);
      }

      return path;
    }

    /// <summary>Whether a sheet's outline is anything but a plain rectangle without holes.</summary>
    public static bool IsShaped(INfp sheet)
    {
      if (sheet?.Points == null)
      {
        return false;
      }

      if (sheet.Children != null && sheet.Children.Count > 0)
      {
        return true;
      }

      var pts = sheet.Points.ToList();
      if (pts.Count > 4 && Math.Abs(pts[0].X - pts[pts.Count - 1].X) < 1e-9 && Math.Abs(pts[0].Y - pts[pts.Count - 1].Y) < 1e-9)
      {
        pts.RemoveAt(pts.Count - 1);
      }

      if (pts.Count != 4)
      {
        return true;
      }

      double boxArea = (sheet.MaxX - sheet.MinX) * (sheet.MaxY - sheet.MinY);
      return Math.Abs(Math.Abs(sheet.Area) - boxArea) > 1e-6 * Math.Max(1, boxArea);
    }
  }
}
