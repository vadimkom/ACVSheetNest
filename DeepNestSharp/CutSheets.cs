namespace DeepNestSharp
{
  using System;
  using System.Collections.Generic;
  using System.IO;
  using System.Linq;
  using DeepNestLib.NestProject;
  using DeepNestLib.Placement;

  /// <summary>
  /// "These sheets are cut, nest the rest": what was cut comes off the order and stays off the stock, so
  /// the next nest is of what is left, on what is left.
  /// </summary>
  internal static class CutSheets
  {
    /// <summary>
    /// Takes every part on the cut sheets off its row. A mirrored copy comes off the mirrored count, any
    /// other off the required count and then the spares. Returns how many parts came off; a part no row
    /// asks for any more (the row was edited since the nest) is left alone.
    /// </summary>
    internal static int DeductParts(IEnumerable<IDetailLoadInfo> rows, IEnumerable<ISheetPlacement> cutSheets)
    {
      var rowList = rows.Where(r => r != null && !string.IsNullOrWhiteSpace(r.Path)).ToList();
      int taken = 0;
      foreach (var placement in cutSheets.SelectMany(s => s.PartPlacements))
      {
        string name = placement.Part?.Name ?? string.Empty;
        var row = rowList.FirstOrDefault(r => Asks(r) && string.Equals(r.Path, name, StringComparison.OrdinalIgnoreCase))
          ?? rowList.FirstOrDefault(r => Asks(r) && string.Equals(FileName(r.Path), name, StringComparison.OrdinalIgnoreCase));
        if (row == null)
        {
          continue;
        }

        if (placement.IsMirrored && row.MirrorQuantity > 0)
        {
          row.MirrorQuantity--;
        }
        else if (row.Quantity > 0)
        {
          row.Quantity--;
        }
        else if (row.Extra > 0)
        {
          row.Extra--;
        }
        else
        {
          row.MirrorQuantity--;
        }

        taken++;
      }

      return taken;
    }

    /// <summary>
    /// The cut sheets are gone from the stock: each takes one off the quantity of a row of its size (the
    /// first that still has one; an unlimited size has no count to lower). A nest itself no longer changes the
    /// quantities, so this is the only place sheets leave the stock.
    /// </summary>
    internal static void TakeCutSheets(IEnumerable<ISheetLoadInfo> rows, IEnumerable<ISheetPlacement> cutSheets)
    {
      var list = rows?.ToList() ?? new List<ISheetLoadInfo>();
      foreach (var sheet in cutSheets ?? Enumerable.Empty<ISheetPlacement>())
      {
        int w = (int)Math.Round(sheet.Sheet.WidthCalculated), h = (int)Math.Round(sheet.Sheet.HeightCalculated);
        var row = list.FirstOrDefault(r => r.Width == w && r.Height == h && !r.Unlimited && r.Quantity > 0);
        if (row != null)
        {
          row.Quantity--;
        }
      }
    }

    /// <summary>Whether the row still wants a copy. A part listed twice (say once as common line, once
    /// spaced) is taken off the first row until that one is done, then off the next.</summary>
    private static bool Asks(IDetailLoadInfo row) => row.Quantity + row.Extra + row.MirrorQuantity > 0;

    private static string FileName(string path)
    {
      try
      {
        return Path.GetFileName(path);
      }
      catch (ArgumentException)
      {
        return path;
      }
    }
  }
}
