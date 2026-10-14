namespace DeepNestLib.IO
{
  using System;
  using System.Collections.Generic;
  using System.IO;
  using System.Linq;
  using System.Text.Json;

  /// <summary>
  /// A shape kept in the library to be drawn to the metal it is cut from: tape of a given section, whose
  /// length and width are those of the offcut. Its numbers are in millimetres, keyed by the parameter's
  /// label key, so a pattern still reads after parameters are added to its shape.
  /// </summary>
  public sealed class ShapePattern
  {
    public const string Extension = ".pattern.json";

    /// <summary>The shape's <see cref="ShapeKind.Id"/>.</summary>
    public string Shape { get; set; }

    public Dictionary<string, double> ValuesMm { get; set; } = new Dictionary<string, double>();

    public ShapeKind Kind => PartShapes.Kinds.FirstOrDefault(k => k.Id == this.Shape);

    /// <summary>A pattern of a shape's current values (in the drawing's units), all but the ones it fits.</summary>
    public static ShapePattern Of(ShapeKind kind, IReadOnlyList<double> values, bool millimetres)
    {
      var pattern = new ShapePattern { Shape = kind.Id };
      for (int i = 0; i < kind.Parameters.Count; i++)
      {
        if (kind.Fit is (int length, int width) && (i == length || i == width))
        {
          continue;
        }

        pattern.ValuesMm[kind.Parameters[i].LabelKey] = millimetres ? values[i] : values[i] * 25.4;
      }

      return pattern;
    }

    /// <summary>The shape's values, in the drawing's units, for a piece <paramref name="length"/> by
    /// <paramref name="width"/> (drawing units); null when the pattern's shape is unknown or does not fit to
    /// a piece.</summary>
    public double[] Realize(double length, double width, bool millimetres)
    {
      var kind = this.Kind;
      if (kind?.Fit is not (int lengthIndex, int widthIndex))
      {
        return null;
      }

      double scale = millimetres ? 1 : 1 / 25.4;
      var values = kind.Parameters
        .Select(p => (this.ValuesMm.TryGetValue(p.LabelKey, out double v) ? v : p.DefaultMm) * scale)
        .ToArray();
      values[lengthIndex] = length;
      values[widthIndex] = width;
      return values;
    }

    public static ShapePattern Load(string path)
      => JsonSerializer.Deserialize<ShapePattern>(File.ReadAllText(path));

    public void Save(string path)
      => File.WriteAllText(path, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
  }

  /// <summary>One part the library offers: a drawing, or a pattern drawn to the piece it is cut from.</summary>
  public sealed record LibraryItem(string Name, string Path, ShapePattern Pattern);

  /// <summary>A folder of the library: parts used together, offered together.</summary>
  public sealed record LibraryGroup(string Name, IReadOnlyList<LibraryItem> Items);

  /// <summary>
  /// The shop's own parts, kept as files in a folder the operator picks (shared by every PC that points at
  /// it): each subfolder is a group, each drawing (DXF, CHF) a part of fixed size and each
  /// <see cref="ShapePattern.Extension"/> file a pattern drawn to the offcut. Nothing is indexed: the folder
  /// is read as it is, so a file dropped in is there next time.
  /// </summary>
  public static class PartLibrary
  {
    public static string DefaultFolder => Path.Combine(
      Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "SheetNest", "Library");

    /// <summary>The groups, by name; the drawings lying loose in the folder itself make a group named after
    /// <paramref name="looseGroupName"/>. Unreadable patterns are left out.</summary>
    public static IReadOnlyList<LibraryGroup> Groups(string folder, string looseGroupName)
    {
      var groups = new List<LibraryGroup>();
      if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
      {
        return groups;
      }

      var loose = Items(folder);
      if (loose.Count > 0)
      {
        groups.Add(new LibraryGroup(looseGroupName, loose));
      }

      foreach (string dir in Directory.EnumerateDirectories(folder).OrderBy(d => d, StringComparer.CurrentCultureIgnoreCase))
      {
        var items = Items(dir);
        if (items.Count > 0)
        {
          groups.Add(new LibraryGroup(Path.GetFileName(dir), items));
        }
      }

      return groups;
    }

    /// <summary>
    /// Makes the library folder the first time it is used, with the shop's perforated tapes in it as
    /// patterns, so filling an offcut works before anyone has added a part. A folder that is already there
    /// is left as it is, empty or not.
    /// </summary>
    public static void CreateIfMissing(string folder, Func<string, string> text)
    {
      if (string.IsNullOrWhiteSpace(folder) || Directory.Exists(folder))
      {
        return;
      }

      var tape = PartShapes.PerforatedTape;
      string group = Path.Combine(folder, text("Library_TapeGroup"));
      Directory.CreateDirectory(group);
      foreach (var preset in tape.Presets)
      {
        var values = tape.Parameters.Select(p => p.DefaultMm).ToArray();
        foreach (var kv in preset.ValuesMm)
        {
          values[kv.Key] = kv.Value;
        }

        SavePattern(group, tape.PatternName(values, text), ShapePattern.Of(tape, values, millimetres: true));
      }
    }

    /// <summary>Saves a pattern into a group's folder (made if new) and returns its file.</summary>
    public static string SavePattern(string groupFolder, string name, ShapePattern pattern)
    {
      Directory.CreateDirectory(groupFolder);
      string path = Path.Combine(groupFolder, SafeName(name) + ShapePattern.Extension);
      pattern.Save(path);
      return path;
    }

    /// <summary>A name that can be a file's.</summary>
    public static string SafeName(string name)
    {
      var invalid = Path.GetInvalidFileNameChars();
      string cleaned = new string((name ?? string.Empty).Trim().Select(c => Array.IndexOf(invalid, c) >= 0 ? '_' : c).ToArray()).Trim('.', ' ');
      return cleaned.Length == 0 ? "part" : cleaned;
    }

    private static IReadOnlyList<LibraryItem> Items(string dir)
    {
      var items = new List<LibraryItem>();
      foreach (string file in Directory.EnumerateFiles(dir).OrderBy(f => f, StringComparer.CurrentCultureIgnoreCase))
      {
        string name = Path.GetFileName(file);
        if (name.EndsWith(ShapePattern.Extension, StringComparison.OrdinalIgnoreCase))
        {
          try
          {
            var pattern = ShapePattern.Load(file);
            if (pattern?.Kind?.Fit != null)
            {
              items.Add(new LibraryItem(name.Substring(0, name.Length - ShapePattern.Extension.Length), file, pattern));
            }
          }
          catch (Exception)
          {
            // A pattern written by hand that does not read is not offered; the rest of the group still is.
          }
        }
        else if (DxfFileExtensions.IsDxf(file))
        {
          items.Add(new LibraryItem(Path.GetFileNameWithoutExtension(file), file, null));
        }
      }

      return items;
    }
  }
}
