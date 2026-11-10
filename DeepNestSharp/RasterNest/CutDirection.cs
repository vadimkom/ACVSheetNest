namespace DeepNestSharp.RasterNest
{
  using System;
  using System.Collections.Generic;
  using System.Linq;
  using DeepNestLib;
  using DeepNestLib.IO;
  using DeepNestLib.Placement;

  /// <summary>
  /// Nesting from the right (2026-11-10, the user: "our laser cuts from right to left"). Every packing step
  /// here lays parts from the left, so the job is mirrored for the engine: each part's outline and each
  /// shaped sheet flipped left for right, the allowed angles turned the other way. The result is mirrored
  /// back, and since a mirror twice over is no mirror, each part comes back as drawn: a part the engine
  /// set at angle a on the mirrored sheet stands at −a on the sheet, its box where the mirror puts it.
  /// The whole layout is the mirror image of an ordinary one: packed from the bottom-right corner, the
  /// offcut on the left. Common-line jobs are nested as they lie: the planner reads its edges back from
  /// the drawings.
  /// </summary>
  internal static partial class SparrowNestService
  {
    /// <summary>
    /// Mirrors the job for the engine, left for right, and returns what turns a result of it back, or null
    /// when the job is nested as it lies.
    /// </summary>
    private static Func<INestResult, INestResult> MirrorForEngine(List<StockSize> sizes, List<Loaded> loaded)
    {
      if (loaded.Count == 0 || loaded.Any(l => l.RequiresMergedLines))
      {
        return null;
      }

      foreach (var s in sizes)
      {
        if (s.Shape != null)
        {
          s.Shape = SheetShape.Normalise(s.Shape.MirrorX());
        }
      }

      var own = new Dictionary<int, INfp>();
      var ownByMirrored = new Dictionary<INfp, INfp>(ReferenceEqualityComparer.Instance);
      foreach (var l in loaded)
      {
        if (!own.ContainsKey(l.Source))
        {
          own[l.Source] = l.Nfp;
        }

        var mirrored = l.Nfp.MirrorX();
        ownByMirrored[mirrored] = l.Nfp;
        l.Nfp = mirrored;
        l.Dilated = l.Dilated?.MirrorX();
        l.Angles = l.Angles?.Select(a => ((-a % 360) + 360) % 360).ToArray();
        if (l.Angles != null)
        {
          l.AngleExtents = l.Angles
            .Select(angle =>
            {
              INfp turned = angle == 0 ? l.Nfp : l.Nfp.Rotate(angle);
              return (turned.MaxX - turned.MinX, turned.MaxY - turned.MinY);
            })
            .ToArray();
        }
      }

      return r => MirroredBack(r, own, ownByMirrored);
    }

    /// <summary>A result of the mirrored job as the sheets lie: every sheet and placement mirrored back.</summary>
    private static INestResult MirroredBack(INestResult mirrored, Dictionary<int, INfp> own, IReadOnlyDictionary<INfp, INfp> ownByMirrored)
    {
      if (mirrored == null)
      {
        return null;
      }

      var collection = new SheetPlacementCollection();
      foreach (var sp in mirrored.UsedSheets)
      {
        var engineSheet = sp.Sheet;
        double left = engineSheet.MinX;
        double right = engineSheet.MaxX;
        ISheet sheet = SheetShape.IsShaped(engineSheet)
          ? new Sheet(SheetShape.Normalise(engineSheet.MirrorX()), WithChildren.Included) { Name = engineSheet.Name }
          : engineSheet;
        var parts = new List<IPartPlacement>();
        foreach (var p in sp.PartPlacements)
        {
          double angle = ((-p.Rotation % 360) + 360) % 360;
          var drawn = own.TryGetValue(p.Source, out var o) ? o : p.Part.MirrorX();
          var turned = Math.Abs(angle) < 1e-9 ? drawn : drawn.Rotate(angle);
          parts.Add(new PartPlacement(turned)
          {
            Id = p.Id,
            X = left + right - p.MaxX - turned.MinX,
            Y = p.MinY - turned.MinY,
            Rotation = angle,
            Source = p.Source,
            IsMirrored = p.IsMirrored,
          });
        }

        collection.Add(new SheetPlacement(
          placementType: sp.PlacementType,
          sheet: sheet,
          partPlacements: parts,
          mergedLength: sp.MergedLength,
          clipperScale: SvgNest.Config.ClipperScale));
      }

      var unplaced = mirrored.UnplacedParts.Select(n => ownByMirrored.TryGetValue(n, out var u) ? u : n).ToList();
      return new NestResult(mirrored.TotalParts, collection, unplaced, PlacementTypeEnum.BoundingBox, 0, 0);
    }
  }
}
