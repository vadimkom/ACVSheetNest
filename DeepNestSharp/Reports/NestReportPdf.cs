namespace DeepNestSharp.Reports
{
  using System;
  using System.Collections.Generic;
  using System.Globalization;
  using System.IO;
  using System.Linq;
  using System.Text;
  using DeepNestLib;
  using DeepNestLib.Placement;

  /// <summary>
  /// Writes the production nest report as a PDF: a job summary page (sheets to cut, utilization,
  /// part totals) plus one page per DISTINCT layout with a scaled drawing and its cut count —
  /// the paper the operator takes to the laser. Self-contained minimal PDF writer (vector lines +
  /// Helvetica text is all a report needs): no external PDF library, nothing to license.
  /// </summary>
  public static class NestReportPdf
  {
    private const double PageW = 842; // A4 landscape, points (297 x 210 mm)
    private const double PageH = 595;

    /// <summary>
    /// <paramref name="partColours"/> comes from the window, not from the placements: the colours follow
    /// the order of the PART LIST and the owner may have picked their own, and neither of those can be read
    /// off a sheet. Null falls back to colouring by the order the parts appear in the layouts.
    /// </summary>
    public static void Write(
      string path,
      IReadOnlyList<(ISheetPlacement Sheet, int Count, string Name)> layouts,
      int unplacedCount,
      string units = "in",
      IReadOnlyDictionary<string, (byte R, byte G, byte B)> partColours = null,
      ReportMaterial material = null)
    {
      var pdf = new MiniPdf();
      GenerateReport(
        () =>
        {
          return pdf.NewPage();
        },
        layouts,
        unplacedCount,
        units,
        partColours,
        material);

      File.WriteAllBytes(path, pdf.Build());
    }

    public static byte[] BuildPdfBytes(
      IReadOnlyList<(ISheetPlacement Sheet, int Count, string Name)> layouts,
      int unplacedCount,
      string units = "in",
      IReadOnlyDictionary<string, (byte R, byte G, byte B)> partColours = null,
      ReportMaterial material = null)
    {
      var pdf = new MiniPdf();
      GenerateReport(
        () =>
        {
          return pdf.NewPage();
        },
        layouts,
        unplacedCount,
        units,
        partColours,
        material);

      return pdf.Build();
    }

    public static System.Windows.Documents.FixedDocument BuildFixedDocument(
      IReadOnlyList<(ISheetPlacement Sheet, int Count, string Name)> layouts,
      int unplacedCount,
      string units = "in",
      IReadOnlyDictionary<string, (byte R, byte G, byte B)> partColours = null,
      ReportMaterial material = null)
    {
      var doc = new System.Windows.Documents.FixedDocument();
      GenerateReport(
        () =>
        {
          var wpf = new WpfPageCanvas();
          var fp = new System.Windows.Documents.FixedPage
          {
            Width = PageW,
            Height = PageH,
          };
          fp.Children.Add(wpf.Canvas);
          var pc = new System.Windows.Documents.PageContent();
          ((System.Windows.Markup.IAddChild)pc).AddChild(fp);
          doc.Pages.Add(pc);
          return wpf;
        },
        layouts,
        unplacedCount,
        units,
        partColours,
        material);

      return doc;
    }

    public static List<System.Windows.FrameworkElement> BuildWpfPages(
      IReadOnlyList<(ISheetPlacement Sheet, int Count, string Name)> layouts,
      int unplacedCount,
      string units = "in",
      IReadOnlyDictionary<string, (byte R, byte G, byte B)> partColours = null,
      ReportMaterial material = null)
    {
      var pages = new List<System.Windows.FrameworkElement>();
      GenerateReport(
        () =>
        {
          var wpf = new WpfPageCanvas();
          pages.Add(wpf.GetPageView());
          return wpf;
        },
        layouts,
        unplacedCount,
        units,
        partColours,
        material);

      return pages;
    }

    public static void GenerateReport(
      Func<IReportCanvas> newPage,
      IReadOnlyList<(ISheetPlacement Sheet, int Count, string Name)> layouts,
      int unplacedCount,
      string units = "in",
      IReadOnlyDictionary<string, (byte R, byte G, byte B)> partColours = null,
      ReportMaterial material = null)
    {
      if (layouts == null || layouts.Count == 0)
      {
        throw new InvalidOperationException(L("Rep_NothingToReport"));
      }

      // A parts list that does not fit carries on over extra pages, so neither the page count in the footers
      // nor the page a layout starts on (the cutting plan's Page column) is known until the list is laid out.
      // A dry run on blank canvases finds both; the real run then prints them.
      var dry = Render(() => new NullCanvas(), layouts, unplacedCount, units, partColours, null, 0, material);
      Render(newPage, layouts, unplacedCount, units, partColours, dry.LayoutPages, dry.Pages, material);
    }

    private static (int Pages, List<int> LayoutPages) Render(
      Func<IReportCanvas> newPage,
      IReadOnlyList<(ISheetPlacement Sheet, int Count, string Name)> layouts,
      int unplacedCount,
      string units,
      IReadOnlyDictionary<string, (byte R, byte G, byte B)> partColours,
      IReadOnlyList<int> layoutPages,
      int pageCount,
      ReportMaterial material)
    {
      int page = 0;
      var layoutStarts = new List<int>();

      // Everything below computes with "mm" or "in". The on-screen preview and printing used to pass the
      // TRANSLATED word, "мм", which matched neither, so a Russian report came out in square inches.
      units = IsMillimetres(units) ? "mm" : "in";

      // Mixed stock: every layout carries ITS OWN sheet size — never assume the first one's.
      int totalSheets = layouts.Sum(l => l.Count);
      int totalParts = layouts.Sum(l => l.Count * l.Sheet.PartPlacements.Count);
      double placedArea = layouts.Sum(l => l.Count * l.Sheet.PartPlacements.Sum(p => Math.Abs(p.Part.NetArea)));
      double stockArea = layouts.Sum(l => l.Count * SheetArea(l.Sheet.Sheet));
      double overallUtil = stockArea <= 0 ? 0 : placedArea / stockArea * 100.0;

      // Total quantity per source part across the whole job (mirrored copies listed separately —
      // a left-hand and a right-hand part are different physical parts on the shop floor).
      // Colour rides alongside the label: they are keyed differently on purpose — a mirrored copy is its own
      // LINE (different physical part to count) but shares its original's COLOUR (the parts list has one
      // thumbnail per file, and that thumbnail is what names the colour).
      var partTotals = NumberedPartTotals(layouts);

      // A part's weight is shown when the job says what it is cut from, in millimetres.
      ReportMaterial weighMaterial = material != null && material.Thickness > 0 && IsMillimetres(units) ? material : null;

      // The number is what ties a shape on the drawing back to a row here, so both tables and the
      // drawing read it out of the same place.
      var partNumbers = partTotals.ToDictionary(t => t.File, t => t.Number, StringComparer.OrdinalIgnoreCase);
      var partInfo = partTotals.ToDictionary(t => t.File, StringComparer.OrdinalIgnoreCase);

      var colours = partColours
        ?? PartColors.Build(layouts.SelectMany(l => l.Sheet.PartPlacements).Select(p => p.Part?.Name));

      // Every part in the job is coloured, a single-type job included — the paper has to match the screen.
      bool colourCoded = colours.Count > 0;

      string stamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

      IReportCanvas NextPage()
      {
        page++;
        var pc = newPage();
        Header(pc, stamp);
        Footer(pc, page, pageCount);
        return pc;
      }

      // One column, whole names: the rows the parts list could not fit go on as many pages as it takes,
      // under a title that says whose list it is.
      void ContinueOnNewPages(IReadOnlyList<PartRow> rows, int next, string title)
      {
        while (next < rows.Count)
        {
          var pc = NextPage();
          pc.Text(40, 544, 13, bold: true, LF("Rep_Continued", title));
          next = PartTable(pc, 520, rows, next, colours, colourCoded, units, weighMaterial);
        }
      }

      // ── Page 1: written for TWO readers — the material buyer (big unambiguous "what to buy"
      // box, nothing to interpret) and the operator (cutting plan + part totals tables below). ──
      {
        var c = NextPage();

        // MATERIAL REQUIRED — one line per sheet size with a big count. This is the purchase order.
        var stockLines = layouts
          .GroupBy(l => (W: l.Sheet.Sheet.WidthCalculated, H: l.Sheet.Sheet.HeightCalculated))
          .Select(g => (g.Key.W, g.Key.H, Count: g.Sum(x => x.Count)))
          .OrderByDescending(t => t.W * t.H)
          .ToList();
        double totalStockArea = stockLines.Sum(s => s.W * s.H * s.Count);
        string totalStockAreaStr = FormatStockArea(totalStockArea, units);

        // Landscape Page 1: MATERIAL REQUIRED (left) and JOB SUMMARY (right) side by side
        double boxTop = 544;

        // What a quote needs besides the sheets: how far the head travels cutting and how many times it
        // pierces (once per contour, outline and holes, and once per piece of a cut inside a part), and with a
        // material chosen what it all weighs.
        double cutLength = partTotals.Sum(t => t.CutLength * t.Qty);
        int pierces = layouts.Sum(l => l.Count * l.Sheet.PartPlacements.Sum(p =>
          1 + (p.Part?.Children?.Count ?? 0) + DeepNestLib.IO.DxfInnerCuts.Measure(p.Part?.Name).Pieces));
        bool weighs = material != null && material.Thickness > 0 && IsMillimetres(units);

        // The buyer's box says everything about the metal: the sheets, what they are and what they weigh. The
        // summary beside it is the job: parts, cutting and how well the metal is used. The material used to sit
        // in the summary, where a long name ran past the box, with the buyer's box half empty.
        var materialRows = new List<(string Label, string Value)>();
        if (material != null)
        {
          materialRows.Add((L("Rep_MaterialKind"), material.Thickness > 0
            ? FormattableString.Invariant($"{material.Name}, {material.Thickness:0.##} ") + UnitWord("mm")
            : material.Name));
        }

        if (weighs)
        {
          materialRows.Add((L("Rep_SheetWeight"), FormattableString.Invariant($"{material.Kilograms(stockArea):0.0} ") + L("Rep_Kg")));
          materialRows.Add((L("Rep_PartWeight"), FormattableString.Invariant($"{material.Kilograms(placedArea):0.0} ") + L("Rep_Kg")));
        }

        const double leftX = 40, leftW = 312, rightX = 368, rightW = 434;
        int jRows = (unplacedCount > 0 ? 4 : 3) + 2;
        double stockBoxH = (stockLines.Count * 22) + (materialRows.Count * 14) + (materialRows.Count > 0 ? 6 : 0) + 28;
        double sumBoxH = (jRows * 15) + 16;
        double topH = Math.Max(stockBoxH, sumBoxH);

        // Left box: MATERIAL REQUIRED
        c.Text(leftX, 550, 10.5, bold: true, L("Rep_Material"));
        c.FillRect(leftX, boxTop - topH, leftW, topH, 0.94, 0.94, 0.94);
        c.Rect(leftX, boxTop - topH, leftW, topH, 0.8);
        double ry = boxTop - 18;
        foreach (var (w, h, count) in stockLines)
        {
          c.Text(52, ry, 14, bold: true, count.ToString("0", CultureInfo.InvariantCulture));
          c.Text(92, ry, 9, bold: false, LF("Rep_SheetsOf", SheetWord(count), Num(w), Num(h), UnitWord(units)));
          c.SetFill(0.35, 0.35, 0.35);
          c.Text(leftX + leftW - 52, ry, 8, bold: false, FormatStockArea(w * h * count, units));
          c.SetFill(0, 0, 0);
          ry -= 22;
        }

        if (materialRows.Count > 0)
        {
          ry -= 2;
          foreach (var (label, value) in materialRows)
          {
            c.Text(52, ry, 8.5, bold: false, label);
            c.Text(170, ry, 8.5, bold: true, value);
            ry -= 14;
          }
        }

        c.Line(48, boxTop - topH + 20, leftX + leftW - 8, boxTop - topH + 20, 0.5);
        c.Text(52, boxTop - topH + 6, 8.5, bold: true,
          LF("Rep_Total", totalSheets.ToString("0", CultureInfo.InvariantCulture), SheetWord(totalSheets), totalStockAreaStr));

        // Right box: JOB SUMMARY
        c.Text(rightX, 550, 10.5, bold: true, L("Rep_Summary"));
        c.FillRect(rightX, boxTop - topH, rightW, topH, 0.965, 0.965, 0.965);
        c.Rect(rightX, boxTop - topH, rightW, topH, 0.8);
        double jy = boxTop - 18;
        void JRow(string label, string value, bool warn = false)
        {
          c.Text(rightX + 14, jy, 8.5, bold: false, label);
          if (warn)
          {
            c.SetFill(0.75, 0, 0);
          }

          c.Text(rightX + 220, jy, 8.5, bold: true, value);
          c.SetFill(0, 0, 0);
          jy -= 15;
        }

        JRow(L("Rep_PartsToCut"), totalParts.ToString("0", CultureInfo.InvariantCulture));
        JRow(L("Rep_CutTotal"), FormatLength(cutLength, units));
        JRow(L("Rep_Pierces"), pierces.ToString("0", CultureInfo.InvariantCulture)); // "1,913" read as a decimal in a Russian report
        JRow(L("Rep_MaterialUsed"), $"{overallUtil.ToString("0.0", CultureInfo.InvariantCulture)} %");
        JRow(L("Rep_DifferentLayouts"), layouts.Count.ToString(CultureInfo.InvariantCulture));
        if (unplacedCount > 0)
        {
          JRow(L("Rep_NotPlaced"), unplacedCount.ToString(CultureInfo.InvariantCulture), warn: true);
        }

        // CUTTING PLAN — one row per layout, table form: what to cut, on what, how many times.
        double planTop = boxTop - topH - 16;
        c.Text(40, planTop, 10.5, bold: true, L("Rep_CuttingPlan"));
        double ty = planTop - 15;
        c.FillRect(40, ty - 4, 762, 15, 0.92, 0.92, 0.92);
        c.Text(48, ty, 8, bold: true, L("Rep_Layout"));
        c.Text(130, ty, 8, bold: true, L("Rep_PageCol"));
        c.Text(210, ty, 8, bold: true, L("Rep_SheetSize"));
        c.Text(430, ty, 8, bold: true, L("Rep_Parts"));
        c.Text(560, ty, 8, bold: true, L("Rep_Cut"));
        c.Text(690, ty, 8, bold: true, L("Rep_Used"));
        ty -= 15;
        int planShown = 0;
        for (int i = 0; i < layouts.Count && ty >= 220; i++)
        {
          var l = layouts[i];
          c.Text(48, ty, 8, bold: false, LF("Rep_LayoutN", i + 1));
          c.Text(130, ty, 8, bold: false, (layoutPages != null && i < layoutPages.Count ? layoutPages[i] : i + 2).ToString(CultureInfo.InvariantCulture));
          c.Text(210, ty, 8, bold: false, $"{Num(l.Sheet.Sheet.WidthCalculated)} x {Num(l.Sheet.Sheet.HeightCalculated)}");
          c.Text(430, ty, 8, bold: false, l.Sheet.PartPlacements.Count.ToString(CultureInfo.InvariantCulture));
          c.Text(560, ty, 8, bold: true, $"x {l.Count}");
          c.Text(690, ty, 8, bold: false, $"{Util(l.Sheet).ToString("0.0", CultureInfo.InvariantCulture)} %");
          c.Line(40, ty - 4, 802, ty - 4, 0.25);
          ty -= 13.5;
          planShown++;
        }

        if (planShown < layouts.Count)
        {
          c.Text(48, ty, 8, bold: false, LF("Rep_MoreLayouts", layouts.Count - planShown));
          ty -= 13.5;
        }

        // PART TOTALS — what the job produces, for checking against the order
        double py = ty - 16;
        c.Text(40, py, 10.5, bold: true, L("Rep_PartTotals"));
        py -= 15;
        var totalRows = partTotals.Select(t => new PartRow(t.Number, t.Colour, t.File, t.BoxArea, t.CutLength, t.Qty, SizeText(t.BoxWidth, t.BoxHeight), t.NetArea)).ToList();
        int next = PartTable(c, py, totalRows, 0, colours, colourCoded, units, weighMaterial);
        ContinueOnNewPages(totalRows, next, L("Rep_PartTotals"));
      }

      // ── One page per distinct layout ────────────────────────────────────────────────────────
      for (int i = 0; i < layouts.Count; i++)
      {
        var (sp, count, _) = layouts[i];
        double sheetW = sp.Sheet.WidthCalculated;
        double sheetH = sp.Sheet.HeightCalculated;
        var c = NextPage();
        layoutStarts.Add(page);

        // Below the header rule at 560: a 13 pt title set at 556 ran straight through it.
        c.Text(40, 544, 13, bold: true, LF("Rep_LayoutTitle", i + 1, layouts.Count, count));
        c.Text(40, 531, 9, bold: false, LF(
          "Rep_LayoutSub",
          sp.PartPlacements.Count,
          Util(sp).ToString("0.0", CultureInfo.InvariantCulture),
          Num(sheetW),
          Num(sheetH),
          UnitWord(units)));

        var onSheet = sp.PartPlacements
          .GroupBy(p => Label(p), StringComparer.OrdinalIgnoreCase)
          .Select(g => (File: g.Key, Colour: PartColors.ColourKeyFor(g.First()), Qty: g.Count()))
          .OrderByDescending(t => t.Qty)
          .ToList();

        var layoutRows = onSheet
          .Select(t => new PartRow(
            partNumbers.TryGetValue(t.File, out int n) ? n : (int?)null,
            t.Colour,
            t.File,
            partInfo.TryGetValue(t.File, out var pi) ? pi.BoxArea : (double?)null,
            partInfo.TryGetValue(t.File, out var pc) ? pc.CutLength : (double?)null,
            t.Qty,
            partInfo.TryGetValue(t.File, out var ps) ? SizeText(ps.BoxWidth, ps.BoxHeight) : null,
            partInfo.TryGetValue(t.File, out var pn) ? pn.NetArea : (double?)null))
          .ToList();

        // Up to LayoutListRows rows share the page with the drawing; the rest carry on overleaf.
        int listRows = Math.Min(onSheet.Count, LayoutListRows);
        double partsListH = (listRows * RowPitch) + 32;
        double boxX = 40, boxW = 762;
        double boxY = 28 + partsListH;
        double boxH = 522 - boxY; // clear of the layout title and its subtitle above
        double scale = Math.Min(boxW / sheetW, boxH / sheetH);
        double ox = boxX + ((boxW - (sheetW * scale)) / 2.0);
        double oy = boxY + ((boxH - (sheetH * scale)) / 2.0);

        if (DeepNestLib.IO.SheetShape.IsShaped(sp.Sheet))
        {
          // An offcut read from a drawing: its own outline and holes, not the box round it.
          c.Polygon(sp.Sheet.Points.Select(p => (ox + (p.X * scale), oy + (p.Y * scale))), fill: false);
          foreach (var hole in sp.Sheet.Children ?? new List<INfp>())
          {
            c.Polygon(hole.Points.Select(p => (ox + (p.X * scale), oy + (p.Y * scale))), fill: false);
          }
        }
        else
        {
          c.Rect(ox, oy, sheetW * scale, sheetH * scale, 0.8);
        }
        foreach (var pp in sp.PartPlacements)
        {
          var poly = pp.PlacedPart;
          if (poly?.Points == null || poly.Points.Length < 3)
          {
            continue;
          }

          c.Polygon(
            poly.Points.Select(p => (ox + (p.X * scale), oy + (p.Y * scale))),
            fill: true,
            fillColour: colourCoded ? PartColors.For(colours, pp) : (ValueTuple<byte, byte, byte>?)null);
          if (poly.Children != null)
          {
            foreach (var hole in poly.Children)
            {
              if (hole?.Points != null && hole.Points.Length >= 3)
              {
                c.Polygon(hole.Points.Select(p => (ox + (p.X * scale), oy + (p.Y * scale))), fill: false, holeFill: true);
              }
            }
          }
        }

        // Part numbers, in a SECOND pass on purpose: drawn inside the loop above, a neighbour filled
        // afterwards would paint over a number already placed. Same numbers as the tables, so a shape
        // on the sheet can be matched to a row and marked.
        foreach (var pp in sp.PartPlacements)
        {
          var poly = pp.PlacedPart;
          if (poly?.Points == null
            || poly.Points.Length < 3
            || !partNumbers.TryGetValue(Label(pp), out int number))
          {
            continue;
          }

          string numberText = number.ToString(CultureInfo.InvariantCulture);
          var (nx, ny, nsize) = NumberLabel(poly, numberText, ox, oy, scale);
          bool lightInk = colourCoded && NeedsLightInk(PartColors.For(colours, pp));
          if (lightInk)
          {
            c.SetFill(1, 1, 1);
          }

          c.Text(nx, ny, nsize, bold: true, numberText);
          if (lightInk)
          {
            c.SetFill(0, 0, 0);
          }
        }

        // Parts on this layout (below the drawing)
        double py = boxY - 12;
        c.Text(40, py, 10, bold: true, L("Rep_PartsOnLayout"));
        py -= 14;
        int next = PartTable(c, py, layoutRows, 0, colours, colourCoded, units, weighMaterial);
        ContinueOnNewPages(layoutRows, next, $"{LF("Rep_LayoutN", i + 1)}: {L("Rep_PartsOnLayout")}");
      }

      return (page, layoutStarts);
    }

    private const int LayoutListRows = 12;
    private const double RowPitch = 13;

    private readonly record struct PartRow(int? Number, string Colour, string File, double? BoxArea, double? CutLength, int Qty, string Size = null, double? NetArea = null);

    /// <summary>The parts table in ONE column across the page: header at <paramref name="py"/>, then rows from
    /// <paramref name="from"/> for as long as they clear the footer. Returns the first row it had no room for.</summary>
    private static int PartTable(
      IReportCanvas c,
      double py,
      IReadOnlyList<PartRow> rows,
      int from,
      IReadOnlyDictionary<string, (byte R, byte G, byte B)> colours,
      bool colourCoded,
      string units,
      ReportMaterial weighMaterial = null)
    {
      // With a weight column the others close up to make room for it; the quantity stays last, at the edge.
      bool weighs = weighMaterial != null;
      const double numX = 48, swatchX = 72, qtyX = 752;
      double sizeX = weighs ? 395 : 430, areaX = weighs ? 482 : 525, weightX = 572, cutX = weighs ? 642 : 625;
      double nameX = colourCoded ? 86 : 72;

      c.FillRect(40, py - 4, 762, 15, 0.92, 0.92, 0.92);
      c.Text(numX, py, 8, bold: true, L("Rep_No"));
      c.Text(nameX, py, 8, bold: true, L("Rep_Part"));
      c.Text(sizeX, py, 8, bold: true, LF("Rep_SizeCol", UnitWord(units)));
      c.Text(areaX, py, 8, bold: true, L("Rep_BoxArea"));
      if (weighs)
      {
        c.Text(weightX, py, 8, bold: true, L("Rep_WeightCol"));
      }

      c.Text(cutX, py, 8, bold: true, L("Rep_CutLength"));
      c.Text(qtyX, py, 8, bold: true, L("Rep_Qty"));
      py -= 14;

      int i = from;
      for (; i < rows.Count && py >= 28; i++)
      {
        var row = rows[i];
        if (row.Number.HasValue)
        {
          c.Text(numX, py, 8, bold: true, row.Number.Value.ToString(CultureInfo.InvariantCulture));
        }

        if (colourCoded && colours.TryGetValue(row.Colour, out var swatch))
        {
          c.FillRect(swatchX, py, 6.5, 6.5, swatch.R / 255.0, swatch.G / 255.0, swatch.B / 255.0);
        }

        c.Text(nameX, py, 8, bold: false, Trunc(row.File, weighs ? 58 : colourCoded ? 65 : 70));
        if (!string.IsNullOrEmpty(row.Size))
        {
          c.Text(sizeX, py, 8, bold: false, row.Size);
        }

        if (row.BoxArea.HasValue)
        {
          c.Text(areaX, py, 8, bold: false, FormatArea(row.BoxArea.Value, units));
        }

        if (weighs && row.NetArea.HasValue)
        {
          // One piece, the metal itself (holes left out), as the summary's part weight counts it.
          c.Text(weightX, py, 8, bold: false, weighMaterial.Kilograms(row.NetArea.Value).ToString("0.00", CultureInfo.InvariantCulture));
        }

        if (row.CutLength.HasValue)
        {
          c.Text(cutX, py, 8, bold: false, FormatLength(row.CutLength.Value, units));
        }

        c.Text(qtyX, py, 8, bold: false, row.Qty.ToString("0", CultureInfo.InvariantCulture));
        c.Line(40, py - 4, 802, py - 4, 0.25);
        py -= RowPitch;
      }

      return i;
    }

    /// <summary>Draws nothing: the dry run only needs to know where the pages break.</summary>
    private sealed class NullCanvas : IReportCanvas
    {
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

      public void Rect(double x, double y, double w, double h, double lineWidth)
      {
      }

      public void Polygon(IEnumerable<(double X, double Y)> pts, bool fill, bool holeFill = false, (byte R, byte G, byte B)? fillColour = null)
      {
      }
    }

    private static void Header(IReportCanvas c, string stamp)
    {
      c.Text(40, 568, 13, bold: true, L("Rep_Header"));
      c.Text(710, 570, 8.5, bold: false, stamp);
      c.Line(40, 560, 802, 560, 0.8);
    }

    private static void Footer(IReportCanvas c, int page, int of)
    {
      c.Text(395, 16, 8, bold: false, LF("Rep_Page", page, of));
    }

    /// <summary>The metal of a sheet: its box, or for a sheet read from a drawing its outline less its holes.</summary>
    private static double SheetArea(ISheet sheet)
      => DeepNestLib.IO.SheetShape.IsShaped(sheet)
        ? RasterNest.SparrowNestService.ShapeArea(sheet)
        : sheet.WidthCalculated * sheet.HeightCalculated;

    private static double Util(ISheetPlacement sp)
    {
      double area = SheetArea(sp.Sheet);
      return area <= 0 ? 0 : sp.PartPlacements.Sum(p => Math.Abs(p.Part.NetArea)) / area * 100.0;
    }

    private static string DisplayName(string path)
    {
      try
      {
        if (!string.IsNullOrWhiteSpace(path)
          && DeepNestLib.IO.DxfExporter.PartNames.TryGetValue(path, out string given)
          && !string.IsNullOrWhiteSpace(given))
        {
          return given; // the name the operator gave the part
        }

        return string.IsNullOrWhiteSpace(path) ? L("Rep_PartFallback") : Path.GetFileName(path);
      }
      catch (ArgumentException)
      {
        return path ?? L("Rep_PartFallback");
      }
    }

    /// <summary>Report label for a placement — mirrored copies count as their own line item.</summary>
    private static string Label(IPartPlacement p) => PartColors.LabelFor(p);

    /// <summary>
    /// One row per distinct part in the job, in the order the tables show them (biggest quantity
    /// first), each carrying the number that identifies it on every sheet drawing.
    /// <para>
    /// The numbers are handed out ONCE over the whole job, never per page: the same part has to carry
    /// the same number on layout 1 and on layout 4, or the operator cannot mark anything by it. A
    /// mirrored copy is its own row and so its own number, because it is the other hand and takes a
    /// different mark.
    /// </para>
    /// </summary>
    internal static List<(string File, string Colour, int Qty, int Number, double BoxArea, double CutLength, double BoxWidth, double BoxHeight, double NetArea)> NumberedPartTotals(
      IReadOnlyList<(ISheetPlacement Sheet, int Count, string Name)> layouts)
    {
      if (layouts == null || layouts.Count == 0)
      {
        return new List<(string, string, int, int, double, double, double, double, double)>();
      }

      return layouts
        .SelectMany(l => l.Sheet.PartPlacements.Select(p => (File: Label(p), Colour: PartColors.ColourKeyFor(p), l.Count, Placement: p)))
        .GroupBy(t => t.File, StringComparer.OrdinalIgnoreCase)
        .Select(g =>
        {
          var first = g.First();
          var p = first.Placement;
          INfp part = p?.Part;
          if (part != null && Math.Abs(p.Rotation % 360) > 1e-6)
          {
            part = part.Rotate(-p.Rotation);
          }

          var (boxW, boxH, boxArea) = ComputeBoundingBox(part);
          // The outline and holes, and any cut inside the part that encloses nothing (strips of a tape blank).
          double cutLength = LaserCutLength(part) + DeepNestLib.IO.DxfInnerCuts.Measure(p?.Part?.Name).Length;

          return (
            File: g.Key,
            Colour: first.Colour,
            Qty: g.Sum(t => t.Count),
            BoxArea: boxArea,
            BoxWidth: Math.Max(boxW, boxH),
            BoxHeight: Math.Min(boxW, boxH),
            CutLength: cutLength,
            NetArea: Math.Abs(p?.Part?.NetArea ?? 0));
        })
        .OrderByDescending(t => t.Qty)
        .Select((t, i) => (t.File, t.Colour, t.Qty, Number: i + 1, t.BoxArea, t.CutLength, t.BoxWidth, t.BoxHeight, t.NetArea))
        .ToList();
    }

    /// <summary>
    /// Computes the area and dimensions of the minimum rectangle enclosing the part polygon.
    /// Evaluates both the coordinate-aligned box and oriented boxes along all polygon edges.
    /// </summary>
    internal static (double Width, double Height, double Area) ComputeBoundingBox(INfp part)
    {
      if (part?.Points == null || part.Points.Length < 2)
      {
        return (0, 0, 0);
      }

      var pts = part.Points;
      double minX = pts[0].X, maxX = pts[0].X;
      double minY = pts[0].Y, maxY = pts[0].Y;
      for (int i = 1; i < pts.Length; i++)
      {
        var p = pts[i];
        if (p.X < minX) minX = p.X;
        if (p.X > maxX) maxX = p.X;
        if (p.Y < minY) minY = p.Y;
        if (p.Y > maxY) maxY = p.Y;
      }

      double bestW = maxX - minX;
      double bestH = maxY - minY;
      double bestArea = bestW * bestH;

      for (int i = 0; i < pts.Length; i++)
      {
        var p1 = pts[i];
        var p2 = pts[(i + 1) % pts.Length];
        double dx = p2.X - p1.X;
        double dy = p2.Y - p1.Y;
        double len = Math.Sqrt((dx * dx) + (dy * dy));
        if (len < 1e-7)
        {
          continue;
        }

        double cos = dx / len;
        double sin = dy / len;

        double uMin = double.MaxValue, uMax = double.MinValue;
        double vMin = double.MaxValue, vMax = double.MinValue;

        for (int j = 0; j < pts.Length; j++)
        {
          var pt = pts[j];
          double u = (pt.X * cos) + (pt.Y * sin);
          double v = (-pt.X * sin) + (pt.Y * cos);

          if (u < uMin) uMin = u;
          if (u > uMax) uMax = u;
          if (v < vMin) vMin = v;
          if (v > vMax) vMax = v;
        }

        double w = uMax - uMin;
        double h = vMax - vMin;
        double area = w * h;

        if (area < bestArea - 1e-7)
        {
          bestArea = area;
          bestW = Math.Max(w, h);
          bestH = Math.Min(w, h);
        }
      }

      return (Math.Max(bestW, bestH), Math.Min(bestW, bestH), bestArea);
    }

    /// <summary>
    /// Total laser cut length for a part: outer perimeter plus perimeter of all internal holes.
    /// </summary>
    internal static double LaserCutLength(INfp part)
    {
      if (part == null)
      {
        return 0;
      }

      double total = Perimeter(part);
      if (part.Children != null)
      {
        foreach (var child in part.Children)
        {
          total += LaserCutLength(child);
        }
      }

      return total;
    }

    /// <summary>
    /// Perimeter of a closed ring, adding the closing segment if start and end points differ.
    /// </summary>
    internal static double Perimeter(INfp poly)
    {
      if (poly?.Points == null || poly.Points.Length < 2)
      {
        return 0;
      }

      var pts = poly.Points;
      double length = 0;
      for (int i = 0; i < pts.Length - 1; i++)
      {
        double dx = pts[i + 1].X - pts[i].X;
        double dy = pts[i + 1].Y - pts[i].Y;
        length += Math.Sqrt((dx * dx) + (dy * dy));
      }

      double closeDx = pts[0].X - pts[pts.Length - 1].X;
      double closeDy = pts[0].Y - pts[pts.Length - 1].Y;
      if (Math.Abs(closeDx) > 1e-6 || Math.Abs(closeDy) > 1e-6)
      {
        length += Math.Sqrt((closeDx * closeDx) + (closeDy * closeDy));
      }

      return length;
    }

    /// <summary>A part's size across its box, the longer side first: "1354 × 1210".</summary>
    internal static string SizeText(double width, double height)
      => width > 0 && height > 0 ? FormattableString.Invariant($"{width:0.#} × {height:0.#}") : null;

    internal static string FormatArea(double area, string units)
    {
      if (area <= 0)
      {
        return "-";
      }

      if (IsMillimetres(units))
      {
        double sqM = area / 1_000_000.0;
        if (sqM >= 10.0)
        {
          return FormattableString.Invariant($"{sqM:0.0} ") + L("Rep_M2");
        }

        if (sqM >= 0.01)
        {
          return FormattableString.Invariant($"{sqM:0.000} ") + L("Rep_M2");
        }

        if (sqM >= 0.001)
        {
          return FormattableString.Invariant($"{sqM:0.000#} ") + L("Rep_M2");
        }

        return FormattableString.Invariant($"{sqM:0.0000#} ") + L("Rep_M2");
      }

      string u = UnitWord(units);
      return area >= 1000
        ? FormattableString.Invariant($"{area:0} ") + LF("Rep_Sq", u)
        : FormattableString.Invariant($"{area:0.##} ") + LF("Rep_Sq", u);
    }

    internal static string FormatLength(double length, string units)
    {
      if (length <= 0)
      {
        return "-";
      }

      // In millimetres a laser path is metres long, and "44,155 mm" reads like a typo: say "44.16 m".
      if (IsMillimetres(units))
      {
        return FormattableString.Invariant($"{length / 1000.0:0.00} ") + L("Rep_Metre");
      }

      string u = UnitWord(units);
      return length >= 1000
        ? FormattableString.Invariant($"{length:0} {u}")
        : FormattableString.Invariant($"{length:0.##} {u}");
    }

    internal static string FormatStockArea(double areaSquareUnits, string units)
    {
      if (areaSquareUnits <= 0)
      {
        return "-";
      }

      if (IsMillimetres(units))
      {
        double sqM = areaSquareUnits / 1_000_000.0;
        if (sqM < 0.01)
        {
          return FormattableString.Invariant($"{sqM:0.###} ") + L("Rep_M2");
        }

        return sqM >= 10
          ? FormattableString.Invariant($"{sqM:0.0} ") + L("Rep_M2")
          : FormattableString.Invariant($"{sqM:0.##} ") + L("Rep_M2");
      }

      double sqFt = areaSquareUnits / 144.0;
      return FormattableString.Invariant($"{sqFt:0} ") + L("Rep_SqFt");
    }

    /// <summary>
    /// Where a part's number goes on the sheet drawing and how big it is, in PDF points.
    /// <para>
    /// The anchor is the outline's AREA CENTROID, not the middle of its bounding box: on an L or a C
    /// the box centre lands in the notch, which is off the material and usually straight on top of
    /// whatever is nested in there. On a convex part the two agree. A part whose middle is a large
    /// hole still gets its number over the hole, which reads fine on paper and costs nothing.
    /// </para>
    /// <para>
    /// The size follows the part, bounded at both ends: under about four points nothing survives
    /// printing, and over twelve a big part gets a silly one. Small parts keep their number even when
    /// it slightly overhangs, because a part with no number is a part the operator cannot mark.
    /// </para>
    /// </summary>
    internal static (double X, double Y, double Size) NumberLabel(
      INfp placed,
      string text,
      double ox,
      double oy,
      double scale)
    {
      (double cx, double cy) = Centroid(placed);
      double side = Math.Min(placed.MaxX - placed.MinX, placed.MaxY - placed.MinY) * scale;
      double size = Math.Max(4.0, Math.Min(12.0, side * 0.42));

      // Helvetica advances a digit by 0.556 em and stands a cap 0.717 em tall. Text() draws from the
      // baseline, left aligned, and this writer cannot measure a string, so centring is arithmetic.
      double width = 0.556 * size * Math.Max(1, text?.Length ?? 1);
      return (
        ox + (cx * scale) - (width / 2.0),
        oy + (cy * scale) - (0.717 * size / 2.0),
        size);
    }

    /// <summary>Area centroid of a closed ring, falling back to the bounding-box centre for a
    /// degenerate one, which is the only case where the area is zero and there is nothing to divide
    /// by.</summary>
    private static (double X, double Y) Centroid(INfp poly)
    {
      var pts = poly.Points;
      double twiceArea = 0, cx = 0, cy = 0;
      for (int i = 0; i < pts.Length; i++)
      {
        var p = pts[i];
        var q = pts[(i + 1) % pts.Length];
        double cross = (p.X * q.Y) - (q.X * p.Y);
        twiceArea += cross;
        cx += (p.X + q.X) * cross;
        cy += (p.Y + q.Y) * cross;
      }

      return Math.Abs(twiceArea) < 1e-12
        ? ((poly.MinX + poly.MaxX) / 2.0, (poly.MinY + poly.MaxY) / 2.0)
        : (cx / (3.0 * twiceArea), cy / (3.0 * twiceArea));
    }

    /// <summary>A dark part colour needs a light number on top of it; the palette holds both.</summary>
    private static bool NeedsLightInk((byte R, byte G, byte B) colour)
      => ((0.299 * colour.R) + (0.587 * colour.G) + (0.114 * colour.B)) < 140.0;

    private static string Trunc(string s, int max) => s.Length <= max ? s : s.Substring(0, max - 1) + "~";

    private static string Num(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);

    private static string L(string key) => DeepNestSharp.Ui.Localization.Get(key);

    private static string LF(string key, params object[] args)
      => string.Format(CultureInfo.InvariantCulture, DeepNestSharp.Ui.Localization.Get(key), args);

    /// <summary>The drawing unit as the report prints it ("mm" / "мм", "in").</summary>
    private static string UnitWord(string units)
      => L(IsMillimetres(units) ? "Unit_Mm" : "Unit_In");

    internal static bool IsMillimetres(string units)
    {
      string u = units?.Trim();
      return string.Equals(u, "mm", StringComparison.OrdinalIgnoreCase)
        || string.Equals(u, "мм", StringComparison.OrdinalIgnoreCase);
    }

    private static string SheetWord(long count)
      => DeepNestSharp.Ui.Localization.Plural(count, "Rep_SheetOne", "Rep_SheetFew", "Rep_SheetMany");

    /// <summary>One page's content stream: text, lines, rectangles and filled polygons.</summary>
    private sealed class PageContent : IReportCanvas
    {
      private readonly StringBuilder sb = new StringBuilder();
      private readonly MiniPdf pdf;

      public PageContent(MiniPdf pdf)
      {
        this.pdf = pdf;
      }

      public void SetFill(double r, double g, double b)
      {
        this.sb.AppendLine(FormattableString.Invariant($"{r:0.###} {g:0.###} {b:0.###} rg"));
      }

      public void FillRect(double x, double y, double w, double h, double r, double g, double b)
      {
        this.sb.AppendLine(FormattableString.Invariant(
          $"{r:0.###} {g:0.###} {b:0.###} rg {x:0.##} {y:0.##} {w:0.##} {h:0.##} re f 0 0 0 rg"));
      }

      public void Text(double x, double y, double size, bool bold, string text)
      {
        this.sb.AppendLine(FormattableString.Invariant(
          $"BT /{(bold ? "F2" : "F1")} {size:0.#} Tf {x:0.##} {y:0.##} Td {this.pdf.TextOperand(text ?? string.Empty, bold)} Tj ET"));
      }

      public void Line(double x1, double y1, double x2, double y2, double width)
      {
        this.sb.AppendLine(FormattableString.Invariant(
          $"{width:0.##} w 0.35 0.35 0.35 RG {x1:0.##} {y1:0.##} m {x2:0.##} {y2:0.##} l S"));
      }

      public void Rect(double x, double y, double w, double h, double lineWidth)
      {
        this.sb.AppendLine(FormattableString.Invariant(
          $"{lineWidth:0.##} w 0.15 0.15 0.15 RG {x:0.##} {y:0.##} {w:0.##} {h:0.##} re S"));
      }

      /// <summary>Part outline (its part-type colour, or aluminum-gray when uncoloured) or hole (white
      /// fill), always stroked.</summary>
      public void Polygon(IEnumerable<(double X, double Y)> pts, bool fill, bool holeFill = false, (byte R, byte G, byte B)? fillColour = null)
      {
        bool first = true;
        foreach (var (x, y) in pts)
        {
          this.sb.Append(FormattableString.Invariant($"{x:0.##} {y:0.##} {(first ? "m" : "l")} "));
          first = false;
        }

        if (first)
        {
          return;
        }

        // The trailing "0 0 0 rg" restores the text fill color — without it every Text() drawn after
        // a polygon inherits the part/hole fill (white holes made whole part lists invisible).
        // Part fill (its type's colour, else aluminum-gray) + near-black outline, matching the app.
        string partRg = fillColour.HasValue
          ? FormattableString.Invariant($"{fillColour.Value.R / 255.0:0.###} {fillColour.Value.G / 255.0:0.###} {fillColour.Value.B / 255.0:0.###}")
          : "0.71 0.72 0.74";
        string paint = fill ? partRg + " rg b" : holeFill ? "1 1 1 rg b" : "s";
        this.sb.AppendLine(FormattableString.Invariant($"h 0.5 w 0.15 0.15 0.15 RG {paint} 0 0 0 rg"));
      }

      public byte[] ToBytes()
      {
        // Reset colors at stream start so pages are independent.
        return Latin1("0 0 0 rg 0 0 0 RG\n" + this.sb.ToString());
      }
    }

    /// <summary>
    /// Minimal PDF assembler: fixed A4-landscape pages (842 x 595 pt), uncompressed content streams, exact
    /// xref. Text is set in Arial embedded as a Unicode font, so Cyrillic labels and part names print; on a
    /// machine without a usable Arial it falls back to the standard Helvetica, which only covers Latin-1.
    /// </summary>
    private sealed class MiniPdf
    {
      private static readonly Lazy<(PdfTrueTypeFont Regular, PdfTrueTypeFont Bold)> SystemFonts =
        new Lazy<(PdfTrueTypeFont, PdfTrueTypeFont)>(PdfTrueTypeFont.LoadSystemArial);

      private readonly List<PageContent> pages = new List<PageContent>();
      private readonly PdfTrueTypeFont regular;
      private readonly PdfTrueTypeFont bold;

      public MiniPdf()
      {
        var (r, b) = SystemFonts.Value;
        if (r != null && b != null)
        {
          this.regular = r.ForDocument();
          this.bold = b.ForDocument();
        }
      }

      /// <summary>A new page, already added to the document.</summary>
      public PageContent NewPage()
      {
        var page = new PageContent(this);
        this.pages.Add(page);
        return page;
      }

      /// <summary>The string as a text-showing operand: glyph ids in hex with the embedded font, a literal
      /// Latin-1 string with the Helvetica fallback.</summary>
      public string TextOperand(string text, bool isBold)
      {
        var font = isBold ? this.bold : this.regular;
        return font != null
          ? "<" + font.Encode(text) + ">"
          : "(" + text.Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)") + ")";
      }

      public byte[] Build()
      {
        var pageStreams = this.pages.Select(p => p.ToBytes()).ToList();

        // Object layout: 1 Catalog, 2 Pages, then per page i: 3+2i = content, 4+2i = page, then the fonts.
        int n = pageStreams.Count;
        int f1 = 3 + (2 * n);
        int f2 = this.regular != null ? f1 + 5 : f1 + 1;
        var objects = new List<byte[]>
        {
          Latin1("<< /Type /Catalog /Pages 2 0 R >>"),
          Latin1($"<< /Type /Pages /Count {n} /Kids [{string.Join(" ", Enumerable.Range(0, n).Select(i => $"{4 + (2 * i)} 0 R"))}] >>"),
        };

        foreach (var stream in pageStreams)
        {
          var obj = new MemoryStream();
          WriteBytes(obj, Latin1($"<< /Length {stream.Length} >>\nstream\n"));
          WriteBytes(obj, stream);
          WriteBytes(obj, Latin1("\nendstream"));
          objects.Add(obj.ToArray());
          objects.Add(Latin1(FormattableString.Invariant(
            $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {PageW} {PageH}] /Resources << /Font << /F1 {f1} 0 R /F2 {f2} 0 R >> >> /Contents {objects.Count} 0 R >>")));
        }

        if (this.regular != null)
        {
          objects.AddRange(this.regular.BuildObjects(f1));
          objects.AddRange(this.bold.BuildObjects(f2));
        }
        else
        {
          objects.Add(Latin1("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>"));
          objects.Add(Latin1("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold /Encoding /WinAnsiEncoding >>"));
        }

        var outStream = new MemoryStream();
        WriteBytes(outStream, Latin1("%PDF-1.4\n"));
        var offsets = new long[objects.Count];
        for (int i = 0; i < objects.Count; i++)
        {
          offsets[i] = outStream.Position;
          WriteBytes(outStream, Latin1($"{i + 1} 0 obj\n"));
          WriteBytes(outStream, objects[i]);
          WriteBytes(outStream, Latin1("\nendobj\n"));
        }

        long xref = outStream.Position;
        var sb = new StringBuilder();
        sb.Append("xref\n0 ").Append(objects.Count + 1).Append('\n');
        sb.Append("0000000000 65535 f \n");
        foreach (long off in offsets)
        {
          sb.Append(off.ToString("0000000000", CultureInfo.InvariantCulture)).Append(" 00000 n \n");
        }

        sb.Append("trailer\n<< /Size ").Append(objects.Count + 1).Append(" /Root 1 0 R >>\nstartxref\n").Append(xref).Append("\n%%EOF");
        WriteBytes(outStream, Latin1(sb.ToString()));
        return outStream.ToArray();
      }

      private static void WriteBytes(Stream s, byte[] b)
      {
        s.Write(b, 0, b.Length);
      }
    }

    private static byte[] Latin1(string s)
    {
      return Encoding.GetEncoding("ISO-8859-1").GetBytes(s);
    }
  }
}
