namespace DeepNestSharp.CiTests
{
  using System.Collections.Generic;
  using DeepNestLib;
  using DeepNestSharp.Ui.Views;
  using FluentAssertions;
  using Xunit;

  /// <summary>
  /// Edit Part warns about a DXF that is really a whole nested sheet. A panel with an ornament cut into it
  /// has the same frame and the same share of cut-outs, and was being warned about at the laser.
  /// </summary>
  public class NestedSheetWarningFixture
  {
    [Fact]
    public void APanelWithAnOrnamentIsNotANestedSheet()
    {
      // A 2000 x 1000 panel with a grid of large cut-outs, nearly half of its area, none holding anything.
      var panel = Rect(0, 0, 2000, 1000);
      for (int i = 0; i < 8; i++)
      {
        for (int j = 0; j < 4; j++)
        {
          panel.Children.Add(Rect(50 + (i * 240), 50 + (j * 235), 180, 180));
        }
      }

      EditPartWindow.LooksLikeNestedSheet(panel).Should().BeFalse();
    }

    [Fact]
    public void PartsWithHolesInsideAFrameAreANestedSheet()
    {
      // The same frame holding parts, each with its own hole: what an exported nest looks like.
      var sheet = Rect(0, 0, 2000, 1000);
      for (int i = 0; i < 8; i++)
      {
        for (int j = 0; j < 4; j++)
        {
          sheet.Children.Add(Rect(50 + (i * 240), 50 + (j * 235), 180, 180));
          sheet.Children.Add(Rect(110 + (i * 240), 110 + (j * 235), 60, 60));
        }
      }

      EditPartWindow.LooksLikeNestedSheet(sheet).Should().BeTrue();
    }

    [Fact]
    public void AContourIsHeldOnlyWhenItLiesInsideAnother()
    {
      var contours = new List<INfp> { Rect(0, 0, 100, 100), Rect(10, 10, 20, 20), Rect(200, 0, 100, 100) };
      EditPartWindow.ContoursHoldingContours(contours).Should().Be(1);
    }

    private static NoFitPolygon Rect(double x, double y, double w, double h)
    {
      var r = new NoFitPolygon();
      r.AddPoint(new SvgPoint(x, y));
      r.AddPoint(new SvgPoint(x + w, y));
      r.AddPoint(new SvgPoint(x + w, y + h));
      r.AddPoint(new SvgPoint(x, y + h));
      r.Children = new List<INfp>();
      return r;
    }
  }
}
