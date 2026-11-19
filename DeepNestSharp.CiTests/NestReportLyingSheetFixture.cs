namespace DeepNestSharp.CiTests
{
  using System.Collections.Generic;
  using System.Linq;
  using DeepNestLib;
  using DeepNestLib.Placement;
  using DeepNestSharp.Reports;
  using FluentAssertions;
  using Xunit;

  /// <summary>
  /// The report draws every sheet lying, long side across the page, however the sheet stands (1.1.82, the
  /// user: "turn the picture to lie, whatever the real position of the sheet"). A standing sheet turns a
  /// quarter turn clockwise: its bottom edge becomes the picture's left.
  /// </summary>
  [Collection(UiLanguageCollection.Name)]
  public sealed class NestReportLyingSheetFixture
  {
    [Fact]
    public void AStandingSheetIsDrawnLyingWithItsBottomOnTheLeft()
    {
      var canvas = new ShapeCanvas();
      NestReportPdf.GenerateReport(() => canvas, Layout(100, 200, (0, 0)), 0, "mm", null);

      var sheet = canvas.Rects.Last(); // the drawing's frame is the last rectangle on the layout page
      sheet.W.Should().BeGreaterThan(sheet.H, "a 100 × 200 sheet lies in the report");
      sheet.W.Should().BeApproximately(2 * sheet.H, 1e-6);

      // The part in the standing sheet's bottom-left corner: at the picture's left edge, at its top.
      var part = canvas.Filled.Single();
      part.Min(p => p.X).Should().BeApproximately(sheet.X, 1e-6);
      part.Max(p => p.Y).Should().BeApproximately(sheet.Y + sheet.H, 1e-6);
    }

    [Fact]
    public void ALyingSheetIsDrawnAsItIs()
    {
      var canvas = new ShapeCanvas();
      NestReportPdf.GenerateReport(() => canvas, Layout(200, 100, (0, 0)), 0, "mm", null);

      var sheet = canvas.Rects.Last();
      sheet.W.Should().BeApproximately(2 * sheet.H, 1e-6);
      var part = canvas.Filled.Single();
      part.Min(p => p.X).Should().BeApproximately(sheet.X, 1e-6);
      part.Min(p => p.Y).Should().BeApproximately(sheet.Y, 1e-6, "the bottom-left corner stays bottom-left");
    }

    [Fact]
    public void TheNumberFollowsItsPartOnATurnedSheet()
    {
      var shape = new NoFitPolygon(new[] { (0.0, 0.0), (10.0, 0.0), (10.0, 10.0), (0.0, 10.0) }.Select(p => new SvgPoint(p.Item1, p.Item2)).ToList());
      var (x, y, size) = NestReportPdf.NumberLabel(shape, "1", ox: 0, oy: 0, scale: 1, turnedSheetWidth: 100);

      // The part's centre (5, 5) on a 100-wide standing sheet is drawn at (5, 95).
      (x + (0.556 * size / 2.0)).Should().BeApproximately(5, 1e-6);
      (y + (0.717 * size / 2.0)).Should().BeApproximately(95, 1e-6);
    }

    private static List<(ISheetPlacement Sheet, int Count, string Name)> Layout(int w, int h, (double X, double Y) at)
    {
      var part = new NoFitPolygon(new[] { (0.0, 0.0), (10.0, 0.0), (10.0, 10.0), (0.0, 10.0) }.Select(p => new SvgPoint(p.Item1, p.Item2)).ToList()) { Name = "plate.dxf" };
      var placements = new List<IPartPlacement> { new PartPlacement(part) { X = at.X, Y = at.Y, Source = 0 } };
      var sheet = new SheetPlacement(PlacementTypeEnum.Gravity, Sheet.NewSheet(1, w, h), placements, 0, false);
      return new List<(ISheetPlacement Sheet, int Count, string Name)> { (sheet, 1, "A") };
    }

    /// <summary>Keeps the rectangles and the filled polygons (the parts) the report draws.</summary>
    private sealed class ShapeCanvas : IReportCanvas
    {
      public List<(double X, double Y, double W, double H)> Rects { get; } = new();

      public List<List<(double X, double Y)>> Filled { get; } = new();

      public void SetFill(double r, double g, double b)
      {
      }

      public void FillRect(double x, double y, double w, double h, double r, double g, double b)
      {
      }

      public void Text(double x, double y, double size, bool bold, string text)
      {
      }

      public void Line(double x1, double y1, double x2, double y2, double width)
      {
      }

      public void Rect(double x, double y, double w, double h, double lineWidth) => this.Rects.Add((x, y, w, h));

      public void Polygon(IEnumerable<(double X, double Y)> pts, bool fill, bool holeFill = false, (byte R, byte G, byte B)? fillColour = null)
      {
        if (fill && !holeFill)
        {
          this.Filled.Add(pts.ToList());
        }
      }
    }
  }
}
