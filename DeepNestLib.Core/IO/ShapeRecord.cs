namespace DeepNestLib.IO
{
  using System;
  using System.Collections.Generic;
  using System.IO;
  using System.Linq;
  using System.Text.Json;

  /// <summary>
  /// What a shape's drawing was made from, kept beside it (<c>name.dxf.shape.json</c>): the kind, the values as
  /// typed (for tape and profile the length and width are the SHEET), the preset and the units. Double-clicking
  /// the part opens the shape dialog on these, so the operator changes the shape rather than its file.
  /// </summary>
  public sealed class ShapeRecord
  {
    public const string Suffix = ".shape.json";

    /// <summary>The kind's <see cref="ShapeKind.Id"/>.</summary>
    public string Shape { get; set; }

    /// <summary>The values as typed in the dialog, by parameter label key, in the units below.</summary>
    public Dictionary<string, double> Values { get; set; } = new Dictionary<string, double>();

    /// <summary>The preset's detail ("B26, Ø8.2 + 4×Ø3.5"), or null for custom sizes.</summary>
    public string Preset { get; set; }

    public bool Millimetres { get; set; } = true;

    /// <summary>The length and width typed were a sheet the shape was drawn inside (tape, profile).</summary>
    public bool ForSheet { get; set; }

    [System.Text.Json.Serialization.JsonIgnore]
    public ShapeKind Kind => PartShapes.Kinds.FirstOrDefault(k => k.Id == this.Shape);

    public static string PathFor(string drawing) => drawing + Suffix;

    public static ShapeRecord Of(ShapeKind kind, IReadOnlyList<double> typed, ShapePreset preset, bool millimetres, bool forSheet)
      => new ShapeRecord
      {
        Shape = kind.Id,
        Values = kind.Parameters.Select((p, i) => (p.LabelKey, Value: typed[i])).ToDictionary(x => x.LabelKey, x => x.Value),
        Preset = preset?.Detail,
        Millimetres = millimetres,
        ForSheet = forSheet,
      };

    /// <summary>The values in the order the kind lists its parameters, in the units asked for; a parameter the
    /// record does not have takes its default.</summary>
    public double[] ValuesIn(bool millimetres)
    {
      var kind = this.Kind;
      double scale = this.Millimetres == millimetres ? 1 : (millimetres ? 25.4 : 1 / 25.4);
      double defaultScale = millimetres ? 1 : 1 / 25.4;
      return kind.Parameters
        .Select(p => this.Values != null && this.Values.TryGetValue(p.LabelKey, out double v) ? v * scale : p.DefaultMm * defaultScale)
        .ToArray();
    }

    /// <summary>Writes the record beside the drawing. A failure costs only the double-click, so it is swallowed.</summary>
    public void SaveFor(string drawing)
    {
      try
      {
        File.WriteAllText(PathFor(drawing), JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
      }
      catch (IOException)
      {
      }
      catch (UnauthorizedAccessException)
      {
      }
    }

    /// <summary>The record beside a drawing, or null when it has none (an ordinary part) or it cannot be read.</summary>
    public static ShapeRecord LoadFor(string drawing)
    {
      try
      {
        string path = PathFor(drawing ?? string.Empty);
        if (!File.Exists(path))
        {
          return null;
        }

        var record = JsonSerializer.Deserialize<ShapeRecord>(File.ReadAllText(path));
        return record?.Kind == null ? null : record;
      }
      catch (Exception)
      {
        return null;
      }
    }
  }
}
