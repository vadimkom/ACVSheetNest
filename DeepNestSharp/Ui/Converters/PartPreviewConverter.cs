namespace DeepNestSharp.Ui.Converters
{
  using System;
  using System.Collections.Generic;
  using System.Globalization;
  using System.Windows;
  using System.Windows.Data;
  using System.Windows.Media;
  using DeepNestLib;
  using DeepNestLib.IO;
  using DeepNestLib.NestProject;

  /// <summary>
  /// Builds a small vector <see cref="DrawingImage"/> (outer outline + holes, EvenOdd) from a part's
  /// source DXF, for the thumbnail shown next to each part. A Geometry can't render itself, so it's
  /// wrapped in a GeometryDrawing/DrawingImage and shown via an &lt;Image&gt; (robust auto-scaling).
  /// Cached per file path.
  /// </summary>
  public class PartPreviewConverter : IValueConverter
  {
    private static readonly Dictionary<string, ImageSource> Cache = new Dictionary<string, ImageSource>();

    /// <summary>
    /// Colour per part file, so each thumbnail is drawn in the colour that part has on the sheet — the parts
    /// list IS the legend. Set by the window that owns the part list, because a converter is created in XAML
    /// and only ever receives the bound value.
    /// </summary>
    private static IReadOnlyDictionary<string, (byte R, byte G, byte B)> colours;

    /// <summary>Hands the thumbnails the job's part colours. The whole cache goes: it is keyed by path, and
    /// the image now depends on the colour too.</summary>
    /// <summary>The drawings changed on disk: every thumbnail is drawn again from its file.</summary>
    public static void Forget() => Cache.Clear();

    public static void SetColours(IReadOnlyDictionary<string, (byte R, byte G, byte B)> partColours)
    {
      colours = partColours;
      Cache.Clear();
    }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
      string path = (value as IDetailLoadInfo)?.Path ?? value as string;
      if (string.IsNullOrEmpty(path))
      {
        return null;
      }

      if (!DxfFileExtensions.IsDxf(path))
      {
        return null;
      }

      string dxfPath = path;

      if (Cache.TryGetValue(path, out ImageSource cached))
      {
        return cached;
      }

      if (!TryLoad(dxfPath, out INfp nfp, out bool timedOut))
      {
        // On timeout skip the cache, so the thumbnail can still appear on a later refresh once the file is free.
        if (!timedOut)
        {
          Cache[path] = null;
        }

        return null;
      }

      var image = Build(path, nfp, PartColors.For(colours, path), 60.0);
      Cache[path] = image;
      return image;
    }

    /// <summary>The part drawn to be looked at large: the same picture as the thumbnail with a thin outline
    /// (1/400 of the part's size instead of 1/60), so small holes stay holes. Not cached.</summary>
    public static ImageSource Large(IDetailLoadInfo part)
    {
      string path = part?.Path;
      if (string.IsNullOrEmpty(path) || !DxfFileExtensions.IsDxf(path))
      {
        return null;
      }

      // The colour picker redraws it on every move of the colour canvas: read the drawing once per file version.
      DateTime stamp;
      try
      {
        stamp = System.IO.File.GetLastWriteTimeUtc(path);
      }
      catch
      {
        return null;
      }

      INfp nfp;
      if (lastLarge.Path == path && lastLarge.Stamp == stamp && lastLarge.Nfp != null)
      {
        nfp = lastLarge.Nfp;
      }
      else
      {
        if (!TryLoad(path, out nfp, out _))
        {
          return null;
        }

        lastLarge = (path, stamp, nfp);
      }

      return Build(path, nfp, PartColors.For(colours, path), 400.0);
    }

    private static (string Path, DateTime Stamp, INfp Nfp) lastLarge;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;

    private static bool TryLoad(string dxfPath, out INfp nfp, out bool timedOut)
    {
      nfp = null;
      timedOut = false;
      try
      {
        // Task.Run so a locked-file retry (await Task.Delay) can't deadlock the UI thread we're blocking
        // on. The wait is BOUNDED: the parser retries a locked file for seconds, and a picture isn't
        // worth freezing the UI that long.
        var load = System.Threading.Tasks.Task.Run(() => DxfParser.LoadDxfFile(dxfPath));
        if (!load.Wait(1500))
        {
          timedOut = true;
          return false;
        }

        var raw = load.GetAwaiter().GetResult();
        return raw != null && raw.TryConvertToNfp(0, out nfp);
      }
      catch
      {
        nfp = null;
        return false;
      }
    }

    /// <param name="penDivisor">The outline is the part's larger side over this.</param>
    private static ImageSource Build(string path, INfp nfp, (byte R, byte G, byte B) colour, double penDivisor)
    {
      var g = new StreamGeometry { FillRule = FillRule.EvenOdd };
      using (var ctx = g.Open())
      {
        AppendContour(ctx, nfp);
        if (nfp.Children != null)
        {
          foreach (var child in nfp.Children)
          {
            AppendContour(ctx, child);
          }
        }
      }

      g.Freeze();
      if (g.Bounds.IsEmpty || g.Bounds.Width <= 0 || g.Bounds.Height <= 0)
      {
        return null;
      }

      double dim = Math.Max(g.Bounds.Width, g.Bounds.Height);
      var pen = new Pen(new SolidColorBrush(Color.FromRgb(0x22, 0x22, 0x22)), dim / penDivisor);
      pen.Freeze();
      // The part's own colour, opaque, exactly as the viewer paints it on the sheet — this thumbnail is how
      // the operator maps a colour back to a part name.
      var fill = new SolidColorBrush(Color.FromRgb(colour.R, colour.G, colour.B));
      fill.Freeze();

      var drawing = new DrawingGroup();
      drawing.Children.Add(new GeometryDrawing(fill, pen, g));

      // Bend lines, dashed: marks, not cuts.
      var bends = DxfBendLines.Of(path);
      if (bends.Count > 0)
      {
        var bendGeometry = new StreamGeometry();
        using (var ctx = bendGeometry.Open())
        {
          foreach (var line in bends)
          {
            ctx.BeginFigure(new Point(line[0].X, -line[0].Y), false, false);
            var rest = new List<Point>(line.Count - 1);
            for (int i = 1; i < line.Count; i++)
            {
              rest.Add(new Point(line[i].X, -line[i].Y));
            }

            ctx.PolyLineTo(rest, true, false);
          }
        }

        bendGeometry.Freeze();
        var bendPen = new Pen(pen.Brush, pen.Thickness * 0.75) { DashStyle = new DashStyle(new[] { 4.0, 3.0 }, 0) };
        bendPen.Freeze();
        drawing.Children.Add(new GeometryDrawing(null, bendPen, bendGeometry));
      }

      drawing.Freeze();
      var image = new DrawingImage(drawing);
      image.Freeze();
      return image;
    }

    private static void AppendContour(StreamGeometryContext ctx, INfp c)
    {
      if (c == null || c.Length < 2)
      {
        return;
      }

      ctx.BeginFigure(new Point(c[0].X, -c[0].Y), true, true); // -Y: DXF up -> screen down
      var pts = new List<Point>(c.Length - 1);
      for (int i = 1; i < c.Length; i++)
      {
        pts.Add(new Point(c[i].X, -c[i].Y));
      }

      ctx.PolyLineTo(pts, true, true);
    }
  }
}
