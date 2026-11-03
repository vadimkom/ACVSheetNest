namespace DeepNestLib.NestProject
{
  using System;
  using System.Collections.Generic;
  using System.IO;
  using System.Linq;
  using System.Text.Json.Nodes;

  /// <summary>
  /// A project keeps full paths to its drawings, and a job copied to a flash drive, or onto another PC, opens
  /// with every one of them pointing where the files no longer are. A drawing missing at its path is looked for
  /// by name in the folder the project was opened from and every folder below it; where several files share the
  /// name, the one whose folders match the old path the furthest wins.
  /// </summary>
  public static class ProjectPaths
  {
    /// <summary>
    /// Points the parts, the sheets drawn from a drawing and the saved nest at the files found beside
    /// <paramref name="projectFolder"/>. Paths that exist, or that no file there matches, are left alone.
    /// Returns how many paths were moved.
    /// </summary>
    public static int Relocate(ProjectInfo project, string projectFolder)
    {
      if (project == null || string.IsNullOrEmpty(projectFolder) || !Directory.Exists(projectFolder))
      {
        return 0;
      }

      ILookup<string, string> files = null;
      var moved = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

      string Find(string path)
      {
        if (string.IsNullOrWhiteSpace(path) || Exists(path))
        {
          return path;
        }

        if (moved.TryGetValue(path, out string known))
        {
          return known;
        }

        files ??= Listing(projectFolder);
        string name = SafeFileName(path);
        var candidates = name == null ? Array.Empty<string>() : files[name.ToUpperInvariant()].ToArray();
        if (candidates.Length == 0)
        {
          return path;
        }

        string best = candidates
          .OrderByDescending(c => SharedTail(c, path))
          .ThenBy(c => c.Length)
          .ThenBy(c => c, StringComparer.OrdinalIgnoreCase)
          .First();
        moved[path] = best;
        return best;
      }

      foreach (var row in project.DetailLoadInfos)
      {
        row.Path = Find(row.Path);
        if (!string.IsNullOrEmpty(row.UnflippedPath))
        {
          row.UnflippedPath = Find(row.UnflippedPath);
        }

        if (!string.IsNullOrEmpty(row.SourceDxfPath))
        {
          row.SourceDxfPath = Find(row.SourceDxfPath);
        }
      }

      foreach (var sheet in project.SheetLoadInfos)
      {
        if (!string.IsNullOrEmpty(sheet.ShapePath))
        {
          sheet.ShapePath = Find(sheet.ShapePath);
        }
      }

      // The saved nest names each part by its drawing too, and the DXF export reads the drawing back from there.
      if (moved.Count > 0 && !string.IsNullOrWhiteSpace(project.LastNestResultJson))
      {
        try
        {
          var nest = JsonNode.Parse(project.LastNestResultJson);
          if (Rewrite(nest, moved))
          {
            project.LastNestResultJson = nest.ToJsonString();
          }
        }
        catch (System.Text.Json.JsonException)
        {
          // A nest that cannot be read is left as it was; the parts themselves are already found.
        }
      }

      return moved.Count;
    }

    private static bool Exists(string path)
    {
      try
      {
        return File.Exists(path);
      }
      catch (Exception)
      {
        return false;
      }
    }

    private static string SafeFileName(string path)
    {
      try
      {
        return Path.GetFileName(path);
      }
      catch (ArgumentException)
      {
        return null;
      }
    }

    /// <summary>Every file in the folder and below, by upper-cased name; folders that cannot be read are skipped.</summary>
    private static ILookup<string, string> Listing(string folder)
    {
      var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.System };
      try
      {
        return Directory.EnumerateFiles(folder, "*", options).ToLookup(f => Path.GetFileName(f).ToUpperInvariant());
      }
      catch (Exception)
      {
        return Array.Empty<string>().ToLookup(f => f);
      }
    }

    /// <summary>How many folders, from the file up, two paths have in common.</summary>
    private static int SharedTail(string a, string b)
    {
      var pa = a.Split('\\', '/');
      var pb = b.Split('\\', '/');
      int n = 0;
      while (n < pa.Length && n < pb.Length && string.Equals(pa[pa.Length - 1 - n], pb[pb.Length - 1 - n], StringComparison.OrdinalIgnoreCase))
      {
        n++;
      }

      return n;
    }

    private static bool Rewrite(JsonNode node, IReadOnlyDictionary<string, string> moved)
    {
      bool changed = false;
      switch (node)
      {
        case JsonObject obj:
          foreach (var key in obj.Select(kv => kv.Key).ToList())
          {
            if (obj[key] is JsonValue value && value.TryGetValue(out string text) && text != null && moved.TryGetValue(text, out string found))
            {
              obj[key] = found;
              changed = true;
            }
            else if (obj[key] != null)
            {
              changed |= Rewrite(obj[key], moved);
            }
          }

          break;
        case JsonArray array:
          for (int i = 0; i < array.Count; i++)
          {
            if (array[i] is JsonValue value && value.TryGetValue(out string text) && text != null && moved.TryGetValue(text, out string found))
            {
              array[i] = found;
              changed = true;
            }
            else if (array[i] != null)
            {
              changed |= Rewrite(array[i], moved);
            }
          }

          break;
      }

      return changed;
    }
  }
}
