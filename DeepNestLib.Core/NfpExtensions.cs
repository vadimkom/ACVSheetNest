namespace DeepNestLib
{
  using System.Collections.Generic;
  using System.Linq;

  public static class NfpExtensions
  {
    public static bool ContainsDxfs(this ICollection<INfp> list) => list.Any(o => o?.Name != null
      && DeepNestLib.IO.DxfFileExtensions.IsDxf(o.Name));

    public static bool ContainsSvgs(this ICollection<INfp> list) => list.Any(o => o.Name.ToLower().Contains(".svg"));
  }
}
