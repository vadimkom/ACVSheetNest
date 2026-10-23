namespace DeepNestSharp.RasterNest
{
  using System;
  using System.Collections.Generic;
  using System.Linq;
  using System.Threading;
  using ClipperLib;
  using DeepNestLib;
  using DeepNestLib.Placement;

  /// <summary>
  /// Sheets that are not rectangles: an offcut of any shape, read from a DXF or CHF drawing.
  /// <para>The engine is a strip packer and only knows a rectangle of fixed height, so a shaped sheet is
  /// packed here instead, part by part, with no-fit polygons: for every pose of a part, the set of positions
  /// where its outline stays inside the sheet (less the margin and the sheet's own holes) and its spacing
  /// shell clears every shell already placed, then the position that keeps the pack shortest along the long
  /// side, lowest across it. Exact geometry, no search; a few hundred parts take seconds.</para>
  /// </summary>
  internal static partial class SparrowNestService
  {
    /// <summary>Clipper units per drawing unit on a shaped sheet: 0.0001 mm (or inch).</summary>
    private const double ShapeScale = 1e4;

    /// <summary>Room added to every clearance on a shaped sheet so integer rounding can never land a part a
    /// hair over the line; far below anything a laser can tell apart.</summary>
    private const double ShapeSafety = 0.002;

    /// <summary>
    /// The region a part's own outline has to stay inside on a shaped sheet: the outline shrunk by the
    /// margin, less each hole of the sheet grown by it. Clipper paths at <paramref name="scale"/>, holes
    /// wound the other way.
    /// </summary>
    internal static List<List<IntPoint>> UsableRegion(INfp shape, double margin, double scale)
    {
      var region = new List<List<IntPoint>>();
      if (shape?.Points == null || shape.Points.Length < 3)
      {
        return region;
      }

      double inset = Math.Max(0, margin) * scale;
      var outer = Offset(Ccw(DeepNestClipper.ScaleUpPath(shape.Points, scale)), -inset, scale);
      var holes = new List<List<IntPoint>>();
      foreach (var child in shape.Children ?? new List<INfp>())
      {
        if (child?.Points != null && child.Points.Length >= 3)
        {
          holes.AddRange(Offset(Ccw(DeepNestClipper.ScaleUpPath(child.Points, scale)), inset, scale));
        }
      }

      var c = new Clipper();
      c.AddPaths(outer, PolyType.ptSubject, true);
      c.AddPaths(holes, PolyType.ptClip, true);
      c.Execute(ClipType.ctDifference, region, PolyFillType.pftNonZero, PolyFillType.pftNonZero);
      return region;
    }

    /// <summary>Area of a shaped sheet a part may use is its whole outline less its holes.</summary>
    internal static double ShapeArea(INfp shape)
      => shape == null ? 0 : Math.Abs(shape.Area) - (shape.Children ?? new List<INfp>()).Sum(h => Math.Abs(h.Area));

    /// <summary>
    /// Packs one shaped sheet from what is left in <paramref name="pool"/>: the parts ordered by count first,
    /// by priority and then biggest first, and the parts that fill the sheets after them, as many as go on.
    /// </summary>
    private static (List<IPartPlacement> Placements, Dictionary<int, int> PlacedBySrc) PackShapedSheet(
      Dictionary<int, int> pool,
      Dictionary<int, Loaded> loadedById,
      INfp shape,
      double margin,
      CancellationToken cancel)
    {
      const double scale = ShapeScale;
      var placements = new List<IPartPlacement>();
      var placedBySrc = new Dictionary<int, int>();
      var region = UsableRegion(shape, margin + ShapeSafety, scale);
      if (region.Count == 0)
      {
        return (placements, placedBySrc);
      }

      var bounds = BoundsOfPaths(region);
      bool alongX = (bounds.MaxX - bounds.MinX) >= (bounds.MaxY - bounds.MinY);
      var placed = new List<(ShapedPose Pose, long X, long Y)>();
      var pairNfps = new Dictionary<(ShapedPose Fixed, ShapedPose Moving), List<List<IntPoint>>>();
      long reach = alongX ? bounds.MinX : bounds.MinY; // far edge of the pack along the long side

      var order = pool
        .Where(kv => kv.Value > 0)
        .Select(kv => kv.Key)
        .OrderBy(k => loadedById[k].Fill ? 1 : 0)
        .ThenBy(k => loadedById[k].Priority)
        .ThenByDescending(k => Math.Abs(loadedById[k].Nfp.Area))
        .ThenBy(k => k)
        .ToList();

      foreach (int src in order)
      {
        var loaded = loadedById[src];
        var poses = loaded.Angles
          .Distinct()
          .Select(angle => ShapedPose.Create(loaded, angle, region, scale))
          .Where(p => p.Inner.Count > 0)
          .ToList();

        int left = pool[src];
        while (left > 0 && poses.Count > 0)
        {
          cancel.ThrowIfCancellationRequested();
          ShapedPose bestPose = null;
          (long Reach, long Across, long Along) bestScore = (long.MaxValue, long.MaxValue, long.MaxValue);
          IntPoint best = default;
          foreach (var pose in poses)
          {
            var free = pose.FreeAfter(placed, pairNfps);
            foreach (var path in free)
            {
              foreach (var p in path)
              {
                long along = alongX ? p.X : p.Y;
                long across = alongX ? p.Y : p.X;
                var score = (Math.Max(reach, along + pose.FarAlong(alongX)), across, along);
                if (score.CompareTo(bestScore) < 0)
                {
                  bestScore = score;
                  bestPose = pose;
                  best = p;
                }
              }
            }
          }

          if (bestPose == null)
          {
            break; // no pose of this part goes on any more; smaller parts still may
          }

          placements.Add(new PartPlacement(bestPose.Rotated)
          {
            X = best.X / scale,
            Y = best.Y / scale,
            Rotation = bestPose.Angle,
            Source = src,
            IsMirrored = loaded.Mirrored,
          });
          placed.Add((bestPose, best.X, best.Y));
          reach = bestScore.Reach;
          placedBySrc[src] = placedBySrc.TryGetValue(src, out int n) ? n + 1 : 1;
          left--;
        }
      }

      return (placements, placedBySrc);
    }

    /// <summary><see cref="PackShapedSheet"/> in the shape the sheet loop takes a pack in.</summary>
    private static (List<IPartPlacement>, Dictionary<int, int>, int, int, int, int, List<CommonEdgeIntent>, IReadOnlyList<CommonLineFallbackNotice>) ShapedSheet(
      Dictionary<int, int> pool, Dictionary<int, Loaded> loadedById, INfp shape, int w, int h, double margin, CancellationToken cancel)
    {
      var (placements, placedBySrc) = PackShapedSheet(pool, loadedById, shape, margin, cancel);
      return (placements, placedBySrc, w, h, 0, 0, new List<CommonEdgeIntent>(), Array.Empty<CommonLineFallbackNotice>());
    }

    /// <summary>
    /// Takes back every placement whose outline does not lie inside the shaped sheet (margin and holes
    /// included), whatever moved it there. Returns how many went back to the pool.
    /// </summary>
    private static int DropOutsideShape(List<IPartPlacement> placements, Dictionary<int, int> placedBySrc, INfp shape, double margin)
    {
      int dropped = 0;
      foreach (var pp in placements.ToList())
      {
        if (IsOutsideShape(pp, shape, margin))
        {
          placements.Remove(pp);
          if (placedBySrc.TryGetValue(pp.Source, out int n))
          {
            if (n <= 1)
            {
              placedBySrc.Remove(pp.Source);
            }
            else
            {
              placedBySrc[pp.Source] = n - 1;
            }
          }

          dropped++;
        }
      }

      return dropped;
    }

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<INfp, Tuple<double, List<List<IntPoint>>>> RegionCache = new();

    /// <summary>
    /// Whether a placed part leaves the metal of a shaped sheet or comes within the margin of its edge or of a
    /// hole in it: the rule the editor paints red by. Always false on a rectangle, whose edges the box test
    /// already judges.
    /// </summary>
    internal static bool IsOutsideShape(IPartPlacement pp, INfp sheet, double margin)
    {
      if (pp?.PlacedPart?.Points == null || !DeepNestLib.IO.SheetShape.IsShaped(sheet))
      {
        return false;
      }

      const double scale = ShapeScale;
      double allowed = Math.Max(0, margin - ShapeSafety);
      List<List<IntPoint>> region;
      lock (RegionCache)
      {
        if (!RegionCache.TryGetValue(sheet, out var cached) || cached.Item1 != allowed)
        {
          cached = Tuple.Create(allowed, UsableRegion(sheet, allowed, scale));
          RegionCache.AddOrUpdate(sheet, cached);
        }

        region = cached.Item2;
      }

      var outline = Ccw(DeepNestClipper.ScaleUpPath(pp.PlacedPart.Points, scale));
      var c = new Clipper();
      c.AddPath(outline, PolyType.ptSubject, true);
      c.AddPaths(region, PolyType.ptClip, true);
      var outside = new List<List<IntPoint>>();
      c.Execute(ClipType.ctDifference, outside, PolyFillType.pftNonZero, PolyFillType.pftNonZero);
      return outside.Sum(o => Math.Abs(Clipper.Area(o))) > 1e-4 * scale * scale;
    }

    /// <summary>The sheet a layout is drawn and saved on: the drawing's outline and holes, or a rectangle.</summary>
    private static Sheet MakeSheet(int number, int w, int h, INfp shape)
    {
      if (shape == null)
      {
        return Sheet.NewSheet(number, w, h);
      }

      return new Sheet(shape, WithChildren.Included) { Name = "shapeSheet" + number };
    }

    private static List<IntPoint> Ccw(List<IntPoint> path)
    {
      if (Clipper.Area(path) < 0)
      {
        path.Reverse();
      }

      return path;
    }

    private static List<List<IntPoint>> Offset(List<IntPoint> path, double delta, double scale)
    {
      if (Math.Abs(delta) < 1e-9)
      {
        return new List<List<IntPoint>> { path };
      }

      var co = new ClipperOffset(4, SvgNest.Config.CurveTolerance * scale);
      co.AddPath(path, JoinType.jtMiter, EndType.etClosedPolygon);
      var solution = new List<List<IntPoint>>();
      co.Execute(ref solution, delta);
      return solution;
    }

    private static (long MinX, long MinY, long MaxX, long MaxY) BoundsOfPaths(List<List<IntPoint>> paths)
    {
      long minX = long.MaxValue, minY = long.MaxValue, maxX = long.MinValue, maxY = long.MinValue;
      foreach (var p in paths.SelectMany(path => path))
      {
        minX = Math.Min(minX, p.X);
        maxX = Math.Max(maxX, p.X);
        minY = Math.Min(minY, p.Y);
        maxY = Math.Max(maxY, p.Y);
      }

      return (minX, minY, maxX, maxY);
    }

    private static List<IntPoint> Negate(List<IntPoint> path)
      => path.Select(p => new IntPoint(-p.X, -p.Y)).ToList();

    /// <summary>
    /// One part at one angle on a shaped sheet. Positions are the shift applied to the turned outline, the
    /// same as <see cref="PartPlacement.X"/>/<see cref="PartPlacement.Y"/>.
    /// </summary>
    private sealed class ShapedPose
    {
      private List<List<IntPoint>> blocked = new List<List<IntPoint>>();
      private int blockedBy;

      public int Angle { get; private set; }

      public INfp Rotated { get; private set; }

      /// <summary>The spacing shell, half the spacing round the turned outline, wound counter-clockwise.</summary>
      public List<IntPoint> Shell { get; private set; }

      /// <summary>Where the shift may put the part with its outline inside the sheet: the inner-fit region.</summary>
      public List<List<IntPoint>> Inner { get; private set; }

      private long MaxX { get; set; }

      private long MaxY { get; set; }

      public static ShapedPose Create(Loaded loaded, int angle, List<List<IntPoint>> region, double scale)
      {
        var rotated = Math.Abs(angle % 360) < 1e-9 ? loaded.Nfp : loaded.Nfp.Rotate(angle);
        var outline = Ccw(DeepNestClipper.ScaleUpPath(rotated.Points, scale));
        var shellNfp = OffsetOutward(rotated, (loaded.FitSpacing + ShapeSafety) / 2.0);
        var shell = Ccw(DeepNestClipper.ScaleUpPath(shellNfp.Points, scale));

        // The outline lies inside the region exactly when one of its points does, its edges cross none of the
        // region's, and no boundary of the region (a hole of the sheet) lies wholly inside it:
        //   (region - p0) - (boundary (+) -outline) - (each boundary's first point - outline).
        var neg = Negate(outline);
        var c = new Clipper();
        c.AddPaths(region.Select(r => Translate(r, -outline[0].X, -outline[0].Y)).ToList(), PolyType.ptSubject, true);
        foreach (var boundary in region)
        {
          c.AddPaths(Clipper.MinkowskiSum(neg, boundary, true), PolyType.ptClip, true);
          c.AddPath(Translate(neg, boundary[0].X, boundary[0].Y), PolyType.ptClip, true);
        }

        var inner = new List<List<IntPoint>>();
        c.Execute(ClipType.ctDifference, inner, PolyFillType.pftNonZero, PolyFillType.pftNonZero);

        return new ShapedPose
        {
          Angle = angle,
          Rotated = rotated,
          Shell = shell,
          Inner = inner,
          MaxX = outline.Max(p => p.X),
          MaxY = outline.Max(p => p.Y),
        };
      }

      /// <summary>How far past the shift the outline reaches along the long side of the sheet.</summary>
      public long FarAlong(bool alongX) => alongX ? this.MaxX : this.MaxY;

      /// <summary>The inner-fit region less every position where this pose's shell would overlap a shell already
      /// placed. The blocked area is kept and only grown by the parts placed since the last call.</summary>
      public List<List<IntPoint>> FreeAfter(List<(ShapedPose Pose, long X, long Y)> placed, Dictionary<(ShapedPose Fixed, ShapedPose Moving), List<List<IntPoint>>> pairNfps)
      {
        if (this.blockedBy < placed.Count)
        {
          var u = new Clipper();
          u.AddPaths(this.blocked, PolyType.ptSubject, true);
          for (int i = this.blockedBy; i < placed.Count; i++)
          {
            var (other, x, y) = placed[i];
            if (!pairNfps.TryGetValue((other, this), out var nfp))
            {
              nfp = PairNfp(other.Shell, this.Shell);
              pairNfps[(other, this)] = nfp;
            }

            u.AddPaths(nfp.Select(path => Translate(path, x, y)).ToList(), PolyType.ptClip, true);
          }

          var merged = new List<List<IntPoint>>();
          u.Execute(ClipType.ctUnion, merged, PolyFillType.pftNonZero, PolyFillType.pftNonZero);
          this.blocked = merged;
          this.blockedBy = placed.Count;
        }

        if (this.blocked.Count == 0)
        {
          return this.Inner;
        }

        var c = new Clipper();
        c.AddPaths(this.Inner, PolyType.ptSubject, true);
        c.AddPaths(this.blocked, PolyType.ptClip, true);
        var free = new List<List<IntPoint>>();
        c.Execute(ClipType.ctDifference, free, PolyFillType.pftNonZero, PolyFillType.pftNonZero);
        return free;
      }

      /// <summary>Shifts of <paramref name="moving"/> at which it overlaps <paramref name="fixedShell"/> at rest:
      /// fixed (+) -moving, built as the sum of the boundaries plus one copy of each shape.</summary>
      private static List<List<IntPoint>> PairNfp(List<IntPoint> fixedShell, List<IntPoint> moving)
      {
        var neg = Negate(moving);
        var c = new Clipper();
        c.AddPaths(Clipper.MinkowskiSum(neg, fixedShell, true), PolyType.ptSubject, true);
        c.AddPath(Translate(fixedShell, neg[0].X, neg[0].Y), PolyType.ptClip, true);
        c.AddPath(Translate(neg, fixedShell[0].X, fixedShell[0].Y), PolyType.ptClip, true);
        var nfp = new List<List<IntPoint>>();
        c.Execute(ClipType.ctUnion, nfp, PolyFillType.pftNonZero, PolyFillType.pftNonZero);
        return nfp;
      }

      private static List<IntPoint> Translate(List<IntPoint> path, long dx, long dy) => TranslatePath(path, dx, dy);
    }
  }
}
