namespace DeepNestSharp.RasterNest
{
  using System;
  using System.Collections.Generic;
  using System.Linq;
  using ClipperLib;
  using DeepNestLib;
  using DeepNestLib.NestProject;
  using DeepNestLib.Placement;

  /// <summary>
  /// A job of a few kinds of part that all go on one sheet is laid in rows, as the operator lays it by hand
  /// (the shop's uprights of 2026-11-17, `Flat-Stoika M-50`: tapered strips in bands, every other one turned
  /// round, the short ones in rows of three and a few standing in the strip left over beside a band). The
  /// engines packed the same job loosely, with parts in uneven rows and some too close to cut. Each kind is a
  /// block of rows (a lattice, every other part or row turned 180° where the part may turn so), the blocks
  /// stacked along the sheet, and the strips a block leaves beside it filled by the next kind first.
  /// </summary>
  internal static partial class SparrowNestService
  {
    /// <summary>Off with SHEETNEST_ROWS=0.</summary>
    internal static bool RowsLayoutEnabled = Environment.GetEnvironmentVariable("SHEETNEST_ROWS") != "0";

    /// <summary>The kinds of part a job may have to be laid in rows.</summary>
    private const int RowsMaxKinds = 4;

    private const double RowsScale = 1e6;

    /// <summary>
    /// The job laid in rows, or null when it is not such a job or does not go on: a few kinds of part (no
    /// common cutting, no free rotation) and rectangular sheets of the best priority in stock.
    /// <list type="bullet">
    /// <item>Counted parts must all go on one sheet; of the sizes that hold them, the smallest.</item>
    /// <item>Parts that fill the sheets as one kit (all of them, two or more, one size of sheet): as many
    /// kits as go on a sheet in rows, the same on every sheet.</item>
    /// </list>
    /// </summary>
    private static INestResult RowsLayout(List<Loaded> loaded, List<StockSize> sizes, double margin, double commonLineGap, double geometryTolerance, bool fillAsKit)
    {
      if (!RowsLayoutEnabled
          || loaded.Count == 0
          || loaded.Count > RowsMaxKinds
          || loaded.Any(l => l.Continuous || l.Cc != CommonCuttingMode.None || l.RequiresMergedLines || l.Nfp == null))
      {
        return null;
      }

      bool fill = loaded.Any(l => l.Fill);
      if (fill ? !(fillAsKit && loaded.Count > 1 && loaded.All(l => l.Fill)) : loaded.Any(l => l.Qty <= 0))
      {
        return null;
      }

      var inStock = sizes.Where(s => s.HasStock).ToList();
      if (inStock.Count == 0)
      {
        return null;
      }

      int best = inStock.Min(s => s.Priority);
      var first = inStock.Where(s => s.Priority == best).ToList();
      if (first.Any(s => s.Shape != null) || (fill && inStock.Count != 1))
      {
        return null;
      }

      var motifs = loaded.ToDictionary(l => l, RowMotifs);
      if (motifs.Values.Any(m => m.Count == 0))
      {
        return null;
      }

      if (fill)
      {
        var size = inStock[0];
        int sheets = size.Unlimited ? Math.Max(1, size.Remaining) : size.Remaining;
        double room = Math.Max(0, size.W - (2 * margin)) * Math.Max(0, size.H - (2 * margin));
        double kitArea = Math.Max(1e-9, loaded.Sum(l => Math.Abs(l.Nfp.Area)));
        List<RowCell> Kits(int n) => n <= 0 ? null : RowsCells(loaded, loaded.ToDictionary(l => l, _ => n), motifs, size.W, size.H, margin);

        // The most kits that go on: halving between none and what the area allows, then a step or two more.
        int lo = 0, hi = (int)Math.Floor(room / kitArea) + 1;
        List<RowCell> bestCells = null;
        while (lo < hi)
        {
          int mid = (lo + hi + 1) / 2;
          var cells = Kits(mid);
          if (cells != null)
          {
            (lo, bestCells) = (mid, cells);
          }
          else
          {
            hi = mid - 1;
          }
        }

        for (int more = lo + 1; more <= lo + 2; more++)
        {
          if (Kits(more) is { } extra)
          {
            bestCells = extra;
          }
        }

        return bestCells == null ? null : RowsResult(Enumerable.Repeat((size.W, size.H, bestCells), sheets).ToList(), commonLineGap, geometryTolerance);
      }

      foreach (var size in first.OrderBy(s => (double)s.W * s.H))
      {
        var cells = RowsCells(loaded, loaded.ToDictionary(l => l, l => l.Qty), motifs, size.W, size.H, margin);
        if (cells != null)
        {
          return RowsResult(new List<(int, int, List<RowCell>)> { (size.W, size.H, cells) }, commonLineGap, geometryTolerance);
        }
      }

      return null;
    }

    /// <summary>
    /// Whether the rows are kept over the engine's nest. Neat rows are worth a little metal (the operator
    /// lays parts so by hand), not much: an L-shaped part interlocks far better searched than in rows.
    /// <list type="bullet">
    /// <item>Parts too close to cut, counted by the nominal spacing as the layout paints them red, are not counted as placed.</item>
    /// <item>Filling the sheets: the rows win when they place at least 95 % of what the engine does.</item>
    /// <item>Counted parts: the rows win when the engine left some out, used more sheets or has parts too
    /// close; otherwise when the rows take at most 5 % more of the sheet's long side on a sheet no larger.</item>
    /// </list>
    /// </summary>
    private static bool RowsBeatEngine(INestResult rows, INestResult engine, List<Loaded> loaded, double margin)
    {
      var spacing = loaded.GroupBy(l => l.Source).ToDictionary(g => g.Key, g => g.Max(l => l.EffSpacing));
      double Of(IPartPlacement p) => spacing.TryGetValue(p.Source, out double s) ? s : 0;
      int Good(INestResult r) => r.UsedSheets.Sum(s => s.PartPlacements.Count
        - PlacementAcceptance.CountUnfit(s.PartPlacements.ToList(), s.Sheet.WidthCalculated, s.Sheet.HeightCalculated, margin, (a, b) => Math.Max(Of(a), Of(b)) * 0.999));
      int rowsGood = Good(rows);
      int engineGood = Good(engine);
      int enginePlaced = engine.UsedSheets.Sum(s => s.PartPlacements.Count);
      if (loaded.Any(l => l.Fill))
      {
        return rowsGood >= 0.95 * engineGood;
      }

      if (engine.UnplacedParts.Count > 0 || engine.UsedSheets.Count > rows.UsedSheets.Count || engineGood < enginePlaced || rowsGood < enginePlaced)
      {
        return rowsGood >= enginePlaced || engineGood < enginePlaced;
      }

      var rs = rows.UsedSheets[0];
      var es = engine.UsedSheets[0];
      if (es.Sheet.WidthCalculated * es.Sheet.HeightCalculated < (rs.Sheet.WidthCalculated * rs.Sheet.HeightCalculated) - 1e-6)
      {
        return false;
      }

      static double Span(ISheetPlacement s) => s.Sheet.WidthCalculated >= s.Sheet.HeightCalculated
        ? s.PartPlacements.Max(p => p.MaxX) - s.PartPlacements.Min(p => p.MinX)
        : s.PartPlacements.Max(p => p.MaxY) - s.PartPlacements.Min(p => p.MinY);
      return Span(rs) <= 1.05 * Span(es);
    }

    private static INestResult RowsResult(List<(int W, int H, List<RowCell> Cells)> sheets, double commonLineGap, double geometryTolerance)
    {
      var collection = new SheetPlacementCollection();
      for (int i = 0; i < sheets.Count; i++)
      {
        collection.Add(new SheetPlacement(
          placementType: PlacementTypeEnum.BoundingBox,
          sheet: MakeSheet(i + 1, sheets[i].W, sheets[i].H, null),
          partPlacements: RowPlacements(sheets[i].Cells),
          mergedLength: 0,
          clipperScale: SvgNest.Config.ClipperScale,
          requiresMergedLines: false,
          commonEdges: new List<CommonEdge>(),
          commonLineGap: commonLineGap,
          geometryTolerance: geometryTolerance));
      }

      int placed = sheets.Sum(s => s.Cells.Count);
      return new NestResult(placed, collection, new List<INfp>(), PlacementTypeEnum.BoundingBox, 0, 0);
    }

    private static List<IPartPlacement> RowPlacements(List<RowCell> cells)
    {
      var placements = new List<IPartPlacement>();
      foreach (var c in cells)
      {
        var shape = c.Motif.Shape[c.Turned];
        int rot = (c.Motif.Rot + (c.Turned * 180)) % 360;
        placements.Add(new PartPlacement(shape) { X = c.X - shape.MinX, Y = c.Y - shape.MinY, Rotation = rot, Source = c.Motif.Part.Source, IsMirrored = c.Motif.Part.Mirrored });
      }

      return placements;
    }

    /// <summary>One way of laying a part in rows: its orientation, which neighbours are turned round, the steps.</summary>
    private sealed class RowMotif
    {
      public Loaded Part;
      public int Rot;
      public int Ax;
      public int Ay;
      public double Px;
      public double Py;
      public double Shift;
      public double W;   // the nominal box of the part at this orientation
      public double H;
      public INfp[] Shape = new INfp[2];
      public List<IntPoint>[] Dil = new List<IntPoint>[2]; // grown by half the spacing, nominal box at the origin

      public int Turned(int i, int j) => ((((this.Ax * i) + (this.Ay * j)) % 2) + 2) % 2;

      public (double X, double Y) Cell(int i, int j) => ((i * this.Px) + ((((j % 2) + 2) % 2) == 1 ? this.Shift : 0), j * this.Py);

      /// <summary>The box round <paramref name="cols"/> × <paramref name="rows"/> cells.</summary>
      public (double W, double H) Block(int cols, int rows)
        => (((cols - 1) * this.Px) + this.W + (rows > 1 ? this.Shift : 0), ((rows - 1) * this.Py) + this.H);
    }

    /// <summary>A part laid in a block: the cell's corner (nominal box) and which of the two shapes.</summary>
    private readonly record struct RowCell(RowMotif Motif, double X, double Y, int Turned);

    private sealed class RowRect
    {
      public double L;
      public double B;
      public double R;
      public double T;

      public double Width => this.R - this.L;

      public double Height => this.T - this.B;
    }

    /// <summary>
    /// <paramref name="counts"/> of each kind laid in rows on a <paramref name="w"/> × <paramref name="h"/>
    /// sheet, the layout that ends nearest the start of the sheet; null when they do not all go on.
    /// </summary>
    private static List<RowCell> RowsCells(List<Loaded> loaded, Dictionary<Loaded, int> counts, Dictionary<Loaded, List<RowMotif>> motifs, int w, int h, double margin)
    {
      bool standing = h >= w;
      double gap = loaded.Max(l => l.FitSpacing);
      var found = new List<(double Extent, List<RowCell> Cells)>();
      foreach (var order in Permutations(loaded))
      {
        // The first kind's block decides the most; its few best shapes are each tried.
        foreach (var firstChoice in TopBlockChoices(motifs[order[0]], counts[order[0]], w, h, margin, standing).Take(6))
        {
          var cells = LayInRows(order, counts, motifs, firstChoice, w, h, margin, gap, standing);
          if (cells != null)
          {
            found.Add((standing ? cells.Max(c => c.Y + c.Motif.H) : cells.Max(c => c.X + c.Motif.W), cells));
          }
        }
      }

      // The steps were found by bisection on grown outlines: checked once more on the parts as placed.
      double SpacingOf(IPartPlacement p) => loaded.First(l => l.Source == p.Source && l.Mirrored == p.IsMirrored).FitSpacing;
      foreach (var (_, cells) in found.OrderBy(f => f.Extent))
      {
        if (PlacementAcceptance.CountUnfit(RowPlacements(cells), w, h, margin, (a, b) => Math.Max(SpacingOf(a), SpacingOf(b)) * 0.999) == 0)
        {
          return cells;
        }
      }

      return null;
    }

    /// <summary>Every order of the kinds (at most 24).</summary>
    private static IEnumerable<List<Loaded>> Permutations(List<Loaded> items)
    {
      if (items.Count <= 1)
      {
        yield return items.ToList();
        yield break;
      }

      for (int i = 0; i < items.Count; i++)
      {
        var rest = items.Where((_, k) => k != i).ToList();
        foreach (var tail in Permutations(rest))
        {
          tail.Insert(0, items[i]);
          yield return tail;
        }
      }
    }

    /// <summary>
    /// The blocks a kind can be laid in across the whole sheet, the shortest along the sheet first: each motif
    /// with as many across as go on (and the block one narrower, which leaves a strip beside it).
    /// </summary>
    private static IEnumerable<(RowMotif Motif, int Across)> TopBlockChoices(List<RowMotif> motifs, int count, int w, int h, double margin, bool standing)
    {
      var choices = new List<(RowMotif, int, double, double)>();
      foreach (var m in motifs)
      {
        int most = AcrossFits(m, standing ? w - (2 * margin) : h - (2 * margin), standing, count);
        for (int across = most; across >= Math.Max(1, most - 1); across--)
        {
          int along = (count + across - 1) / across;
          var (bw, bh) = standing ? m.Block(across, along) : m.Block(along, across);
          choices.Add((m, across, standing ? bh : bw, standing ? bw : bh));
        }
      }

      return choices.OrderBy(c => c.Item3).ThenBy(c => c.Item4).Select(c => (c.Item1, c.Item2));
    }

    /// <summary>How many cells of a block go across <paramref name="room"/>: columns on a standing sheet, rows on a lying one.</summary>
    private static int AcrossFits(RowMotif m, double room, bool standing, int count)
    {
      int n = 0;
      while (n < count)
      {
        var (bw, bh) = standing ? m.Block(n + 1, 2) : m.Block(2, n + 1);
        if ((standing ? bw : bh) > room + 1e-9)
        {
          break;
        }

        n++;
      }

      return n;
    }

    /// <summary>
    /// The kinds in this order: each first into the strips left beside the blocks laid so far, as many as go
    /// there, then the rest in a block across the sheet past everything. Null when it does not all go on.
    /// </summary>
    private static List<RowCell> LayInRows(List<Loaded> order, Dictionary<Loaded, int> counts, Dictionary<Loaded, List<RowMotif>> motifs, (RowMotif Motif, int Across) firstChoice, int w, int h, double margin, double gap, bool standing)
    {
      var cells = new List<RowCell>();
      var free = new List<RowRect>();
      double reach = margin; // where the next block across the sheet starts along it
      for (int k = 0; k < order.Count; k++)
      {
        var part = order[k];
        int left = counts[part];

        // Into the strips first, the one that takes most each time.
        while (left > 0 && free.Count > 0)
        {
          RowRect bestRect = null;
          RowMotif bestMotif = null;
          int bestCols = 0, bestRows = 0, bestTake = 0;
          foreach (var rect in free)
          {
            foreach (var m in motifs[part])
            {
              int cols = CellsAlong(m, rect.Width, true, rect.Height);
              int rows = CellsAlong(m, rect.Height, false, rect.Width);
              if (cols < 1 || rows < 1)
              {
                continue;
              }

              // A single row has no shifted row to make room for.
              while (cols > 0 && m.Block(cols, rows).W > rect.Width + 1e-9)
              {
                cols--;
              }

              int take = Math.Min(left, cols * rows);
              if (take > bestTake)
              {
                (bestRect, bestMotif, bestCols, bestRows, bestTake) = (rect, m, cols, rows, take);
              }
            }
          }

          if (bestTake == 0)
          {
            break;
          }

          free.Remove(bestRect);
          var block = FillBlock(bestMotif, bestRect.L, bestRect.B, bestCols, bestRows, bestTake, standing);
          cells.AddRange(block);
          left -= bestTake;
          free.AddRange(SplitAround(bestRect, block, gap, standing));
        }

        if (left == 0)
        {
          continue;
        }

        // The rest across the sheet, past everything.
        RowMotif motif;
        int across;
        if (k == 0)
        {
          (motif, across) = firstChoice;
        }
        else
        {
          var choice = TopBlockChoices(motifs[part], left, w, h, margin, standing).FirstOrDefault();
          if (choice.Motif == null)
          {
            return null;
          }

          (motif, across) = choice;
        }

        int along = (left + across - 1) / across;
        int cols2 = standing ? across : along;
        int rows2 = standing ? along : across;
        double x0 = standing ? margin : reach;
        double y0 = standing ? reach : margin;
        var (bw, bh) = motif.Block(cols2, rows2);
        if (x0 + bw > w - margin + 1e-6 || y0 + bh > h - margin + 1e-6)
        {
          return null;
        }

        var placed = FillBlock(motif, x0, y0, cols2, rows2, left, standing);
        cells.AddRange(placed);
        var whole = standing
          ? new RowRect { L = margin, B = y0, R = w - margin, T = y0 + bh }
          : new RowRect { L = x0, B = margin, R = x0 + bw, T = h - margin };
        free.AddRange(SplitAround(whole, placed, gap, standing));
        reach = (standing ? y0 + bh : x0 + bw) + gap;
      }

      return cells;
    }

    /// <summary>How many cells go along one side of a rectangle: columns across its width, or rows up its height.</summary>
    private static int CellsAlong(RowMotif m, double room, bool columns, double otherRoom)
    {
      if (columns)
      {
        return room + 1e-9 < m.W ? 0 : (int)Math.Floor(((room - m.W) / m.Px) + 1e-9) + 1;
      }

      return room + 1e-9 < m.H ? 0 : (int)Math.Floor(((room - m.H) / m.Py) + 1e-9) + 1;
    }

    /// <summary>
    /// <paramref name="count"/> cells of a block at (<paramref name="x0"/>, <paramref name="y0"/>): row by row
    /// from the bottom on a standing sheet, column by column from the left on a lying one, so what is left of
    /// the block stays in one piece at its far end.
    /// </summary>
    private static List<RowCell> FillBlock(RowMotif m, double x0, double y0, int cols, int rows, int count, bool standing)
    {
      var cells = new List<RowCell>();
      int outer = standing ? rows : cols;
      int inner = standing ? cols : rows;
      for (int a = 0; a < outer && cells.Count < count; a++)
      {
        for (int b = 0; b < inner && cells.Count < count; b++)
        {
          int i = standing ? b : a;
          int j = standing ? a : b;
          var (cx, cy) = m.Cell(i, j);
          cells.Add(new RowCell(m, x0 + cx, y0 + cy, m.Turned(i, j)));
        }
      }

      return cells;
    }

    /// <summary>
    /// The free strips a block leaves in its rectangle: beside it along the sheet's width (a standing sheet) or
    /// over it (a lying one), past it the other way, and the end of a short last row or column.
    /// </summary>
    private static IEnumerable<RowRect> SplitAround(RowRect rect, List<RowCell> block, double gap, bool standing)
    {
      double right = block.Max(c => c.X + c.Motif.W);
      double top = block.Max(c => c.Y + c.Motif.H);
      var parts = new List<RowRect>();
      if (standing)
      {
        parts.Add(new RowRect { L = right + gap, B = rect.B, R = rect.R, T = top });
        parts.Add(new RowRect { L = rect.L, B = top + gap, R = rect.R, T = rect.T });
        // The end of a short last row, under the block's top.
        double lastBottom = block.Max(c => c.Y);
        var lastRow = block.Where(c => c.Y >= lastBottom - 1e-6).ToList();
        double lastRight = lastRow.Max(c => c.X + c.Motif.W);
        double rowsBelowTop = block.Where(c => c.Y < lastBottom - 1e-6).Select(c => c.Y + c.Motif.H).DefaultIfEmpty(rect.B - gap).Max();
        parts.Add(new RowRect { L = lastRight + gap, B = rowsBelowTop + gap, R = right, T = top });
      }
      else
      {
        parts.Add(new RowRect { L = rect.L, B = top + gap, R = right, T = rect.T });
        parts.Add(new RowRect { L = right + gap, B = rect.B, R = rect.R, T = rect.T });
        double lastLeft = block.Max(c => c.X);
        var lastCol = block.Where(c => c.X >= lastLeft - 1e-6).ToList();
        double lastTop = lastCol.Max(c => c.Y + c.Motif.H);
        double colsBeforeEnd = block.Where(c => c.X < lastLeft - 1e-6).Select(c => c.X + c.Motif.W).DefaultIfEmpty(rect.L - gap).Max();
        parts.Add(new RowRect { L = colsBeforeEnd + gap, B = lastTop + gap, R = right, T = top });
      }

      return parts.Where(r => r.Width > 1e-6 && r.Height > 1e-6);
    }

    /// <summary>
    /// The motifs one kind can be laid in: at each allowed orientation, all parts alike, and where it may also
    /// turn round, every other part along the row, every other row, or both turned 180°; each in straight rows
    /// and in rows shifted half a step. The steps are the smallest the grown outlines allow.
    /// </summary>
    private static List<RowMotif> RowMotifs(Loaded part)
    {
      var angles = LatticeAngles(part);
      var motifs = new List<RowMotif>();
      foreach (int rot in angles)
      {
        int back = (rot + 180) % 360;
        var alts = new List<(int, int)> { (0, 0) };
        if (rot < 180 && angles.Contains(back))
        {
          alts.AddRange(new[] { (1, 0), (0, 1), (1, 1) });
        }

        var a = rot == 0 ? part.Nfp : part.Nfp.Rotate(rot);
        var b = part.Nfp.Rotate(back);
        foreach (var (ax, ay) in alts)
        {
          foreach (var m in MotifSteps(part, rot, a, b, ax, ay))
          {
            motifs.Add(m);
          }
        }
      }

      return motifs;
    }

    private static IEnumerable<RowMotif> MotifSteps(Loaded part, int rot, INfp a, INfp b, int ax, int ay)
    {
      double eps = 1e-6 * RowsScale * RowsScale;
      List<IntPoint> Grown(INfp s)
      {
        var path = DeepNestClipper.ScaleUpPath(OffsetOutward(s, part.FitSpacing / 2.0).Points, RowsScale);
        return TranslatePath(path, (long)Math.Round(-s.MinX * RowsScale), (long)Math.Round(-s.MinY * RowsScale));
      }

      var proto = new RowMotif { Part = part, Rot = rot, Ax = ax, Ay = ay, W = a.MaxX - a.MinX, H = a.MaxY - a.MinY };
      proto.Shape[0] = a;
      proto.Shape[1] = b;
      proto.Dil[0] = Grown(a);
      proto.Dil[1] = ax == 0 && ay == 0 ? proto.Dil[0] : Grown(b);
      var boxes = proto.Dil.Select(BoundsOfPath).ToArray();
      double dilW = (Math.Max(boxes[0].MaxX, boxes[1].MaxX) - Math.Min(boxes[0].MinX, boxes[1].MinX)) / RowsScale;
      double dilH = (Math.Max(boxes[0].MaxY, boxes[1].MaxY) - Math.Min(boxes[0].MinY, boxes[1].MinY)) / RowsScale;
      double slack = Math.Max(1e-4, 1e-3 * Math.Max(dilW, dilH));

      bool Clear(RowMotif m, int i1, int j1, int i2, int j2)
      {
        var (x1, y1) = m.Cell(i1, j1);
        var (x2, y2) = m.Cell(i2, j2);
        int o1 = m.Turned(i1, j1), o2 = m.Turned(i2, j2);
        long dx = (long)Math.Round((x2 - x1) * RowsScale), dy = (long)Math.Round((y2 - y1) * RowsScale);
        var b1 = boxes[o1];
        var b2 = boxes[o2];
        if (b2.MinX + dx >= b1.MaxX || b1.MinX >= b2.MaxX + dx || b2.MinY + dy >= b1.MaxY || b1.MinY >= b2.MaxY + dy)
        {
          return true;
        }

        return !Overlaps(m.Dil[o1], TranslatePath(m.Dil[o2], dx, dy), eps);
      }

      bool AlongClear(RowMotif m) => new[] { 0, 1 }.All(i0 => Enumerable.Range(1, 3).All(k => Clear(m, i0, 0, i0 + k, 0)));
      bool AcrossClear(RowMotif m) => new[] { 0, 1 }.All(j0 => new[] { 0, 1 }.All(i0 =>
        Enumerable.Range(i0 - 3, 7).All(i => Clear(m, i0, j0, i, j0 + 1) && Clear(m, i0, j0, i, j0 + 2))));

      double Smallest(Func<double, bool> clearAt, double hi)
      {
        double lo = 0;
        for (int i = 0; i < 24; i++)
        {
          double mid = (lo + hi) / 2;
          if (clearAt(mid))
          {
            hi = mid;
          }
          else
          {
            lo = mid;
          }
        }

        return hi + slack;
      }

      RowMotif With(double px, double py, double shift)
        => new RowMotif { Part = part, Rot = rot, Ax = ax, Ay = ay, W = proto.W, H = proto.H, Shape = proto.Shape, Dil = proto.Dil, Px = px, Py = py, Shift = shift };

      double stepX = Smallest(px => AlongClear(With(px, dilH * 3, 0)), dilW);
      foreach (double shift in new[] { 0.0, stepX / 2 })
      {
        double stepY = Smallest(py => AcrossClear(With(stepX, py, shift)), dilH);
        var m = With(stepX, stepY, shift);
        if (AlongClear(m) && AcrossClear(m))
        {
          yield return m;
        }
      }
    }
  }
}
