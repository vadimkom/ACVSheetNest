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
    /// Whether the path is a part drawing the DXF pipeline reads: .dxf, or .chf, the laser
    /// CAM's own format, which <see cref="DrawingFile"/> reads into the same entities.
    /// </summary>
    public static bool IsDxf(string? path)
    {
      if (string.IsNullOrWhiteSpace(path))
      {
        return false;
      }

      return path.EndsWith(".dxf", StringComparison.OrdinalIgnoreCase)
          || path.EndsWith(".chf", StringComparison.OrdinalIgnoreCase);
    }
  }
}