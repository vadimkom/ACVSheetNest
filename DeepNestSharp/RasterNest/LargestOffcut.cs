namespace DeepNestSharp.RasterNest
{
  using System;
  using System.Collections.Generic;
  using DeepNestLib;
  using DeepNestLib.IO;
  using DeepNestLib.Placement;

  /// <summary>
  /// The largest clean rectangle of metal a layout leaves, on any of its sheets: what can go back on the rack.
  /// The variants list ranks nests by it after the sheet count (2026-11-23, the user: utilisation is the
  /// parts' area over the sheets' area, the same for every engine that lays the job on as many sheets, so it
  /// told the layouts apart only by rounding).
  /// <para>Judged on the parts' real outlines on a grid of 512 cells along the sheet's longer side, each
  /// part grown by the spacing; the rectangle reaches the sheet's edges (an offcut is cut off with them).
  /// Sheets drawn from a file (round offcuts and the like) are not measured.</para>
  /// </summary>
  internal static class LargestOffcut
  {
    private const int Cells = 512;

    /// <summary>The largest offcut over the result's sheets as (W, H) along the sheet's axes, or null when
    /// no rectangular sheet was used.</summary>
    public static (double W, double H)? Of(INestResult result, double spacing)
    {
      (double W, double H)? best = null;
      if (result?.UsedSheets == null)
      {
        return null;
      }

      foreach (var sheet in result.UsedSheets)
      {
        var one = Of(sheet, spacing);
        if (one is { } o && (best == null || (o.W * o.H) > (best.Value.W * best.Value.H)))
        {
          best = o;
        }
      }

      return best;
    }

    /// <summary>The largest offcut of one sheet, or null for a sheet drawn from a file.</summary>
    public static (double W, double H)? Of(ISheetPlacement sheet, double spacing)
    {
      if (sheet?.Sheet == null || SheetShape.IsShaped(sheet.Sheet))
      {
        return null;
      }

      double w = sheet.Sheet.WidthCalculated;
      double h = sheet.Sheet.HeightCalculated;
      var outlines = new List<SvgPoint[]>();
      foreach (var p in sheet.PartPlacements)
      {
        var placed = p.PlacedPart;
        if (placed?.Points != null && placed.Points.Length >= 3)
        {
          outlines.Add(placed.Points);
        }
      }

      return Of(outlines, w, h, spacing);
    }

    /// <summary>The largest empty rectangle of a <paramref name="w"/> × <paramref name="h"/> sheet around
    /// these outlines (sheet coordinates), each grown by <paramref name="spacing"/>.</summary>
    internal static (double W, double H) Of(IReadOnlyList<SvgPoint[]> outlines, double w, double h, double spacing)
    {
      if (w <= 0 || h <= 0)
      {
        return (0, 0);
      }

      double cell = Math.Max(w, h) / Cells;
      int nx = Math.Max(1, (int)Math.Ceiling((w / cell) - 1e-9));
      int ny = Math.Max(1, (int)Math.Ceiling((h / cell) - 1e-9));
      var taken = new bool[ny, nx];
      var xs = new List<double>();
      foreach (var pts in outlines)
      {
        Fill(pts, taken, nx, ny, cell, xs);
      }

      int grow = spacing > 0 ? (int)Math.Ceiling((spacing / cell) - 1e-9) : 0;
      if (grow > 0)
      {
        taken = Grow(taken, nx, ny, grow);
      }

      // Largest rectangle of free cells: a histogram of free run heights per row, then a stack.
      var heights = new int[nx];
      var stack = new int[nx + 1];
      long bestArea = 0;
      int bestW = 0, bestH = 0;
      for (int j = 0; j < ny; j++)
      {
        for (int i = 0; i < nx; i++)
        {
          heights[i] = taken[j, i] ? 0 : heights[i] + 1;
        }

        int top = 0;
        for (int i = 0; i <= nx; i++)
        {
          int hgt = i < nx ? heights[i] : 0;
          while (top > 0 && heights[stack[top - 1]] >= hgt)
          {
            int tall = heights[stack[--top]];
            int left = top > 0 ? stack[top - 1] + 1 : 0;
            long area = (long)tall * (i - left);
            if (area > bestArea)
            {
              bestArea = area;
              bestW = i - left;
              bestH = tall;
            }
          }

          stack[top++] = i;
        }
      }

      return (Math.Min(w, bestW * cell), Math.Min(h, bestH * cell));
    }

    /// <summary>Marks the cells a polygon touches: each row is cut at its bottom, middle and top (even-odd
    /// spans), and every vertex marks its own cell, so thin slivers are not lost between the samples.</summary>
    private static void Fill(SvgPoint[] pts, bool[,] taken, int nx, int ny, double cell, List<double> xs)
    {
      int n = pts.Length;
      double minY = double.MaxValue, maxY = double.MinValue;
      foreach (var q in pts)
      {
        minY = Math.Min(minY, q.Y);
        maxY = Math.Max(maxY, q.Y);
        int ci = (int)Math.Floor(q.X / cell);
        int cj = (int)Math.Floor(q.Y / cell);
        if (ci >= 0 && ci < nx && cj >= 0 && cj < ny)
        {
          taken[cj, ci] = true;
        }
      }

      int j0 = Math.Max(0, (int)Math.Floor(minY / cell));
      int j1 = Math.Min(ny - 1, (int)Math.Floor(maxY / cell));
      for (int j = j0; j <= j1; j++)
      {
        for (int s = 0; s < 3; s++)
        {
          double y = (j + (s * 0.5)) * cell;
          y = Math.Min(Math.Max(y, minY), maxY);
          xs.Clear();
          for (int k = 0; k < n; k++)
          {
            var a = pts[k];
            var b = pts[(k + 1) % n];
            if ((a.Y <= y && b.Y > y) || (b.Y <= y && a.Y > y))
            {
              xs.Add(a.X + ((y - a.Y) * (b.X - a.X) / (b.Y - a.Y)));
            }
          }

          xs.Sort();
          for (int k = 0; k + 1 < xs.Count; k += 2)
          {
            int i0 = Math.Max(0, (int)Math.Floor(xs[k] / cell));
            int i1 = Math.Min(nx - 1, (int)Math.Floor(xs[k + 1] / cell));
            for (int i = i0; i <= i1; i++)
            {
              taken[j, i] = true;
            }
          }
        }
      }
    }

    /// <summary>Grows the taken cells by <paramref name="r"/> cells every way (a square, so never less than
    /// the spacing).</summary>
    private static bool[,] Grow(bool[,] taken, int nx, int ny, int r)
    {
      var across = new bool[ny, nx];
      for (int j = 0; j < ny; j++)
      {
        int last = int.MinValue / 2;
        for (int i = 0; i < nx; i++)
        {
          if (taken[j, i])
          {
            last = i;
          }

          across[j, i] = i - last <= r;
        }

        last = int.MaxValue / 2;
        for (int i = nx - 1; i >= 0; i--)
        {
          if (taken[j, i])
          {
            last = i;
          }

          across[j, i] |= last - i <= r;
        }
      }

      var grown = new bool[ny, nx];
      for (int i = 0; i < nx; i++)
      {
        int last = int.MinValue / 2;
        for (int j = 0; j < ny; j++)
        {
          if (across[j, i])
          {
            last = j;
          }

          grown[j, i] = j - last <= r;
        }

        last = int.MaxValue / 2;
        for (int j = ny - 1; j >= 0; j--)
        {
          if (across[j, i])
          {
            last = j;
          }

          grown[j, i] |= last - j <= r;
        }
      }

      return grown;
    }
  }
}
