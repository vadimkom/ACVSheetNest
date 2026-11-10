namespace DeepNestSharp.RasterNest
{
  using System;
  using System.Collections.Generic;
  using System.Linq;
  using DeepNestLib;
  using DeepNestLib.Placement;

  /// <summary>
  /// Empty metal a packed sheet still has, judged on boxes: where a waiting part's box goes clear of the boxes
  /// of the parts on it. The engine packs a strip and a sheet keeps what lands inside it, so it can come back
  /// with a band of the sheet empty and parts that fit it left over. The shop's door panels (2026-11-10,
  /// `PortaFixture`): eight strips 1915 long and eight panels 290 × 425 on two 1250 × 2500 sheets; the
  /// first sheet came back with four strips and one panel, a band of 1250 × 570 empty over them, and three
  /// parts went uncut where the operator lays four strips and four panels on each sheet by hand.
  /// </summary>
  internal static partial class SparrowNestService
  {
    /// <summary>Parts set into a sheet's empty metal without a search (diagnostics).</summary>
    internal static int DiagFreeRoomPlaced;

    /// <summary>Cells along the longer side of the sheet the room is looked for in.</summary>
    private const int FreeRoomCells = 512;

    /// <summary>
    /// The sheet's free room as a grid of cells: a cell is taken when any part's box, grown by the spacing,
    /// touches it, so a box set on free cells is clear of every part on the sheet, whatever their shapes.
    /// </summary>
    private sealed class FreeRoom
    {
      private readonly int nx;
      private readonly int ny;
      private readonly double cell;
      private readonly double margin;
      private readonly int[,] taken; // prefix sums of taken cells, (ny + 1) × (nx + 1)

      public FreeRoom(IEnumerable<IPartPlacement> placed, double spacing, int w, int h, double margin)
      {
        this.margin = Math.Max(0, margin);
        double uw = w - (2 * this.margin);
        double uh = h - (2 * this.margin);
        this.cell = Math.Max(Math.Max(uw, uh) / FreeRoomCells, 1e-3);
        this.nx = Math.Max(0, (int)Math.Floor(uw / this.cell));
        this.ny = Math.Max(0, (int)Math.Floor(uh / this.cell));
        var grid = new bool[this.ny, this.nx];
        foreach (var p in placed)
        {
          int i0 = Math.Max(0, (int)Math.Floor((p.MinX - spacing - this.margin) / this.cell));
          int i1 = Math.Min(this.nx, (int)Math.Ceiling((p.MaxX + spacing - this.margin) / this.cell));
          int j0 = Math.Max(0, (int)Math.Floor((p.MinY - spacing - this.margin) / this.cell));
          int j1 = Math.Min(this.ny, (int)Math.Ceiling((p.MaxY + spacing - this.margin) / this.cell));
          for (int j = j0; j < j1; j++)
          {
            for (int i = i0; i < i1; i++)
            {
              grid[j, i] = true;
            }
          }
        }

        this.taken = new int[this.ny + 1, this.nx + 1];
        for (int j = 0; j < this.ny; j++)
        {
          for (int i = 0; i < this.nx; i++)
          {
            this.taken[j + 1, i + 1] = (grid[j, i] ? 1 : 0) + this.taken[j, i + 1] + this.taken[j + 1, i] - this.taken[j, i];
          }
        }
      }

      /// <summary>The lowest, then leftmost, corner a box of <paramref name="bw"/> × <paramref name="bh"/> goes
      /// on clear of everything, or null. <paramref name="leftFirst"/> looks column by column instead: the
      /// leftmost, then lowest.</summary>
      public (double X, double Y)? Spot(double bw, double bh, bool leftFirst = false)
      {
        int k = (int)Math.Ceiling(bw / this.cell);
        int l = (int)Math.Ceiling(bh / this.cell);
        int outer = leftFirst ? this.nx - k : this.ny - l;
        int inner = leftFirst ? this.ny - l : this.nx - k;
        for (int a = 0; a <= outer; a++)
        {
          for (int b = 0; b <= inner; b++)
          {
            int i = leftFirst ? a : b;
            int j = leftFirst ? b : a;
            if (this.taken[j + l, i + k] - this.taken[j, i + k] - this.taken[j + l, i] + this.taken[j, i] == 0)
            {
              return (this.margin + (i * this.cell), this.margin + (j * this.cell));
            }
          }
        }

        return null;
      }

      /// <summary>The area of the largest empty rectangle of whole cells: the biggest clean offcut.</summary>
      public double LargestEmpty()
      {
        var heights = new int[this.nx];
        var stack = new int[this.nx + 1];
        long best = 0;
        for (int j = 0; j < this.ny; j++)
        {
          for (int i = 0; i < this.nx; i++)
          {
            bool isTaken = this.taken[j + 1, i + 1] - this.taken[j, i + 1] - this.taken[j + 1, i] + this.taken[j, i] != 0;
            heights[i] = isTaken ? 0 : heights[i] + 1;
          }

          int top = 0;
          for (int i = 0; i <= this.nx; i++)
          {
            int hgt = i < this.nx ? heights[i] : 0;
            while (top > 0 && heights[stack[top - 1]] >= hgt)
            {
              int tall = heights[stack[--top]];
              int left = top > 0 ? stack[top - 1] + 1 : 0;
              best = Math.Max(best, (long)tall * (i - left));
            }

            stack[top++] = i;
          }
        }

        return best * this.cell * this.cell;
      }
    }

    /// <summary>The widest spacing any of these parts keeps, which every box is grown by.</summary>
    private static double RoomSpacing(IEnumerable<int> sources, Dictionary<int, Loaded> loadedById)
      => sources.Select(s => loadedById.TryGetValue(s, out var l) ? l.FitSpacing : 0).DefaultIfEmpty(0).Max();

    /// <summary>
    /// Where <paramref name="part"/> goes in <paramref name="room"/>: the angle, the turned outline and its
    /// placement offset, or null when no box of it fits. Bit 0 of <paramref name="rule"/> sets the parts down
    /// from the left instead of from the bottom; bit 1 breaks a tie between two angles at one corner by the
    /// nearer far side across instead of along. In a band one rule stands the parts in a row and another lays
    /// them; which fits more depends on the band, so <see cref="PutIntoFreeRoom"/> tries all four.
    /// </summary>
    private static (int Angle, INfp Turned, double X, double Y)? SpotFor(Loaded part, FreeRoom room, int rule = 0)
      => SpotFor(part.Nfp, LatticeAngles(part), room, rule);

    private static (int Angle, INfp Turned, double X, double Y)? SpotFor(INfp outline, List<int> angles, FreeRoom room, int rule)
    {
      bool leftFirst = (rule & 1) != 0;
      bool acrossTie = (rule & 2) != 0;
      (int Angle, INfp Turned, double X, double Y)? best = null;
      (double, double, double) bestKey = default;
      foreach (int angle in angles)
      {
        var turned = angle == 0 ? outline : outline.Rotate(angle);
        double bw = turned.MaxX - turned.MinX;
        double bh = turned.MaxY - turned.MinY;
        var spot = room.Spot(bw, bh, leftFirst);
        if (spot == null)
        {
          continue;
        }

        double along = leftFirst ? spot.Value.X : spot.Value.Y;
        double across = leftFirst ? spot.Value.Y : spot.Value.X;
        double tie = leftFirst ^ acrossTie ? spot.Value.X + bw : spot.Value.Y + bh;
        var key = (along, across, tie);
        if (best == null || key.CompareTo(bestKey) < 0)
        {
          best = (angle, turned, spot.Value.X - turned.MinX, spot.Value.Y - turned.MinY);
          bestKey = key;
        }
      }

      return best;
    }

    /// <summary>
    /// Sets the counted parts still waiting into the empty metal of a packed sheet, best priority and biggest
    /// first, each where its box goes clear of everything on the sheet: no search, so it costs nothing next
    /// to a pack. A sheet with common cutting keeps what its references were set on. Returns how many went on.
    /// </summary>
    private static int PutIntoFreeRoom(
      Dictionary<int, int> pool,
      Dictionary<int, Loaded> loadedById,
      List<IPartPlacement> placements,
      Dictionary<int, int> placedBySrc,
      int w,
      int h,
      double margin)
    {
      if (placements == null || placedBySrc == null || placedBySrc.Keys.Any(k => loadedById[k].RequiresMergedLines))
      {
        return 0;
      }

      int Waiting(int src) => pool[src] - (placedBySrc.TryGetValue(src, out int n) ? n : 0);
      var waiting = pool.Keys
        .Where(k => Waiting(k) > 0 && !loadedById[k].Fill && !loadedById[k].RequiresMergedLines && FitsOnSheet(loadedById[k], w, h, margin))
        .OrderBy(k => loadedById[k].Priority)
        .ThenByDescending(k => Math.Abs(loadedById[k].Nfp.Area))
        .ThenBy(k => k)
        .ToList();
      if (waiting.Count == 0)
      {
        return 0;
      }

      double spacing = RoomSpacing(placedBySrc.Keys.Concat(waiting), loadedById);
      double usable = Math.Max(0, w - (2 * margin)) * Math.Max(0, h - (2 * margin));
      double Area(int src) => Math.Abs(loadedById[src].Nfp.Area);
      var copies = waiting.SelectMany(src => Enumerable.Repeat(src, Waiting(src))).ToList();
      var rules = Enumerable.Range(0, 4).ToList();

      // As the sheet lies: the waiting parts into the room left. Ties go to the first rule.
      var plain = rules.Select(rule => Lay(placements, copies, rule)).Aggregate((a, b) => b.Count > a.Count ? b : a);

      // The small parts on the sheet taken off and laid again with the waiting ones, best priority and biggest
      // first, kept when more go on than there were; one that does not go back waits for the next sheet with
      // the others of its priority. The engine shortens its strip, so it lays small parts on
      // their side across the empty band where standing in a row more of them fit: the door panels went two
      // lying and one standing where four stand.
      var small = placements.Where(p => Area(p.Source) < BigPartShare * usable && !loadedById[p.Source].Fill).ToList();
      List<IPartPlacement> relaid = null;
      if (small.Count > 0 && small.Count <= MaxRelaid)
      {
        var kept = placements.Except(small).ToList();
        var again = small.Select(p => p.Source)
          .Concat(copies)
          .OrderBy(src => loadedById[src].Priority)
          .ThenByDescending(Area)
          .ThenBy(src => src)
          .ToList();
        foreach (int rule in rules)
        {
          var laid = Lay(kept, again, rule);
          int gain = laid.Count - small.Count;
          if (gain > plain.Count && (relaid == null || gain > relaid.Count - small.Count))
          {
            relaid = laid;
          }
        }
      }

      if (relaid != null)
      {
        placements.RemoveAll(p => small.Contains(p));
        foreach (var p in small)
        {
          if (--placedBySrc[p.Source] == 0)
          {
            placedBySrc.Remove(p.Source);
          }
        }
      }

      var added = relaid ?? plain;
      foreach (var p in added)
      {
        placements.Add(p);
        placedBySrc[p.Source] = (placedBySrc.TryGetValue(p.Source, out int n) ? n : 0) + 1;
      }

      return added.Count - (relaid != null ? small.Count : 0);

      // Lays the copies in order, each where the rule puts its box; a copy with no room is skipped.
      List<IPartPlacement> Lay(List<IPartPlacement> start, List<int> sequence, int rule)
      {
        var layout = new List<IPartPlacement>(start);
        var laid = new List<IPartPlacement>();
        var full = new HashSet<int>();
        for (int c = 0; c < sequence.Count; c++)
        {
          int src = sequence[c];
          var spot = full.Contains(src) ? null : SpotFor(loadedById[src], new FreeRoom(layout, spacing, w, h, margin), rule);
          if (spot == null)
          {
            full.Add(src);
            continue;
          }

          var p = new PartPlacement(spot.Value.Turned)
          {
            X = spot.Value.X,
            Y = spot.Value.Y,
            Rotation = spot.Value.Angle,
            Source = src,
            IsMirrored = loadedById[src].Mirrored,
          };
          layout.Add(p);
          laid.Add(p);
        }

        return laid;
      }
    }

    /// <summary>Most small parts a sheet has laid again by boxes; above that the search's own layout stands.</summary>
    private const int MaxRelaid = 50;
  }
}
