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

    /// <summary>Time the last shaped sheet spent on the poses (inner-fit regions), and their sizes (diagnostics).</summary>
    internal static long DiagShapedPosesMs;

    internal static string DiagShapedPoints;


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
    /// <para>Which way the parts settle decides how many go on a round or ragged offcut, and no one way wins
    /// everywhere: along the long side, along the short one, or out against the edge. All are packed (the
    /// costly geometry is shared between them) and the one that puts on the most is kept.</para>
    /// </summary>
    private static (List<IPartPlacement> Placements, Dictionary<int, int> PlacedBySrc) PackShapedSheet(
      Dictionary<int, int> pool,
      Dictionary<int, Loaded> loadedById,
      INfp shape,
      double margin,
      CancellationToken cancel)
    {
      const double scale = ShapeScale;
      var exact = UsableRegion(shape, margin + ShapeSafety, scale);
      if (exact.Count == 0)
      {
        return (new List<IPartPlacement>(), new Dictionary<int, int>());
      }

      // Curves come as hundreds of short chords, and a no-fit polygon costs the product of the two point
      // counts. Every outline is simplified by a hair (a quarter of a millimetre on a metre of sheet), always to the safe
      // side: parts and their shells grown first, the sheet shrunk first.
      var bounds = BoundsOfPaths(exact);
      double diagonal = Math.Sqrt(Math.Pow(bounds.MaxX - bounds.MinX, 2) + Math.Pow(bounds.MaxY - bounds.MinY, 2));
      long delta = Math.Max(1, (long)(diagonal * 2.5e-4));
      var region = Simplified(OffsetAll(exact, -delta), delta);
      if (region.Count == 0)
      {
        return (new List<IPartPlacement>(), new Dictionary<int, int>());
      }

      var order = pool
        .Where(kv => kv.Value > 0)
        .Select(kv => kv.Key)
        .OrderBy(k => loadedById[k].Fill ? 1 : 0)
        .ThenBy(k => loadedById[k].Priority)
        .ThenByDescending(k => Math.Abs(loadedById[k].Nfp.Area))
        .ThenBy(k => k)
        .ToList();

      var jobs = order.SelectMany(src => loadedById[src].Angles.Distinct().Select(angle => (Src: src, Angle: angle))).ToList();
      var posesWatch = System.Diagnostics.Stopwatch.StartNew();
      var built = new ShapedPose[jobs.Count];
      System.Threading.Tasks.Parallel.For(
        0,
        jobs.Count,
        new System.Threading.Tasks.ParallelOptions { CancellationToken = cancel },
        i => built[i] = ShapedPose.Create(loadedById[jobs[i].Src], jobs[i].Angle, region, scale, delta));
      var poses = order.ToDictionary(src => src, src => built.Where((p, i) => jobs[i].Src == src && p.Inner.Count > 0).ToList());

      DiagShapedPosesMs = posesWatch.ElapsedMilliseconds;
      DiagShapedPoints = $"region {region.Sum(r => r.Count)}, parts {string.Join("/", built.Take(1).Select(p => p.Shell.Count + "+" + p.Outline.Count))}";
      var pairNfps = new System.Collections.Concurrent.ConcurrentDictionary<(ShapedPose Fixed, ShapedPose Moving), List<List<IntPoint>>>();
      // The order the copies go on in. Biggest first leaves the small ones the gaps; taking the sizes in turn
      // can be the only way the last small one goes on. A kit of fillers always goes on in turn, one of each:
      // filled biggest first, the first member took all the room on a round offcut, and the kit was evened
      // down to one of each where the same two parts by count went on three and two.
      double Area(int src) => Math.Abs(loadedById[src].Nfp.Area);
      var counted = order.Where(src => !loadedById[src].Fill).ToList();
      var fillers = order.Where(src => loadedById[src].Fill).ToList();
      bool kit = fillers.Count > 1 && fillers.All(src => loadedById[src].Kit);
      var fillerSequence = kit
        ? InTurn(fillers, pool)
        : fillers.SelectMany(src => Enumerable.Repeat(src, pool[src])).ToList();
      var sequences = new List<List<int>>
      {
        counted.SelectMany(src => Enumerable.Repeat(src, pool[src])).Concat(fillerSequence).ToList(),
      };
      var turns = counted.GroupBy(src => loadedById[src].Priority).SelectMany(g => InTurn(g.ToList(), pool)).Concat(fillerSequence).ToList();
      if (!turns.SequenceEqual(sequences[0]))
      {
        sequences.Add(turns);
      }

      // Kept: the most of the counted parts, then the most filling, a kit counted as whole kits.
      double Worth(Dictionary<int, int> bySrc)
      {
        int Got(int src) => bySrc.TryGetValue(src, out int n) ? n : 0;
        double countedArea = counted.Sum(src => Got(src) * Area(src));
        double fillArea = kit ? fillers.Min(Got) * fillers.Sum(Area) : fillers.Sum(src => Got(src) * Area(src));
        return (countedArea * 1e6) + fillArea;
      }

      List<IPartPlacement> bestPlacements = null;
      Dictionary<int, int> bestBySrc = null;
      List<(ShapedPose Pose, long X, long Y)> bestPlaced = null;
      double bestWorth = -1;
      foreach (var sequence in sequences)
      {
        foreach (ShapedGravity gravity in Enum.GetValues(typeof(ShapedGravity)))
        {
          var (placements, bySrc, placed) = PackShapedOnce(loadedById, sequence, kit ? fillers : null, poses, pairNfps, exact, bounds, gravity, scale, cancel);
          double worth = Worth(bySrc);
          if (worth > bestWorth + 1e-6)
          {
            bestWorth = worth;
            bestPlacements = placements;
            bestBySrc = bySrc;
            bestPlaced = placed;
          }
        }
      }

      // One part filling what is left (washers): rows, straight or half a step shifted, as on a rectangle. On
      // the shop's round offcut the part-by-part pack scattered 665 washers where shifted rows hold about 745,
      // and rows are what an operator lays by hand and a laser cuts in order. Kept when they seat at least as
      // many as the pack did.
      if (fillers.Count == 1 && poses[fillers[0]].Count > 0)
      {
        int filler = fillers[0];
        var around = bestPlaced.Where(p => !poses[filler].Contains(p.Pose)).ToList();
        int packed = bestBySrc.TryGetValue(filler, out int n) ? n : 0;
        var rows = ShapedRows(loadedById[filler], pool[filler], poses[filler], around, pairNfps, exact, scale, cancel);
        if (rows.Count > 0 && rows.Count >= packed)
        {
          bestPlacements = bestPlacements.Where(p => p.Source != filler).ToList();
          foreach (var (pose, at) in rows)
          {
            bestPlacements.Add(new PartPlacement(pose.Rotated)
            {
              X = at.X / scale,
              Y = at.Y / scale,
              Rotation = pose.Angle,
              Source = filler,
              IsMirrored = loadedById[filler].Mirrored,
            });
          }

          bestBySrc[filler] = rows.Count;
        }
      }

      return (bestPlacements, bestBySrc);
    }

    /// <summary>
    /// One part laid in rows on a shaped sheet, around the parts already on it: every orientation, straight
    /// and half-shifted rows at the smallest steps its spacing allows (<see cref="LatticeSteps"/>), and the
    /// grid slid over an eighth of a step at a time, since on a curved edge where the rows start decides how
    /// many fit. A position counts when it lies in the pose's free region (on the metal, clear of the parts
    /// placed); the grid that seats most is kept, each of its positions checked on the real shapes.
    /// </summary>
    private static List<(ShapedPose Pose, IntPoint At)> ShapedRows(
      Loaded part,
      int max,
      List<ShapedPose> partPoses,
      List<(ShapedPose Pose, long X, long Y)> placed,
      System.Collections.Concurrent.ConcurrentDictionary<(ShapedPose Fixed, ShapedPose Moving), List<List<IntPoint>>> pairNfps,
      List<List<IntPoint>> exact,
      double scale,
      CancellationToken cancel)
    {
      const int slides = 8;
      var gate = new object();
      (int Count, ShapedPose Pose, double Px, double Py, double Shift, double Ox, double Oy, (long MinX, long MinY, long MaxX, long MaxY) Box) best = default;
      (int, double, int, int) bestKey = (int.MaxValue, 0, 0, 0); // ties go to the lowest angle, straight rows, least slide: the same answer on any core count
      var free = new System.Collections.Concurrent.ConcurrentDictionary<ShapedPose, List<(List<IntPoint> Path, (long MinX, long MinY, long MaxX, long MaxY) Box, int Sign)>>();

      bool Inside(List<(List<IntPoint> Path, (long MinX, long MinY, long MaxX, long MaxY) Box, int Sign)> region, IntPoint p)
      {
        int winding = 0;
        foreach (var (path, box, sign) in region)
        {
          if (p.X >= box.MinX && p.X <= box.MaxX && p.Y >= box.MinY && p.Y <= box.MaxY && Clipper.PointInPolygon(p, path) != 0)
          {
            winding += sign;
          }
        }

        return winding > 0;
      }

      IEnumerable<IntPoint> Grid((long MinX, long MinY, long MaxX, long MaxY) box, double px, double py, double shift, double ox, double oy)
      {
        int row = 0;
        for (double y = box.MinY + oy; y <= box.MaxY; y += py, row++)
        {
          for (double x = box.MinX + ox + ((row & 1) == 1 ? shift : 0); x <= box.MaxX; x += px)
          {
            yield return new IntPoint((long)Math.Round(x), (long)Math.Round(y));
          }
        }
      }

      var jobs = partPoses
        .Where(pose => LatticeAngles(part).Contains(pose.Angle))
        .SelectMany(pose => LatticeSteps(pose.Rotated, part.FitSpacing).Select(s => (Pose: pose, Px: s.Px * scale, Py: s.Py * scale, Shift: s.Shift * scale)))
        .ToList();
      System.Threading.Tasks.Parallel.ForEach(jobs, new System.Threading.Tasks.ParallelOptions { CancellationToken = cancel }, job =>
      {
        var region = free.GetOrAdd(job.Pose, pose => pose.FreeAfter(new ShapedPose.Blocked(), placed, pairNfps)
          .Select(path => (path, BoundsOfPaths(new List<List<IntPoint>> { path }), Clipper.Orientation(path) ? 1 : -1))
          .ToList());
        if (region.Count == 0)
        {
          return;
        }

        var box = BoundsOfPaths(region.Select(r => r.Path).ToList());
        for (int sx = 0; sx < slides; sx++)
        {
          for (int sy = 0; sy < slides; sy++)
          {
            double ox = job.Px * sx / slides, oy = job.Py * sy / slides;
            int count = Grid(box, job.Px, job.Py, job.Shift, ox, oy).Count(p => Inside(region, p));
            lock (gate)
            {
              var key = (job.Pose.Angle, job.Shift, sx, sy);
              if (count > best.Count || (count == best.Count && count > 0 && key.CompareTo(bestKey) < 0))
              {
                best = (count, job.Pose, job.Px, job.Py, job.Shift, ox, oy, box);
                bestKey = key;
              }
            }
          }
        }
      });

      var rows = new List<(ShapedPose Pose, IntPoint At)>();
      if (best.Pose == null)
      {
        return rows;
      }

      var region0 = free[best.Pose];
      foreach (var p in Grid(best.Box, best.Px, best.Py, best.Shift, best.Ox, best.Oy))
      {
        if (rows.Count >= max)
        {
          break;
        }

        if (Inside(region0, p) && ShapedFits(best.Pose, p, placed, exact))
        {
          rows.Add((best.Pose, p));
        }
      }

      return rows;
    }

    /// <summary>The copies of <paramref name="sources"/> one of each in turn, as long as each has copies left.</summary>
    private static List<int> InTurn(List<int> sources, Dictionary<int, int> pool)
    {
      var left = sources.ToDictionary(src => src, src => pool[src]);
      var sequence = new List<int>();
      while (left.Values.Any(n => n > 0))
      {
        foreach (int src in sources.Where(src => left[src] > 0))
        {
          sequence.Add(src);
          left[src]--;
        }
      }

      return sequence;
    }

    /// <summary>Which way the parts settle on a shaped sheet.</summary>
    private enum ShapedGravity
    {
      /// <summary>Keep the pack short along the long side, then low across it.</summary>
      AlongLong,

      /// <summary>The same along the short side.</summary>
      AlongShort,

      /// <summary>Out against the edge, as far from the middle of the sheet as they go: rings a round offcut.</summary>
      Outward,
    }

    /// <summary>Puts the copies on in <paramref name="sequence"/> order. A part that no longer goes on anywhere is
    /// skipped from then on; a member of <paramref name="kit"/> that does not go on ends the whole kit, whose
    /// next round could not be whole.</summary>
    private static (List<IPartPlacement> Placements, Dictionary<int, int> PlacedBySrc, List<(ShapedPose Pose, long X, long Y)> Placed) PackShapedOnce(
      Dictionary<int, Loaded> loadedById,
      List<int> sequence,
      List<int> kit,
      Dictionary<int, List<ShapedPose>> poses,
      System.Collections.Concurrent.ConcurrentDictionary<(ShapedPose Fixed, ShapedPose Moving), List<List<IntPoint>>> pairNfps,
      List<List<IntPoint>> exact,
      (long MinX, long MinY, long MaxX, long MaxY) bounds,
      ShapedGravity gravity,
      double scale,
      CancellationToken cancel)
    {
      var placements = new List<IPartPlacement>();
      var placedBySrc = new Dictionary<int, int>();
      var placed = new List<(ShapedPose Pose, long X, long Y)>();
      bool longIsX = (bounds.MaxX - bounds.MinX) >= (bounds.MaxY - bounds.MinY);
      bool alongX = gravity == ShapedGravity.AlongShort ? !longIsX : longIsX;
      long reach = alongX ? bounds.MinX : bounds.MinY; // far edge of the pack along the chosen side
      double midX = (bounds.MinX + bounds.MaxX) / 2.0, midY = (bounds.MinY + bounds.MaxY) / 2.0;

      (long, long, long) Score(ShapedPose pose, IntPoint p)
      {
        if (gravity == ShapedGravity.Outward)
        {
          double cx = p.X + pose.CentreX - midX, cy = p.Y + pose.CentreY - midY;
          return (-(long)(((cx * cx) + (cy * cy)) / 1000), alongX ? p.Y : p.X, alongX ? p.X : p.Y);
        }

        long along = alongX ? p.X : p.Y;
        long across = alongX ? p.Y : p.X;
        return (Math.Max(reach, along + pose.FarAlong(alongX)), across, along);
      }

      var state = new System.Collections.Concurrent.ConcurrentDictionary<ShapedPose, ShapedPose.Blocked>();
      var done = new HashSet<int>();
      foreach (int src in sequence)
      {
        var loaded = loadedById[src];
        var srcPoses = poses[src];
        if (done.Contains(src))
        {
          continue;
        }

        cancel.ThrowIfCancellationRequested();

        // A few of the best positions of every pose, best first. The no-fit polygons are exact in principle,
        // but Clipper can leave a sliver of zero area where two of them meet, and a corner of that sliver
        // reads as free room: on a round offcut a part went straight on top of another one. Each position is
        // therefore checked against the real shapes before it is taken.
        var candidates = new System.Collections.Concurrent.ConcurrentBag<((long, long, long) Score, ShapedPose Pose, IntPoint At)>();
        System.Threading.Tasks.Parallel.ForEach(srcPoses, pose =>
        {
          var free = pose.FreeAfter(state.GetOrAdd(pose, _ => new ShapedPose.Blocked()), placed, pairNfps);
          foreach (var top in free.SelectMany(path => path).Select(p => (Score: Score(pose, p), At: p)).OrderBy(t => t.Score).Take(6))
          {
            candidates.Add((top.Score, pose, top.At));
          }
        });

        var chosen = candidates
          .OrderBy(c => c.Score)
          .ThenBy(c => c.Pose.Angle)
          .ThenBy(c => c.At.X)
          .ThenBy(c => c.At.Y)
          .FirstOrDefault(c => ShapedFits(c.Pose, c.At, placed, exact));
        if (chosen.Pose == null)
        {
          // No pose of this part goes on any more; smaller parts still may.
          done.Add(src);
          if (kit != null && kit.Contains(src))
          {
            done.UnionWith(kit);
          }

          continue;
        }

        placements.Add(new PartPlacement(chosen.Pose.Rotated)
        {
          X = chosen.At.X / scale,
          Y = chosen.At.Y / scale,
          Rotation = chosen.Pose.Angle,
          Source = src,
          IsMirrored = loaded.Mirrored,
        });
        placed.Add((chosen.Pose, chosen.At.X, chosen.At.Y));
        if (gravity != ShapedGravity.Outward)
        {
          reach = Math.Max(reach, chosen.Score.Item1);
        }

        placedBySrc[src] = placedBySrc.TryGetValue(src, out int n) ? n + 1 : 1;
      }

      return (placements, placedBySrc, placed);
    }

    /// <summary>The check every position passes before a part is put there: its outline inside the metal and
    /// its shell clear of every shell already placed, measured on the shapes themselves.</summary>
    private static bool ShapedFits(ShapedPose pose, IntPoint at, List<(ShapedPose Pose, long X, long Y)> placed, List<List<IntPoint>> exact)
    {
      const double eps = 1e-4 * ShapeScale * ShapeScale;
      var outline = Translate(pose.Outline, at.X, at.Y);
      var c = new Clipper();
      c.AddPath(outline, PolyType.ptSubject, true);
      c.AddPaths(exact, PolyType.ptClip, true);
      var outside = new List<List<IntPoint>>();
      c.Execute(ClipType.ctDifference, outside, PolyFillType.pftNonZero, PolyFillType.pftNonZero);
      if (outside.Sum(o => Math.Abs(Clipper.Area(o))) > eps)
      {
        return false;
      }

      var shell = Translate(pose.Shell, at.X, at.Y);
      var box = BoundsOfPaths(new List<List<IntPoint>> { shell });
      foreach (var (other, x, y) in placed)
      {
        var otherShell = Translate(other.Shell, x, y);
        var ob = BoundsOfPaths(new List<List<IntPoint>> { otherShell });
        if (ob.MinX > box.MaxX || ob.MaxX < box.MinX || ob.MinY > box.MaxY || ob.MaxY < box.MinY)
        {
          continue;
        }

        var k = new Clipper();
        k.AddPath(shell, PolyType.ptSubject, true);
        k.AddPath(otherShell, PolyType.ptClip, true);
        var both = new List<List<IntPoint>>();
        k.Execute(ClipType.ctIntersection, both, PolyFillType.pftNonZero, PolyFillType.pftNonZero);
        if (both.Sum(o => Math.Abs(Clipper.Area(o))) > eps)
        {
          return false;
        }
      }

      return true;
    }

    private static List<IntPoint> Translate(List<IntPoint> path, long dx, long dy) => TranslatePath(path, dx, dy);

    /// <summary>Every path offset together (holes included) by <paramref name="delta"/> scaled units.</summary>
    private static List<List<IntPoint>> OffsetAll(List<List<IntPoint>> paths, long delta)
    {
      var co = new ClipperOffset(4, SvgNest.Config.CurveTolerance * ShapeScale);
      co.AddPaths(paths, JoinType.jtMiter, EndType.etClosedPolygon);
      var solution = new List<List<IntPoint>>();
      co.Execute(ref solution, delta);
      return solution;
    }

    /// <summary>
    /// Douglas-Peucker on a closed path: the fewest vertices that keep every dropped one within
    /// <paramref name="tolerance"/> of the new outline. An arc of hundreds of short chords comes down to the
    /// few dozen its tolerance needs (Clipper's own clean-up only drops vertices already in line).
    /// </summary>
    private static List<IntPoint> Reduce(List<IntPoint> path, long tolerance)
    {
      if (path.Count <= 4)
      {
        return path;
      }

      // Split the ring at its first vertex and the vertex farthest from it, and reduce both halves.
      int far = 0;
      double farDist = -1;
      for (int i = 1; i < path.Count; i++)
      {
        double dx = path[i].X - path[0].X, dy = path[i].Y - path[0].Y;
        double d = (dx * dx) + (dy * dy);
        if (d > farDist)
        {
          farDist = d;
          far = i;
        }
      }

      var keep = new bool[path.Count];
      keep[0] = keep[far] = true;
      var stack = new Stack<(int From, int To)>();
      stack.Push((0, far));
      stack.Push((far, path.Count));
      double tol2 = (double)tolerance * tolerance;
      while (stack.Count > 0)
      {
        var (from, to) = stack.Pop();
        var a = path[from];
        var b = path[to % path.Count];
        double ex = b.X - a.X, ey = b.Y - a.Y;
        double len2 = (ex * ex) + (ey * ey);
        int worst = -1;
        double worstDist = tol2;
        for (int i = from + 1; i < to; i++)
        {
          double px = path[i].X - a.X, py = path[i].Y - a.Y;
          double d2 = len2 <= 0 ? (px * px) + (py * py) : Math.Pow((px * ey) - (py * ex), 2) / len2;
          if (d2 > worstDist)
          {
            worstDist = d2;
            worst = i;
          }
        }

        if (worst >= 0)
        {
          keep[worst] = true;
          stack.Push((from, worst));
          stack.Push((worst, to));
        }
      }

      var reduced = path.Where((p, i) => keep[i]).ToList();
      return reduced.Count >= 3 ? reduced : path;
    }

    /// <summary>Each path reduced to <paramref name="delta"/> (see <see cref="Reduce"/>); paths that collapse go.</summary>
    private static List<List<IntPoint>> Simplified(List<List<IntPoint>> paths, long delta)
      => paths.Select(p => Reduce(p, delta)).Where(p => p.Count >= 3).ToList();

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
    /// same as <see cref="PartPlacement.X"/>/<see cref="PartPlacement.Y"/>. What one packing has blocked for
    /// it is kept apart (<see cref="Blocked"/>), so several packings share the costly geometry.
    /// </summary>
    private sealed class ShapedPose
    {
      public int Angle { get; private set; }

      public INfp Rotated { get; private set; }

      /// <summary>The turned outline, wound counter-clockwise, as it is.</summary>
      public List<IntPoint> Outline { get; private set; }

      /// <summary>The spacing shell, half the spacing round the turned outline, simplified outward.</summary>
      public List<IntPoint> Shell { get; private set; }

      /// <summary>Where the shift may put the part with its outline inside the sheet: the inner-fit region.</summary>
      public List<List<IntPoint>> Inner { get; private set; }

      public double CentreX { get; private set; }

      public double CentreY { get; private set; }

      private long MaxX { get; set; }

      private long MaxY { get; set; }

      public static ShapedPose Create(Loaded loaded, int angle, List<List<IntPoint>> region, double scale, long delta)
      {
        var rotated = Math.Abs(angle % 360) < 1e-9 ? loaded.Nfp : loaded.Nfp.Rotate(angle);
        var outline = Ccw(DeepNestClipper.ScaleUpPath(rotated.Points, scale));
        var shellNfp = OffsetOutward(rotated, (loaded.FitSpacing + ShapeSafety) / 2.0);
        var shell = Grown(Ccw(DeepNestClipper.ScaleUpPath(shellNfp.Points, scale)), delta);
        var forFit = Grown(outline, delta);

        // The outline lies inside the region exactly when one of its points does, its edges cross none of the
        // region's, and no boundary of the region (a hole of the sheet) lies wholly inside it:
        //   (region - p0) - (boundary (+) -outline) - (each boundary's first point - outline).
        var neg = Negate(forFit);
        var c = new Clipper();
        c.AddPaths(region.Select(r => Translate(r, -forFit[0].X, -forFit[0].Y)).ToList(), PolyType.ptSubject, true);
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
          Outline = outline,
          Shell = shell,
          Inner = inner,
          CentreX = outline.Average(p => (double)p.X),
          CentreY = outline.Average(p => (double)p.Y),
          MaxX = outline.Max(p => p.X),
          MaxY = outline.Max(p => p.Y),
        };
      }

      /// <summary>How far past the shift the outline reaches along the chosen side of the sheet.</summary>
      public long FarAlong(bool alongX) => alongX ? this.MaxX : this.MaxY;

      /// <summary>The inner-fit region less every position where this pose's shell would overlap a shell already
      /// placed. The blocked area is kept in <paramref name="state"/> and only grown by the parts placed since.</summary>
      public List<List<IntPoint>> FreeAfter(
        Blocked state,
        List<(ShapedPose Pose, long X, long Y)> placed,
        System.Collections.Concurrent.ConcurrentDictionary<(ShapedPose Fixed, ShapedPose Moving), List<List<IntPoint>>> pairNfps)
      {
        if (state.By < placed.Count)
        {
          var u = new Clipper();
          u.AddPaths(state.Area, PolyType.ptSubject, true);
          for (int i = state.By; i < placed.Count; i++)
          {
            var (other, x, y) = placed[i];
            var nfp = pairNfps.GetOrAdd((other, this), key => PairNfp(key.Fixed.Shell, key.Moving.Shell));
            u.AddPaths(nfp.Select(path => Translate(path, x, y)).ToList(), PolyType.ptClip, true);
          }

          var merged = new List<List<IntPoint>>();
          u.Execute(ClipType.ctUnion, merged, PolyFillType.pftNonZero, PolyFillType.pftNonZero);
          state.Area = merged;
          state.By = placed.Count;
        }

        if (state.Area.Count == 0)
        {
          return this.Inner;
        }

        var c = new Clipper();
        c.AddPaths(this.Inner, PolyType.ptSubject, true);
        c.AddPaths(state.Area, PolyType.ptClip, true);
        var free = new List<List<IntPoint>>();
        c.Execute(ClipType.ctDifference, free, PolyFillType.pftNonZero, PolyFillType.pftNonZero);
        return free;
      }

      /// <summary>Shifts of <paramref name="moving"/> at which it overlaps <paramref name="fixedShell"/> at rest:
      /// fixed (+) -moving, built as the sum of the boundaries plus one copy of each shape. Holes of no real
      /// size (Clipper leaves slivers where the sums meet) are filled: no part fits in them.</summary>
      private static List<List<IntPoint>> PairNfp(List<IntPoint> fixedShell, List<IntPoint> moving)
      {
        var neg = Negate(moving);
        var c = new Clipper();
        c.AddPaths(Clipper.MinkowskiSum(neg, fixedShell, true), PolyType.ptSubject, true);
        c.AddPath(Translate(fixedShell, neg[0].X, neg[0].Y), PolyType.ptClip, true);
        c.AddPath(Translate(neg, fixedShell[0].X, fixedShell[0].Y), PolyType.ptClip, true);
        var nfp = new List<List<IntPoint>>();
        c.Execute(ClipType.ctUnion, nfp, PolyFillType.pftNonZero, PolyFillType.pftNonZero);
        return nfp.Where(p => Clipper.Area(p) > 0 || Math.Abs(Clipper.Area(p)) > ShapeScale * ShapeScale).ToList();
      }

      /// <summary>The path grown by <paramref name="delta"/> and then simplified by as much: never smaller.</summary>
      private static List<IntPoint> Grown(List<IntPoint> path, long delta)
      {
        var grown = OffsetAll(new List<List<IntPoint>> { path }, delta).OrderByDescending(p => Math.Abs(Clipper.Area(p))).FirstOrDefault() ?? path;
        var clean = Reduce(grown, delta);
        return Ccw(clean.Count >= 3 ? clean : grown);
      }

      /// <summary>What one packing has made unusable for a pose so far, and after how many placed parts.</summary>
      public sealed class Blocked
      {
        public List<List<IntPoint>> Area { get; set; } = new List<List<IntPoint>>();

        public int By { get; set; }
      }
    }
  }
}
