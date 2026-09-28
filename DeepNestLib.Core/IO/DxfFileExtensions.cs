namespace DeepNestLib.IO
{
  using System;
  using System.Collections.Generic;
  using IxMilia.Dxf.Entities;

  public static class DxfFileExtensions
  {
    public static void AddRange(this IList<DxfEntity> list, IEnumerable<DxfLine> lines)
    {
      foreach (var line in lines)
      {
        list.Add(line);
      }
    }

    /// <summary>
    /// Checks whether the given path or filename has a DXF extension (.dxf or alias .dfx), case-insensitively.
    /// </summary>
    public static bool IsDxf(string? path)
    {
      if (string.IsNullOrWhiteSpace(path))
      {
        return false;
      }

      return path.EndsWith(".dxf", StringComparison.OrdinalIgnoreCase)
          || path.EndsWith(".dfx", StringComparison.OrdinalIgnoreCase);
    }
  }
}