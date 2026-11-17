namespace DeepNestSharp
{
  using System;
  using System.IO;
  using System.Text.Json;
  using System.Text.Json.Nodes;

  /// <summary>
  /// The job last worked on, kept whatever the operator answered about saving (the user, 2026-11-17: "save
  /// the last project automatically, say in _last, and add an item to load it"). Written to
  /// %LOCALAPPDATA%\SheetNest\_last.dnest when the app closes, when another project replaces it and with every
  /// autosave; File → "Открыть последний проект" opens it. Several running copies share it: the last to
  /// write wins.
  /// </summary>
  public static class LastProject
  {
    /// <summary>Where it is kept; tests point it elsewhere.</summary>
    internal static string Folder { get; set; } = Path.Combine(
      Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SheetNest");

    public static string FilePath => Path.Combine(Folder, "_last.dnest");

    private static string MetaPath => Path.Combine(Folder, "_last.meta.json");

    /// <summary>Keeps the project. A job with no parts and no sheets is not worth keeping over the one
    /// before it (an empty new job closed is not "the last project"). Returns whether it was written.</summary>
    public static bool Write(string projectJson, string originalFilePath)
    {
      try
      {
        if (IsEmpty(projectJson))
        {
          return false;
        }

        Directory.CreateDirectory(Folder);
        string temp = FilePath + ".tmp";
        File.WriteAllText(temp, projectJson);
        File.Move(temp, FilePath, true);
        File.WriteAllText(MetaPath, JsonSerializer.Serialize(new Meta
        {
          OriginalPath = originalFilePath ?? string.Empty,
          SavedAtLocal = DateTime.Now.ToString("yyyy-MM-dd HH:mm"),
        }));
        return true;
      }
      catch
      {
        // Never in the operator's way.
        return false;
      }
    }

    /// <summary>The kept project's own file (empty when it was never saved) and when it was kept; false
    /// when there is none.</summary>
    public static bool TryGet(out string originalPath, out string savedAt)
    {
      originalPath = string.Empty;
      savedAt = string.Empty;
      try
      {
        if (!File.Exists(FilePath))
        {
          return false;
        }

        savedAt = File.GetLastWriteTime(FilePath).ToString("yyyy-MM-dd HH:mm");
        if (File.Exists(MetaPath))
        {
          var meta = JsonSerializer.Deserialize<Meta>(File.ReadAllText(MetaPath));
          originalPath = meta?.OriginalPath ?? string.Empty;
          savedAt = string.IsNullOrEmpty(meta?.SavedAtLocal) ? savedAt : meta.SavedAtLocal;
        }

        return true;
      }
      catch
      {
        return File.Exists(FilePath);
      }
    }

    /// <summary>A file the app keeps for itself (this one, the autosaves): never a recent project.</summary>
    public static bool IsOwnFile(string path)
    {
      try
      {
        string folder = Path.GetFullPath(Folder).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return Path.GetFullPath(path).StartsWith(folder, StringComparison.OrdinalIgnoreCase)
          && path.EndsWith(".dnest", StringComparison.OrdinalIgnoreCase);
      }
      catch
      {
        return false;
      }
    }

    internal static bool IsEmpty(string projectJson)
    {
      try
      {
        var root = JsonNode.Parse(projectJson) as JsonObject;
        return root == null
          || (Count(root["DetailLoadInfos"]) == 0 && Count(root["SheetLoadInfos"]) == 0);
      }
      catch
      {
        return true;
      }
    }

    private static int Count(JsonNode node) => node switch
    {
      JsonArray array => array.Count,
      JsonObject obj when obj["$values"] is JsonArray values => values.Count,
      JsonObject obj when obj["Items"] is JsonArray items => items.Count,
      _ => 0,
    };

    private sealed class Meta
    {
      public string OriginalPath { get; set; }

      public string SavedAtLocal { get; set; }
    }
  }
}
