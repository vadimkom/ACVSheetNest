namespace DeepNestSharp.Reports
{
  using System;
  using System.Collections.Generic;
  using System.Windows;
  using System.Windows.Controls;
  using System.Windows.Media;
  using System.Windows.Shapes;

  /// <summary>
  /// Unified drawing abstraction used by both the native PDF writer and the WPF on-screen preview.
  /// </summary>
  public interface IReportCanvas
  {
    void SetFill(double r, double g, double b);

    void FillRect(double x, double y, double w, double h, double r, double g, double b);

    void Text(double x, double y, double size, bool bold, string text);

    void Line(double x1, double y1, double x2, double y2, double width);

    void Rect(double x, double y, double w, double h, double lineWidth);

    void Polygon(IEnumerable<(double X, double Y)> pts, bool fill, bool holeFill = false, (byte R, byte G, byte B)? fillColour = null);
  }

  /// <summary>
  /// WPF implementation of <see cref="IReportCanvas"/> rendering A4 landscape vector pages (842 x 595 pt).
  /// </summary>
  public sealed class WpfPageCanvas : IReportCanvas
  {
    private const double PageH = 595.0;
    private const double PageW = 842.0;

    private readonly Canvas canvas;
    private Brush currentTextBrush = Brushes.Black;
    private static readonly FontFamily HelveticaFont = new FontFamily("Helvetica, Arial, Segoe UI, sans-serif");
    private static readonly Brush DefaultPartBrush = new SolidColorBrush(Color.FromRgb(181, 184, 189));
    private static readonly Brush StrokeDarkBrush = new SolidColorBrush(Color.FromRgb(38, 38, 38));
    private static readonly Brush LineGrayBrush = new SolidColorBrush(Color.FromRgb(89, 89, 89));

    public WpfPageCanvas()
    {
      this.canvas = new Canvas
      {
        Width = PageW,
        Height = PageH,
        Background = Brushes.White,
        ClipToBounds = true
      };
    }

    public Canvas Canvas => this.canvas;

    public void SetFill(double r, double g, double b)
    {
      byte rb = (byte)Math.Clamp((int)Math.Round(r * 255.0), 0, 255);
      byte gb = (byte)Math.Clamp((int)Math.Round(g * 255.0), 0, 255);
      byte bb = (byte)Math.Clamp((int)Math.Round(b * 255.0), 0, 255);
      this.currentTextBrush = new SolidColorBrush(Color.FromRgb(rb, gb, bb));
    }

    public void FillRect(double x, double y, double w, double h, double r, double g, double b)
    {
      byte rb = (byte)Math.Clamp((int)Math.Round(r * 255.0), 0, 255);
      byte gb = (byte)Math.Clamp((int)Math.Round(g * 255.0), 0, 255);
      byte bb = (byte)Math.Clamp((int)Math.Round(b * 255.0), 0, 255);

      var rect = new Rectangle
      {
        Width = Math.Max(0, w),
        Height = Math.Max(0, h),
        Fill = new SolidColorBrush(Color.FromRgb(rb, gb, bb))
      };
      Canvas.SetLeft(rect, x);
      Canvas.SetTop(rect, PageH - (y + h));
      this.canvas.Children.Add(rect);
    }

    public void Rect(double x, double y, double w, double h, double lineWidth)
    {
      var rect = new Rectangle
      {
        Width = Math.Max(0, w),
        Height = Math.Max(0, h),
        Stroke = StrokeDarkBrush,
        StrokeThickness = Math.Max(0.5, lineWidth)
      };
      Canvas.SetLeft(rect, x);
      Canvas.SetTop(rect, PageH - (y + h));
      this.canvas.Children.Add(rect);
    }

    public void Line(double x1, double y1, double x2, double y2, double width)
    {
      var line = new Line
      {
        X1 = x1,
        Y1 = PageH - y1,
        X2 = x2,
        Y2 = PageH - y2,
        Stroke = LineGrayBrush,
        StrokeThickness = Math.Max(0.5, width)
      };
      this.canvas.Children.Add(line);
    }

    public void Text(double x, double y, double size, bool bold, string text)
    {
      var tb = new TextBlock
      {
        Text = text,
        FontFamily = HelveticaFont,
        FontSize = size,
        FontWeight = bold ? FontWeights.Bold : FontWeights.Normal,
        Foreground = this.currentTextBrush
      };
      Canvas.SetLeft(tb, x);
      Canvas.SetTop(tb, PageH - y - (size * 0.98));
      this.canvas.Children.Add(tb);
    }

    public void Polygon(IEnumerable<(double X, double Y)> pts, bool fill, bool holeFill = false, (byte R, byte G, byte B)? fillColour = null)
    {
      var geom = new StreamGeometry();
      using (var ctx = geom.Open())
      {
        bool first = true;
        foreach (var (px, py) in pts)
        {
          var pt = new Point(px, PageH - py);
          if (first)
          {
            ctx.BeginFigure(pt, isFilled: true, isClosed: true);
            first = false;
          }
          else
          {
            ctx.LineTo(pt, isStroked: true, isSmoothJoin: false);
          }
        }
      }
      geom.Freeze();

      Brush fillBrush;
      if (fill)
      {
        fillBrush = fillColour.HasValue
          ? new SolidColorBrush(Color.FromRgb(fillColour.Value.R, fillColour.Value.G, fillColour.Value.B))
          : DefaultPartBrush;
      }
      else if (holeFill)
      {
        fillBrush = Brushes.White;
      }
      else
      {
        fillBrush = Brushes.Transparent;
      }

      var path = new Path
      {
        Data = geom,
        Fill = fillBrush,
        Stroke = StrokeDarkBrush,
        StrokeThickness = 0.5
      };
      this.canvas.Children.Add(path);
    }

    public FrameworkElement GetPageView()
    {
      var border = new Border
      {
        Width = PageW,
        Height = PageH,
        Background = Brushes.White,
        BorderBrush = new SolidColorBrush(Color.FromRgb(80, 80, 80)),
        BorderThickness = new Thickness(1),
        Margin = new Thickness(0, 10, 0, 18),
        Effect = new System.Windows.Media.Effects.DropShadowEffect
        {
          BlurRadius = 14,
          ShadowDepth = 3,
          Opacity = 0.35,
          Direction = 270
        },
        Child = this.canvas
      };
      return border;
    }
  }
}
