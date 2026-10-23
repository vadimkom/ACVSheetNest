namespace DeepNestSharp.Ui.Converters
{
  using System;
  using System.Collections.Generic;
  using System.Globalization;
  using System.IO;
  using System.Windows;
  using System.Windows.Data;
  using System.Windows.Media;
  using DeepNestLib;
  using DeepNestLib.IO;

  /// <summary>
  /// The small picture of a stock row: the sheet in proportion, and for a sheet read from a drawing its real
  /// outline and holes, so an odd-shaped offcut can be told apart from the rectangle round it before nesting.
  /// Bound to ShapePath, Width and Height. Drawings are cached by path and time written.
  /// </summary>
  public class SheetPreviewConverter : IMultiValueConverter
  {
    private static readonly Dictionary<(string Path, DateTime Written), ImageSource> Cache = new Dictionary<(string, DateTime), ImageSource>();
    private static readonly Brush Fill = Frozen(new SolidColorBrush(Color.FromRgb(0xE4, 0xE8, 0xEC)));
    private static readonly Brush Stroke = Frozen(new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44)));

    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
      string path = values.Length > 0 ? values[0] as string : null;
      int w = values.Length > 1 && values[1] is int width ? width : 0;
      int h = values.Length > 2 && values[2] is int height ? height : 0;

      if (!string.IsNullOrEmpty(path))
      {
        return FromDrawing(path);
      }

      if (w <= 0 || h <= 0)
      {
        return null;
      }

      return Build(new RectangleGeometry(new Rect(0, 0, w, h)));
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) => null;

    private static ImageSource FromDrawing(string path)
    {
      DateTime written;
      try
      {
        if (!File.Exists(path))
        {
          return null;
        }

        written = File.GetLastWriteTimeUtc(path);
      }
      catch (Exception)
      {
        return null;
      }

      if (Cache.TryGetValue((path, written), out var cached))
      {
        return cached;
      }

      ImageSource image = null;
      try
      {
        // Bounded like the part thumbnails: a locked file is retried for seconds, and a picture is not worth
        // freezing the window for. On timeout nothing is cached, so it can still appear on a later refresh.
        var load = System.Threading.Tasks.Task.Run(() => SheetShape.Load(path));
        if (!load.Wait(1500))
        {
          return null;
        }

        INfp shape = load.GetAwaiter().GetResult();
        var g = new StreamGeometry { FillRule = FillRule.EvenOdd };
        using (var ctx = g.Open())
        {
          AppendContour(ctx, shape);
          foreach (var hole in shape.Children ?? new List<INfp>())
          {
            AppendContour(ctx, hole);
          }
        }

        g.Freeze();
        image = Build(g);
      }
      catch (Exception)
      {
        image = null;
      }

      Cache[(path, written)] = image;
      return image;
    }

    private static ImageSource Build(Geometry g)
    {
      if (g.Bounds.IsEmpty || g.Bounds.Width <= 0 || g.Bounds.Height <= 0)
      {
        return null;
      }

      var pen = new Pen(Stroke, Math.Max(g.Bounds.Width, g.Bounds.Height) / 50.0);
      pen.Freeze();
      var image = new DrawingImage(new GeometryDrawing(Fill, pen, g));
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

    private static Brush Frozen(SolidColorBrush brush)
    {
      brush.Freeze();
      return brush;
    }
  }
}
