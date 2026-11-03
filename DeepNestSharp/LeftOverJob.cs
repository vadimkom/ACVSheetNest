namespace DeepNestSharp
{
  using System;
  using System.Collections.Generic;
  using System.Linq;
  using System.Text.Json;
  using System.Text.Json.Nodes;
  using DeepNestLib.Placement;
  using DeepNestSharp.RasterNest;

  /// <summary>
  /// What a nest left uncut when the stock ran out, and the job that cuts it later: the same project with only
  /// the parts still owed and the sheets they need.
  /// </summary>
  internal static class LeftOverJob
  {
    /// <summary>
    /// Every counted population less the copies the result placed, matched by drawing and hand, in the order
    /// given. Parts that fill the sheets are never owed. The populations are copied, not changed.
    /// </summary>
    internal static List<RasterPartInfo> Owed(IEnumerable<RasterPartInfo> populations, INestResult result)
    {
      var placed = new Dictionary<(string Path, bool Mirrored), int>();
      foreach (var pp in result?.UsedSheets?.SelectMany(s => s.PartPlacements) ?? Enumerable.Empty<IPartPlacement>())
      {
        var key = ((pp.Part?.Name ?? string.Empty).ToUpperInvariant(), pp.IsMirrored);
        placed[key] = placed.TryGetValue(key, out int n) ? n + 1 : 1;
      }

      var owed = new List<RasterPartInfo>();
      foreach (var population in populations.Where(p => !p.Fill && !string.IsNullOrEmpty(p.Path)))
      {
        var key = (population.Path.ToUpperInvariant(), population.Mirrored);
        int have = placed.TryGetValue(key, out int n) ? n : 0;
        int on = Math.Min(population.Quantity, have);
        placed[key] = have - on;
        if (population.Quantity - on > 0)
        {
          owed.Add(new RasterPartInfo
          {
            Path = population.Path,
            Quantity = population.Quantity - on,
            Rotations = population.Rotations,
            Priority = population.Priority,
            Spacing = population.Spacing,
            Cc = population.Cc,
            CommonCuttingDomain = population.CommonCuttingDomain,
            RequiresMergedLines = population.RequiresMergedLines,
            Mirrored = population.Mirrored,
          });
        }
      }

      return owed;
    }

    /// <summary>
    /// The project <paramref name="projectJson"/> as a job of what is owed: each row's quantity is what is
    /// still owed (spares included), its mirrored count likewise, rows owed nothing, left out or filling the
    /// sheets are dropped, the stock is <paramref name="sheets"/> sheets of <paramref name="width"/> ×
    /// <paramref name="height"/>, and no nest is kept.
    /// </summary>
    internal static string ProjectJson(string projectJson, IReadOnlyList<RasterPartInfo> owed, int sheets, int width, int height)
    {
      var left = owed.ToList();
      var root = JsonNode.Parse(projectJson).AsObject();
      var kept = new JsonArray();
      foreach (var node in root["DetailLoadInfos"]?.AsArray().ToList() ?? new List<JsonNode>())
      {
        var row = node.AsObject();
        string path = row["Path"]?.GetValue<string>() ?? string.Empty;
        int Take(bool mirrored)
        {
          var part = left.FirstOrDefault(p => p.Mirrored == mirrored && string.Equals(p.Path, path, StringComparison.OrdinalIgnoreCase));
          if (part == null)
          {
            return 0;
          }

          left.Remove(part);
          return part.Quantity;
        }

        if ((row["FillSheets"]?.GetValue<bool>() ?? false) || !(row["IsIncluded"]?.GetValue<bool>() ?? true))
        {
          continue;
        }

        int normal = Take(false);
        int mirror = Take(true);
        if (normal + mirror == 0)
        {
          continue;
        }

        row["Quantity"] = normal;
        row["Extra"] = 0;
        row["MirrorQuantity"] = mirror;
        row.Parent?.AsArray().Remove(row);
        kept.Add(row);
      }

      root["DetailLoadInfos"] = kept;
      root["SheetLoadInfos"] = new JsonArray(new JsonObject
      {
        ["Width"] = width,
        ["Height"] = height,
        ["Quantity"] = sheets,
        ["Unlimited"] = false,
        ["Priority"] = 5,
      });
      root.Remove("LastNestResultJson");
      return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }
  }
}
