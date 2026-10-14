namespace DeepNestSharp.CiTests
{
  using System;
  using System.Collections.Generic;
  using System.IO;
  using System.Linq;
  using System.Text;
  using DeepNestLib;
  using DeepNestLib.Placement;
  using DeepNestSharp.Reports;
  using FluentAssertions;
  using Xunit;

  /// <summary>
  /// The report numbers every distinct part from 1 and prints that number on each of its copies in the
  /// sheet drawing, so an operator can read a number off a cut piece and find its row. The number is
  /// therefore only worth anything if it means the SAME part everywhere it appears.
  /// </summary>
  [Collection(UiLanguageCollection.Name)]
  public sealed class NestReportPartNumbersFixture : IDisposable
  {
    // The labels and units asserted below are the English ones; the report prints in the UI language.
    private readonly UiLanguageScope english = new UiLanguageScope(DeepNestSharp.Ui.Localization.LangEnglish);

    public void Dispose() => this.english.Dispose();

    [Fact]
    public void NumbersRunFromOneAndFollowTheOrderTheTableIsPrintedIn()
    {
      var layouts = new List<(ISheetPlacement Sheet, int Count, string Name)>
      {
        (SheetOf(("few.dxf", 1), ("many.dxf", 3), ("some.dxf", 2)), 1, "A"),
      };

      var totals = NestReportPdf.NumberedPartTotals(layouts);

      totals.Select(t => t.Number).Should().Equal(new[] { 1, 2, 3 }, "the table reads 1, 2, 3 down the page");
      totals.Select(t => t.File).Should().Equal(
        new[] { "many.dxf", "some.dxf", "few.dxf" },
        "the rows stay ordered by quantity, so the numbers follow that order");
      totals.Select(t => t.Qty).Should().Equal(new[] { 3, 2, 1 });
    }

    /// <summary>
    /// The one that makes the feature work. A part cut on two different layouts is one part with one
    /// mark; numbering each page on its own would print a 1 on layout A and a 2 on layout B for the
    /// same drawing, and every mark made from the report would be wrong.
    /// </summary>
    [Fact]
    public void APartOnTwoLayoutsGetsOneNumberAndItsQuantityAddsUp()
    {
      var layouts = new List<(ISheetPlacement Sheet, int Count, string Name)>
      {
        (SheetOf(("bracket.dxf", 2)), 1, "A"),
        (SheetOf(("bracket.dxf", 1), ("plate.dxf", 1)), 1, "B"),
      };

      var totals = NestReportPdf.NumberedPartTotals(layouts);

      totals.Should().HaveCount(2, "two drawings, whatever they are spread across");
      var bracket = totals.Single(t => t.File == "bracket.dxf");
      bracket.Number.Should().Be(1);
      bracket.Qty.Should().Be(3, "two on the first layout and one on the second");
    }

    /// <summary>A layout cut several times produces its parts several times over.</summary>
    [Fact]
    public void CuttingALayoutMoreThanOnceMultipliesTheQuantityButNotTheNumbers()
    {
      var layouts = new List<(ISheetPlacement Sheet, int Count, string Name)>
      {
        (SheetOf(("bracket.dxf", 2)), 5, "A"),
      };

      var totals = NestReportPdf.NumberedPartTotals(layouts);

      totals.Should().ContainSingle().Which.Qty.Should().Be(10);
      totals[0].Number.Should().Be(1);
    }

    /// <summary>
    /// A mirrored copy is the other hand: a different physical part, cut from the same drawing, that
    /// takes its own mark. It has always been its own row in the report and so it earns its own number.
    /// </summary>
    [Fact]
    public void AMirroredCopyIsNumberedSeparatelyFromItsOriginal()
    {
      var placements = new List<IPartPlacement>
      {
        Part("bracket.dxf", 0, 0),
        Part("bracket.dxf", 20, 0),
        Mirrored(Part("bracket.dxf", 40, 0)),
      };
      var layouts = new List<(ISheetPlacement Sheet, int Count, string Name)>
      {
        (Sheet(placements), 1, "A"),
      };

      var totals = NestReportPdf.NumberedPartTotals(layouts);

      totals.Should().HaveCount(2);
      totals.Select(t => t.File).Should().BeEquivalentTo(new[] { "bracket.dxf", "bracket.dxf (mirrored)" });
      totals.Select(t => t.Number).Should().Equal(new[] { 1, 2 });
    }

    /// <summary>
    /// The number is anchored on the AREA CENTROID, not the middle of the bounding box. This L is the
    /// case that separates them: its box centre sits in the notch, off the material and on top of
    /// whatever is nested in there, while its centroid is on the part.
    /// </summary>
    [Fact]
    public void OnAnLTheNumberLandsOnTheMaterialAndNotInTheNotch()
    {
      INfp shape = Poly("angle.dxf", (0, 0), (10, 0), (10, 4), (4, 4), (4, 10), (0, 10));

      var (x, y, size) = NestReportPdf.NumberLabel(shape, "7", ox: 0, oy: 0, scale: 1);

      // Recover the centre the label was built around: Text() is given the baseline's left end.
      double centreX = x + (0.556 * size * "7".Length / 2.0);
      double centreY = y + (0.717 * size / 2.0);

      Inside(shape, centreX, centreY).Should().BeTrue("the number belongs on the part it names");
      Inside(shape, 5, 5).Should().BeFalse("and the bounding-box centre, which is what this replaces, is not");
      centreX.Should().BeApproximately(3.875, 1e-6);
      centreY.Should().BeApproximately(3.875, 1e-6);
    }

    [Theory]
    [InlineData(1, 4.0)]       // a tiny part still gets a number that survives printing
    [InlineData(100, 12.0)]    // and a big one does not get a silly one
    [InlineData(20, 8.4)]      // in between, the number follows the part
    public void TheNumberIsSizedToThePartWithinBounds(double side, double expected)
    {
      INfp shape = Poly("square.dxf", (0, 0), (side, 0), (side, side), (0, side));

      var (_, _, size) = NestReportPdf.NumberLabel(shape, "1", ox: 0, oy: 0, scale: 1);

      size.Should().BeApproximately(expected, 1e-9);
    }

    /// <summary>
    /// End to end, on the bytes that reach the paper. The writer leaves its content streams
    /// uncompressed, so the drawing's labels can be counted: they are the only bold text the report
    /// sets at this size, and there must be exactly one per placed part on every layout page.
    /// </summary>
    [Fact]
    public void EveryPlacedPartCarriesItsNumberOnTheDrawing()
    {
      // Small parts on a big sheet so the label size clamps to its floor of 4, which no table uses.
      var layouts = new List<(ISheetPlacement Sheet, int Count, string Name)>
      {
        (SheetOf(1.0, ("bracket.dxf", 3), ("plate.dxf", 1)), 2, "A"),
        (SheetOf(1.0, ("bracket.dxf", 2)), 1, "B"),
      };

      string path = Path.Combine(Path.GetTempPath(), $"sheetnest-report-{Guid.NewGuid():N}.pdf");
      try
      {
        NestReportPdf.Write(path, layouts, unplacedCount: 0);

        string pdf = Encoding.GetEncoding(28591).GetString(File.ReadAllBytes(path));

        Occurrences(pdf, "/F2 4 Tf").Should().Be(
          6,
          "four parts drawn on the first layout page and two on the second, each with its number");

        // Text reaches the PDF as glyph ids of the embedded font, so what it SAYS is read off the pages as
        // they are drawn, which is the same call sequence the PDF writer receives.
        var texts = RecordedTexts(layouts);
        texts.Should().Contain("No.", "the table needs the column the drawing refers to");
        texts.Should().Contain("Box Area");
        texts.Should().Contain("Cut Length");
        texts.Should().Contain("1").And.Contain("2");
      }
      finally
      {
        File.Delete(path);
      }
    }

    [Fact]
    public void ComputeBoundingBoxCalculatesCorrectDimensionsAndArea()
    {
      // 20 x 10 rectangle
      INfp rect = Poly("rect.dxf", (0, 0), (20, 0), (20, 10), (0, 10));
      var (w, h, area) = NestReportPdf.ComputeBoundingBox(rect);
      w.Should().BeApproximately(20.0, 1e-6);
      h.Should().BeApproximately(10.0, 1e-6);
      area.Should().BeApproximately(200.0, 1e-6);

      // Rotated rectangle (rotated by 45 degrees)
      INfp rotated = rect.Rotate(45);
      var (rw, rh, rarea) = NestReportPdf.ComputeBoundingBox(rotated);
      rw.Should().BeApproximately(20.0, 1e-4);
      rh.Should().BeApproximately(10.0, 1e-4);
      rarea.Should().BeApproximately(200.0, 1e-4);

      // L-shape with legs 10 and 10, notch 6x6
      INfp lShape = Poly("angle.dxf", (0, 0), (10, 0), (10, 4), (4, 4), (4, 10), (0, 10));
      var (lw, lh, larea) = NestReportPdf.ComputeBoundingBox(lShape);
      lw.Should().BeApproximately(10.0, 1e-6);
      lh.Should().BeApproximately(10.0, 1e-6);
      larea.Should().BeApproximately(100.0, 1e-6);
    }

    [Fact]
    public void LaserCutLengthIncludesOuterContourAndHoles()
    {
      // Square 10 x 10 has perimeter 40
      INfp outer = Poly("square.dxf", (0, 0), (10, 0), (10, 10), (0, 10));
      NestReportPdf.LaserCutLength(outer).Should().BeApproximately(40.0, 1e-6);

      // Add a 2 x 2 square hole (perimeter 8)
      INfp hole = Poly("hole.dxf", (4, 4), (6, 4), (6, 6), (4, 6));
      outer.Children.Add(hole);

      // Total laser cut length should be 40 + 8 = 48
      NestReportPdf.LaserCutLength(outer).Should().BeApproximately(48.0, 1e-6);
    }

    [Fact]
    public void NumberedPartTotalsPopulatesBoxAreaAndCutLength()
    {
      var layouts = new List<(ISheetPlacement Sheet, int Count, string Name)>
      {
        (SheetOf(10.0, ("bracket.dxf", 2)), 1, "A"),
      };

      var totals = NestReportPdf.NumberedPartTotals(layouts);

      totals.Should().ContainSingle();
      totals[0].BoxArea.Should().BeApproximately(100.0, 1e-6);
      totals[0].CutLength.Should().BeApproximately(40.0, 1e-6);
    }

    [Fact]
    public void FormatStockAreaProducesMetricSquareMetersWhenUnitsMm()
    {
      NestReportPdf.FormatStockArea(2_000_000, "mm").Should().Be("2 m²");
      NestReportPdf.FormatStockArea(12_500_000, "mm").Should().Be("12.5 m²");
      NestReportPdf.FormatStockArea(288, "in").Should().Be("2 sq ft");
    }

    [Fact]
    public void FormatAreaProducesSquareMetersWhenUnitsMm()
    {
      NestReportPdf.FormatArea(244_569, "mm").Should().Be("0.245 m²");
      NestReportPdf.FormatArea(79_569, "mm").Should().Be("0.080 m²");
      NestReportPdf.FormatArea(1_500, "mm").Should().Be("0.0015 m²");
      NestReportPdf.FormatArea(200, "mm").Should().Be("0.0002 m²");
      NestReportPdf.FormatArea(50, "in").Should().Be("50 sq in");
    }

    [Fact]
    public void PdfReportUsesA4LandscapeAndMetricUnitsWhenConfigured()
    {
      var layouts = new List<(ISheetPlacement Sheet, int Count, string Name)>
      {
        (SheetOf(10.0, ("partA.dxf", 2)), 1, "Layout1"),
      };

      string path = Path.Combine(Path.GetTempPath(), $"sheetnest-report-a4-{Guid.NewGuid():N}.pdf");
      try
      {
        NestReportPdf.Write(path, layouts, unplacedCount: 0, units: "mm");

        string pdf = Encoding.GetEncoding(28591).GetString(File.ReadAllBytes(path));

        pdf.Should().Contain("/MediaBox [0 0 842 595]");
        string text = string.Join("\n", RecordedTexts(layouts, "mm"));
        text.Should().Contain("m²");
        text.Should().NotContain("sq ft");
        text.Should().NotContain("sq mm");
      }
      finally
      {
        File.Delete(path);
      }
    }

    /// <summary>
    /// Cyrillic part names and Russian labels used to come out of the PDF as question marks: the report
    /// set everything in standard Helvetica, which covers Latin-1 only. The text now goes through an
    /// embedded Unicode font, and the ToUnicode map is what lets a viewer copy and search it.
    /// </summary>
    [Fact]
    public void CyrillicTextIsEmbeddedAsUnicodeRatherThanQuestionMarks()
    {
      if (PdfTrueTypeFont.LoadSystemArial().Regular == null)
      {
        return; // no Arial on this machine: the writer falls back to Helvetica, which this cannot test
      }

      using var russian = new UiLanguageScope(DeepNestSharp.Ui.Localization.LangRussian);
      var layouts = new List<(ISheetPlacement Sheet, int Count, string Name)>
      {
        (SheetOf(10.0, ("кронштейн.dxf", 2)), 1, "A"),
      };

      string pdf = Encoding.GetEncoding(28591).GetString(NestReportPdf.BuildPdfBytes(layouts, unplacedCount: 0, units: "mm"));

      pdf.Should().Contain("/Encoding /Identity-H");
      pdf.Should().Contain("/FontFile2");
      pdf.Should().Contain("<043A>", "'к' of the part name must map back to Unicode");
      pdf.Should().NotContain(") Tj", "no text may be written as a Latin-1 string any more");
      pdf.Should().Contain("> Tj");
      RecordedTexts(layouts, "mm").Should().Contain(DeepNestSharp.Ui.Localization.Get("Rep_BoxArea"));
    }

    /// <summary>
    /// The report preview and printing passed the unit already translated ("мм"), which the report did
    /// not recognise, so a Russian operator's report came out in square inches and square feet.
    /// </summary>
    [Fact]
    public void TheTranslatedMillimetreWordStillMeansMillimetres()
    {
      using var russian = new UiLanguageScope(DeepNestSharp.Ui.Localization.LangRussian);
      var layouts = new List<(ISheetPlacement Sheet, int Count, string Name)>
      {
        (SheetOf(100.0, ("plate.dxf", 2)), 1, "A"),
      };

      string text = string.Join("\n", RecordedTexts(layouts, "мм"));

      text.Should().Contain("м²").And.Contain("мм");
      text.Should().NotContain("кв. фут").And.NotContain("кв. in").And.NotContain(" in");
    }

    /// <summary>
    /// At the laser the parts list is read down one column, names in full. A long list used to be split in
    /// two columns that cut every name to 28 characters, and whatever did not fit was dropped as "and N more".
    /// Now what does not fit goes on over the next pages, the footers count those pages, and the cutting plan
    /// sends the reader to the page the layout really starts on.
    /// </summary>
    [Fact]
    public void ALongPartsListIsOneColumnOfWholeNamesCarriedOnOverleaf()
    {
      var names = Enumerable.Range(1, 45)
        .Select(i => $"Flat-Pattern - elbow^Tr_{i:00}_300x300_250x200_with_a_long_tail.dxf")
        .ToArray();
      var layouts = new List<(ISheetPlacement Sheet, int Count, string Name)>
      {
        (SheetOf(names.Select(n => (n, 1)).ToArray()), 2, "A"),
      };

      var pages = new List<List<(double X, string Text)>>();
      NestReportPdf.GenerateReport(
        () =>
        {
          var page = new List<(double X, string Text)>();
          pages.Add(page);
          return new PositionCanvas(page);
        },
        layouts,
        0,
        "mm",
        null);

      var all = pages.SelectMany(p => p).ToList();
      foreach (var name in names)
      {
        all.Count(t => t.Text == name).Should().Be(2, "{0} is listed whole in the part totals and on its layout", name);
      }

      all.Where(t => names.Contains(t.Text)).Select(t => t.X).Distinct().Should().ContainSingle("the list is one column");
      all.Should().NotContain(t => t.Text.StartsWith("... and ", StringComparison.Ordinal), "nothing is dropped");

      pages.Count.Should().BeGreaterThan(2, "45 rows fit neither under the plan nor under the drawing");
      for (int i = 0; i < pages.Count; i++)
      {
        pages[i].Select(t => t.Text).Should().Contain($"Page {i + 1} of {pages.Count}");
      }

      int layoutPage = pages.FindIndex(p => p.Any(t => t.Text.StartsWith("Layout 1 of 1", StringComparison.Ordinal))) + 1;
      layoutPage.Should().BeGreaterThan(2, "the part totals ran over onto a page of their own first");
      var plan = pages[0].Select(t => t.Text).ToList();
      plan[plan.IndexOf("Layout 1") + 1].Should().Be(layoutPage.ToString(), "the cutting plan points at the layout's real page");
      pages.Skip(layoutPage).Should().Contain(p => p.Any(t => t.Text == "Layout 1: Parts on this layout (continued)"));
    }

    /// <summary>
    /// On a shop PC .NET's native zlib failed to load and the PDF could not be written at all. The font is
    /// then embedded uncompressed rather than losing the report.
    /// </summary>
    [Fact]
    public void WithoutZlibTheFontGoesInUncompressedRatherThanFailingTheReport()
    {
      byte[] font = Encoding.ASCII.GetBytes("pretend this is a font program");

      string stream = Encoding.ASCII.GetString(PdfTrueTypeFont.FontFileStream(
        font,
        _ => throw new IOException("The underlying compression routine could not be loaded correctly.")));

      stream.Should().NotContain("/FlateDecode");
      stream.Should().Contain($"/Length {font.Length} /Length1 {font.Length}");
      stream.Should().Contain("pretend this is a font program");
      Encoding.ASCII.GetString(PdfTrueTypeFont.FontFileStream(font)).Should().Contain("/FlateDecode", "zlib is used when it loads");
    }

    [Fact]
    public void AJobWithAMaterialSaysWhatItIsWhatItWeighsAndHowMuchCutting()
    {
      var layouts = new List<(ISheetPlacement Sheet, int Count, string Name)>
      {
        (SheetOf(("bracket.dxf", 2)), 1, "A"),
      };

      var texts = new List<string>();
      NestReportPdf.GenerateReport(() => new RecordingCanvas(texts), layouts, 0, "mm", null, new ReportMaterial("Galvanised steel", 1.5, 7.85));

      texts.Should().Contain("Material").And.Contain("Galvanised steel, 1.5 mm");
      texts.Should().Contain("Cut length").And.Contain("Pierces").And.Contain("2", "two parts without holes are pierced twice");
      texts.Should().Contain("Sheet weight").And.Contain("Part weight");
      texts.Should().Contain(t => t.EndsWith(" kg", StringComparison.Ordinal));
    }

    [Fact]
    public void AJobWithoutAMaterialStillSaysHowMuchCuttingButNoWeight()
    {
      var layouts = new List<(ISheetPlacement Sheet, int Count, string Name)>
      {
        (SheetOf(("bracket.dxf", 2)), 1, "A"),
      };

      var texts = RecordedTexts(layouts, "mm");

      texts.Should().Contain("Cut length").And.Contain("Pierces");
      texts.Should().NotContain("Sheet weight").And.NotContain("Material");
    }

    private static List<string> RecordedTexts(
      IReadOnlyList<(ISheetPlacement Sheet, int Count, string Name)> layouts,
      string units = "in")
    {
      var texts = new List<string>();
      NestReportPdf.GenerateReport(() => new RecordingCanvas(texts), layouts, 0, units, null);
      return texts;
    }

    private static int Occurrences(string haystack, string needle)
    {
      int count = 0;
      for (int i = haystack.IndexOf(needle, StringComparison.Ordinal); i >= 0;
           i = haystack.IndexOf(needle, i + needle.Length, StringComparison.Ordinal))
      {
        count++;
      }

      return count;
    }

    /// <summary>Ray casting, so the L test asserts what it says it does rather than trusting a number.</summary>
    private static bool Inside(INfp poly, double x, double y)
    {
      var pts = poly.Points;
      bool inside = false;
      for (int i = 0, j = pts.Length - 1; i < pts.Length; j = i++)
      {
        if (((pts[i].Y > y) != (pts[j].Y > y))
          && (x < (((pts[j].X - pts[i].X) * (y - pts[i].Y)) / (pts[j].Y - pts[i].Y)) + pts[i].X))
        {
          inside = !inside;
        }
      }

      return inside;
    }

    private static INfp Poly(string name, params (double X, double Y)[] points)
      => new NoFitPolygon(points.Select(p => new SvgPoint(p.X, p.Y)).ToList()) { Name = name };

    private static IPartPlacement Part(string name, double x, double y)
      => new PartPlacement(Poly(name, (0, 0), (10, 0), (10, 10), (0, 10))) { X = x, Y = y, Source = 0 };

    private static IPartPlacement Part(string name, double x, double y, double side)
      => new PartPlacement(Poly(name, (0, 0), (side, 0), (side, side), (0, side))) { X = x, Y = y, Source = 0 };

    private static IPartPlacement Mirrored(IPartPlacement placement)
    {
      placement.IsMirrored = true;
      return placement;
    }

    private static ISheetPlacement Sheet(List<IPartPlacement> placements)
      => new SheetPlacement(PlacementTypeEnum.Gravity, DeepNestLib.Sheet.NewSheet(1, 200, 100), placements, 0, false);

    private static ISheetPlacement SheetOf(params (string Name, int Copies)[] parts)
      => SheetOf(10.0, parts);

    private static ISheetPlacement SheetOf(double side, params (string Name, int Copies)[] parts)
    {
      var placements = new List<IPartPlacement>();
      double x = 0;
      foreach (var (name, copies) in parts)
      {
        for (int i = 0; i < copies; i++)
        {
          placements.Add(Part(name, x, 0, side));
          x += side * 2;
        }
      }

      return Sheet(placements);
    }

    /// <summary>Keeps each text with where it starts across the page.</summary>
    private sealed class PositionCanvas : IReportCanvas
    {
      private readonly List<(double X, string Text)> texts;

      public PositionCanvas(List<(double X, string Text)> texts) => this.texts = texts;

      public void SetFill(double r, double g, double b)
      {
      }

      public void FillRect(double x, double y, double w, double h, double r, double g, double b)
      {
      }

      public void Text(double x, double y, double size, bool bold, string text) => this.texts.Add((x, text));

      public void Line(double x1, double y1, double x2, double y2, double width)
      {
      }

      public void Rect(double x, double y, double w, double h, double lineWidth)
      {
      }

      public void Polygon(IEnumerable<(double X, double Y)> pts, bool fill, bool holeFill = false, (byte R, byte G, byte B)? fillColour = null)
      {
      }
    }

    /// <summary>Keeps the text of every page the report draws, and ignores the geometry.</summary>
    private sealed class RecordingCanvas : IReportCanvas
    {
      private readonly List<string> texts;

      public RecordingCanvas(List<string> texts) => this.texts = texts;

      public void SetFill(double r, double g, double b)
      {
      }

      public void FillRect(double x, double y, double w, double h, double r, double g, double b)
      {
      }

      public void Text(double x, double y, double size, bool bold, string text) => this.texts.Add(text);

      public void Line(double x1, double y1, double x2, double y2, double width)
      {
      }

      public void Rect(double x, double y, double w, double h, double lineWidth)
      {
      }

      public void Polygon(IEnumerable<(double X, double Y)> pts, bool fill, bool holeFill = false, (byte R, byte G, byte B)? fillColour = null)
      {
      }
    }
  }
}
