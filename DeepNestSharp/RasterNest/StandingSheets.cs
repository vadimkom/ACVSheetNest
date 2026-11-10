namespace DeepNestSharp.RasterNest
{
  using System;
  using System.Collections.Generic;
  using System.Linq;
  using DeepNestLib;
  using DeepNestLib.IO;
  using DeepNestLib.Placement;

  /// <summary>
  /// A standing sheet (1250 × 2500, a 360 × 1890 strip) is packed from the bottom-left corner up, not in
  /// columns from left to right. Sparrow is a strip packer: it fills a strip of the sheet's height and
  /// shortens it along X, so a sheet narrower than it is tall came out in columns over its whole height
  /// with the free metal a narrow strip down the side (the shop's job of 2026-11-03: two columns of a
  /// 360 mm sheet used, 1860 of 1890 mm long). Such a job is nested turned a quarter turn clockwise, every
  /// part turned with it, and the result turned back: the parts keep the rotations they would have had, the
  /// sheets their sizes, and the spare metal is left in one piece across the top.
  /// </summary>
  internal static partial class SparrowNestService
  {
    /// <summary>
    /// Whether the job is nested turned: most of its stock stands (by area, an unlimited size counted once).
    /// Never with common-line parts, whose planner reads its edges back from the drawings as they lie.
    /// </summary>
    private static bool NestTurned(List<StockSize> sizes, List<Loaded> loaded)
    {
      if (loaded.Any(l => l.RequiresMergedLines))
      {
        return false;
      }

      double standing = 0;
      foreach (var s in sizes)
      {
        double area = (double)s.W * s.H * Math.Max(1, s.Unlimited ? 1 : s.Remaining);
        standing += s.W < s.H ? area : s.W > s.H ? -area : 0;
      }

      return standing > 0;
    }

    /// <summary>
    /// Turns the job a quarter turn clockwise for the engine: the sheets lie, a sheet's outline turns with
    /// it, and every part's geometry is turned so that a part placed at angle a on the turned sheet is at
    /// angle a on the sheet as it stands. Returns each part's own geometry, by source.
    /// </summary>
    private static Dictionary<int, INfp> TurnForEngine(List<StockSize> sizes, List<Loaded> loaded)
    {
      foreach (var s in sizes)
      {
        (s.W, s.H) = (s.H, s.W);
        if (s.Shape != null)
        {
          s.Shape = SheetShape.Normalise(SheetShape.Turned(s.Shape, -1));
        }
      }

      var upright = new Dictionary<int, INfp>();
      foreach (var l in loaded)
      {
        upright[l.Source] = l.Nfp;
        l.Nfp = l.Nfp.Rotate(-90);
        l.Dilated = l.Dilated?.Rotate(-90);
        l.AngleExtents = l.Angles
          .Select(angle =>
          {
            INfp turned = angle == 0 ? l.Nfp : l.Nfp.Rotate(angle);
            return (turned.MaxX - turned.MinX, turned.MaxY - turned.MinY);
          })
          .ToArray();
      }

      return upright;
    }

    /// <summary>
    /// The result of a turned nest as the sheets stand: each sheet turned a quarter turn back (a point
    /// (x, y) of the lying sheet goes to (W − y, x), W the standing sheet's width), each part redrawn from
    /// its own geometry at the angle it was placed at, unplaced parts given back as they were loaded. With
    /// <paramref name="settle"/> the parts are then slid to the bottom-left corner.
    /// </summary>
    private static INestResult TurnedBack(INestResult turned, Dictionary<int, INfp> upright, IReadOnlyDictionary<INfp, INfp> uprightByTurned, (IReadOnlyDictionary<int, Loaded> Loaded, double Margin)? settle)
    {
      if (turned == null)
      {
        return null;
      }

      var collection = new SheetPlacementCollection();
      int number = 0;
      foreach (var sp in turned.UsedSheets)
      {
        number++;
        var engineSheet = sp.Sheet;
        double w = engineSheet.MaxY - engineSheet.MinY;
        ISheet sheet = SheetShape.IsShaped(engineSheet)
          ? new Sheet(SheetShape.Normalise(SheetShape.Turned(engineSheet, 1)), WithChildren.Included) { Name = engineSheet.Name }
          : Sheet.NewSheet(number, (int)Math.Round(w), (int)Math.Round(engineSheet.MaxX - engineSheet.MinX));
        var parts = new List<IPartPlacement>();
        foreach (var p in sp.PartPlacements)
        {
          var own = upright.TryGetValue(p.Source, out var u) ? u : p.Part.Rotate(90);
          var rotated = Math.Abs(p.Rotation % 360) < 1e-9 ? own : own.Rotate(p.Rotation);
          parts.Add(new PartPlacement(rotated)
          {
            Id = p.Id,
            X = w - p.Y,
            Y = p.X,
            Rotation = p.Rotation,
            Source = p.Source,
            IsMirrored = p.IsMirrored,
          });
        }

        // The turn takes the engine's bottom edge to the right-hand one: slide the parts left (then down) to
        // the corner. Not on a sheet of another shape, whose edge the slide does not know; its own packer
        // lays parts against the edge from several sides anyway.
        if (settle != null && !SheetShape.IsShaped(sheet) && parts.Count > 0)
        {
          void Slide()
          {
            var items = parts.Select(pp => new CompactItem
            {
              Poly = pp.Part,
              X = pp.X,
              Y = pp.Y,
              Spacing = settle.Value.Loaded.TryGetValue(pp.Source, out var l) ? l.EffSpacing : 0,
            }).ToList();
            RasterCompact.Compact(items, sheet.MaxX - sheet.MinX, sheet.MaxY - sheet.MinY, settle.Value.Margin);
            for (int k = 0; k < parts.Count; k++)
            {
              parts[k].X = items[k].X;
              parts[k].Y = items[k].Y;
            }
          }

          Slide();
          double gap = parts.Max(pp => settle.Value.Loaded.TryGetValue(pp.Source, out var l) ? Math.Max(l.EffSpacing, l.FitSpacing) : 0);
          TallColumnsFirst(parts, gap, settle.Value.Margin);

          // Laid again by boxes on a grid, the parts stand up to a cell further apart than they need: slid
          // together again on their shapes.
          if (TidyOffcut(parts, settle.Value.Loaded, upright, gap, (int)Math.Round(sheet.MaxX - sheet.MinX), (int)Math.Round(sheet.MaxY - sheet.MinY), settle.Value.Margin))
          {
            Slide();
          }
        }

        collection.Add(new SheetPlacement(
          placementType: sp.PlacementType,
          sheet: sheet,
          partPlacements: parts,
          mergedLength: sp.MergedLength,
          clipperScale: SvgNest.Config.ClipperScale));
      }

      var unplaced = turned.UnplacedParts.Select(n => uprightByTurned.TryGetValue(n, out var u) ? u : n).ToList();
      return new NestResult(turned.TotalParts, collection, unplaced, PlacementTypeEnum.BoundingBox, 0, 0);
    }

    /// <summary>
    /// The columns of a sheet, left to right: runs of parts whose boxes overlap across. Two columns are told
    /// apart only where their boxes stand at least <paramref name="gap"/> apart, so each stays clear of the
    /// others wherever it is moved across.
    /// </summary>
    private static List<(double MinX, double MaxX, double Top, List<IPartPlacement> Parts)> Columns(IEnumerable<IPartPlacement> parts, double gap)
    {
      var columns = new List<(double MinX, double MaxX, double Top, List<IPartPlacement> Parts)>();
      foreach (var p in parts.OrderBy(p => p.MinX))
      {
        if (columns.Count > 0 && p.MinX < columns[^1].MaxX + gap - 1e-6)
        {
          var c = columns[^1];
          c.Parts.Add(p);
          columns[^1] = (c.MinX, Math.Max(c.MaxX, p.MaxX), Math.Max(c.Top, p.MaxY), c.Parts);
        }
        else
        {
          columns.Add((p.MinX, p.MaxX, p.MaxY, new List<IPartPlacement> { p }));
        }
      }

      return columns;
    }

    /// <summary>
    /// Stands the columns of a standing sheet tallest first, from the left: the metal left over the parts is
    /// then a staircase (a strip across the top and a step down at the right) instead of a comb of gaps over
    /// the short columns. The door job (2026-11-10): strips 1915 and 1775 long, each with a panel over it,
    /// came out tall, short, short, tall; the operator lays the two tall ones first and the two short ones
    /// after. The columns are set side by side from the left margin <paramref name="gap"/> apart, each at its
    /// own height. Returns whether anything moved.
    /// </summary>
    private static bool TallColumnsFirst(List<IPartPlacement> parts, double gap, double margin)
    {
      var columns = Columns(parts, gap);
      if (columns.Count < 2)
      {
        return false;
      }

      // Heights within half a millimetre are one height; those keep the order they had.
      var order = columns.OrderByDescending(c => Math.Round(c.Top * 2) / 2).ToList();
      if (order.SequenceEqual(columns))
      {
        return false;
      }

      double x = Math.Max(margin, columns[0].MinX);
      foreach (var c in order)
      {
        double shift = x - c.MinX;
        foreach (var p in c.Parts)
        {
          p.X += shift;
        }

        x = c.MaxX + shift + gap;
      }

      return true;
    }

    /// <summary>Most parts a standing sheet is laid again by boxes for a cleaner offcut.</summary>
    private const int MaxTidied = 30;

    /// <summary>
    /// Lays each column of a standing sheet again by boxes from the bottom-left corner, its widest parts first
    /// (or its tallest), the rest of the sheet staying as it is, and keeps the layout whose largest empty
    /// rectangle is the biggest, when all the column's parts go back on and that piece grows by a twentieth.
    /// The door job (2026-11-10): beside the two tall columns the operator puts the two wide panels side by
    /// side at the bottom and the two narrow 1775 strips over them, which leaves 190 × 2060 clean at the
    /// right; the strips standing with the panels over them left a sliver. Boxes are kept a spacing apart, so
    /// whatever the shapes nothing comes closer; parts that interlock lose that by boxes, and such a layout
    /// seldom wins. Returns whether anything was laid again.
    /// </summary>
    private static bool TidyOffcut(List<IPartPlacement> parts, IReadOnlyDictionary<int, Loaded> loaded, Dictionary<int, INfp> upright, double gap, int w, int h, double margin)
    {
      if (parts.Count < 2 || parts.Count > MaxTidied
        || parts.Any(p => !loaded.TryGetValue(p.Source, out var l) || l.RequiresMergedLines || !upright.ContainsKey(p.Source)))
      {
        return false;
      }

      var columns = Columns(parts, gap);
      if (columns.Count < 2)
      {
        return false;
      }

      bool changed = false;
      foreach (var column in columns.Where(c => c.Parts.Count > 1))
      {
        var others = parts.Except(column.Parts).ToList();
        double bestScore = new FreeRoom(parts, gap, w, h, margin).LargestEmpty() * 1.05;
        List<IPartPlacement> best = null;
        var orders = new[]
        {
          column.Parts.OrderByDescending(p => Math.Round(p.MaxX - p.MinX)).ThenByDescending(p => p.MaxY - p.MinY).ThenBy(p => p.Source).ToList(),
          column.Parts.OrderByDescending(p => Math.Round(p.MaxY - p.MinY)).ThenByDescending(p => p.MaxX - p.MinX).ThenBy(p => p.Source).ToList(),
        };
        foreach (var order in orders)
        {
          for (int rule = 0; rule < 4; rule++)
          {
            // A column laid again stands no taller than it did, so the tops still step down to the right; two
            // cells over are the grid's rounding, which the slide afterwards takes back.
            var laid = Lay(others, order, rule);
            double score = laid == null || laid.Max(p => p.MaxY) > column.Top + (2.0 * Math.Max(w, h) / FreeRoomCells)
              ? -1
              : new FreeRoom(others.Concat(laid), gap, w, h, margin).LargestEmpty();
            if (score > bestScore)
            {
              (best, bestScore) = (laid, score);
            }
          }
        }

        if (best != null)
        {
          parts.RemoveAll(p => column.Parts.Contains(p));
          parts.AddRange(best);
          changed = true;
        }
      }

      return changed;

      // The parts in this order, each where the rule puts its box; null when one does not go on.
      List<IPartPlacement> Lay(List<IPartPlacement> fixedParts, List<IPartPlacement> order, int rule)
      {
        var layout = new List<IPartPlacement>(fixedParts);
        var laid = new List<IPartPlacement>();
        foreach (var p in order)
        {
          var spot = SpotFor(upright[p.Source], LatticeAngles(loaded[p.Source]), new FreeRoom(layout, gap, w, h, margin), rule);
          if (spot == null)
          {
            return null;
          }

          var placed = new PartPlacement(spot.Value.Turned)
          {
            Id = p.Id,
            X = spot.Value.X,
            Y = spot.Value.Y,
            Rotation = spot.Value.Angle,
            Source = p.Source,
            IsMirrored = p.IsMirrored,
          };
          layout.Add(placed);
          laid.Add(placed);
        }

        return laid;
      }
    }
  }
}
