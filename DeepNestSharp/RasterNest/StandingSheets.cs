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
  }
}
