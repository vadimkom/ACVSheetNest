namespace DeepNestLib.IO
{
  using System;
  using System.Collections.Generic;
  using System.Globalization;
  using System.IO;
  using System.Linq;
  using IxMilia.Dxf;
  using IxMilia.Dxf.Entities;

  /// <summary>One number a shape is drawn from: the text key of its label and its value in millimetres when
  /// the dialog opens.</summary>
  public sealed record ShapeParameter(string LabelKey, double DefaultMm);

  /// <summary>
  /// Ready-made sizes of a shape, such as a DIN 125 washer M8: the parameters it sets (by index, in
  /// millimetres), which the dialog fills in and locks. The others stay for the operator to type.
  /// </summary>
  public sealed record ShapePreset(string LabelKey, string Detail, IReadOnlyDictionary<int, double> ValuesMm);

  /// <summary>
  /// A part the program draws itself. A new kind is one class here and its texts: the dialog builds its
  /// fields from <see cref="Parameters"/> and <see cref="Presets"/>, and <see cref="PartShapes.Write"/>
  /// saves what <see cref="Draw"/> returns. Values are in the drawing's units.
  /// </summary>
  public abstract class ShapeKind
  {
    /// <summary>Text key of the shape's name.</summary>
    public abstract string NameKey { get; }

    public abstract IReadOnlyList<ShapeParameter> Parameters { get; }

    /// <summary>Ready-made sizes; the dialog offers "Custom" after them.</summary>
    public virtual IReadOnlyList<ShapePreset> Presets => Array.Empty<ShapePreset>();

    /// <summary>The text key of what is wrong with these values, or null when they draw a part.</summary>
    public virtual string Problem(IReadOnlyList<double> values) => values.Any(v => !(v > 0)) ? "Shape_NeedsSize" : null;

    /// <summary>A line telling what will be made, such as how many strips; null for none.</summary>
    public virtual string Describe(IReadOnlyList<double> values, Func<string, string> text, string unit) => null;

    /// <summary>The part's name, which is also its file's name.</summary>
    public abstract string PartName(IReadOnlyList<double> values, ShapePreset preset, Func<string, string> text);

    /// <summary>The part's entities with their lower left on the origin: closed outlines and holes on the cut
    /// layer, cuts inside it that enclose nothing on the inner-cut layer.</summary>
    public abstract IEnumerable<DxfEntity> Draw(IReadOnlyList<double> values);

    protected static string N(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);

    protected static DxfEntity Cut(DxfEntity entity)
    {
      entity.Layer = DxfOutputLayers.CutName;
      return entity;
    }

    protected static DxfEntity InnerCut(DxfEntity entity)
    {
      entity.Layer = DxfOutputLayers.InnerCutSourceName;
      return entity;
    }

    protected static DxfLwPolyline Rectangle(double width, double height)
      => new DxfLwPolyline(new[]
      {
        new DxfLwPolylineVertex { X = 0, Y = 0 },
        new DxfLwPolylineVertex { X = width, Y = 0 },
        new DxfLwPolylineVertex { X = width, Y = height },
        new DxfLwPolylineVertex { X = 0, Y = height },
      })
      { IsClosed = true };
  }

  /// <summary>
  /// The parts the operator can add without drawing them: fifty 100 x 100 squares, washers, a block of
  /// perforated tape, as a row like any other part rather than a trip to the CAD program. Each is a DXF of
  /// its own; the file is the part and its name is the part's name.
  /// </summary>
  public static class PartShapes
  {
    public static readonly ShapeKind Rectangle = new RectangleShape();
    public static readonly ShapeKind Circle = new CircleShape();
    public static readonly ShapeKind Washer = new WasherShape();
    public static readonly ShapeKind PerforatedTape = new PerforatedTapeShape();

    /// <summary>Every shape, in the order the dialog offers them.</summary>
    public static readonly IReadOnlyList<ShapeKind> Kinds = new[] { Rectangle, Circle, Washer, PerforatedTape };

    /// <summary>Where the shapes live. Not temp: a saved project points at them and nothing else could
    /// put them back.</summary>
    public static string DefaultFolder => Path.Combine(
      Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SheetNest", "shapes");

    /// <summary>Writes the shape and returns the file. Throws when the values draw no part.</summary>
    public static string Write(string folder, string name, ShapeKind kind, IReadOnlyList<double> values, bool millimetres)
    {
      if (kind.Problem(values) is string problem)
      {
        throw new ArgumentOutOfRangeException(nameof(values), problem);
      }

      var file = new DxfFile();
      file.Header.Version = DxfAcadVersion.R2000;
      file.Header.DefaultDrawingUnits = millimetres ? DxfUnits.Millimeters : DxfUnits.Inches;
      file.Header.DrawingUnits = millimetres ? DxfDrawingUnits.Metric : DxfDrawingUnits.English;
      foreach (var entity in kind.Draw(values))
      {
        file.Entities.Add(entity);
      }

      Directory.CreateDirectory(folder);
      string path = Path.Combine(folder, Sanitise(name) + ".dxf");
      file.Save(path);
      return path;
    }

    private static string Sanitise(string name)
    {
      var invalid = Path.GetInvalidFileNameChars();
      var chars = name.Trim().ToCharArray();
      for (int i = 0; i < chars.Length; i++)
      {
        if (Array.IndexOf(invalid, chars[i]) >= 0)
        {
          chars[i] = '_';
        }
      }

      string cleaned = new string(chars).Trim('.', ' ');
      return cleaned.Length == 0 ? "shape" : cleaned;
    }
  }

  /// <summary>Width × height; a square when they are equal.</summary>
  public sealed class RectangleShape : ShapeKind
  {
    public override string NameKey => "Shape_Rectangle";

    public override IReadOnlyList<ShapeParameter> Parameters { get; } = new[]
    {
      new ShapeParameter("Shape_Width", 100),
      new ShapeParameter("Shape_Height", 100),
    };

    public override string PartName(IReadOnlyList<double> v, ShapePreset preset, Func<string, string> text)
      => text(Math.Abs(v[0] - v[1]) < 1e-9 ? "Shape_Square" : "Shape_Rectangle") + " " + N(v[0]) + "x" + N(v[1]);

    public override IEnumerable<DxfEntity> Draw(IReadOnlyList<double> v)
    {
      yield return Cut(Rectangle(v[0], v[1]));
    }
  }

  /// <summary>A disc of a diameter.</summary>
  public sealed class CircleShape : ShapeKind
  {
    public override string NameKey => "Shape_Circle";

    public override IReadOnlyList<ShapeParameter> Parameters { get; } = new[] { new ShapeParameter("Shape_Diameter", 100) };

    public override string PartName(IReadOnlyList<double> v, ShapePreset preset, Func<string, string> text)
      => text("Shape_Circle") + " D" + N(v[0]);

    public override IEnumerable<DxfEntity> Draw(IReadOnlyList<double> v)
    {
      yield return Cut(new DxfCircle(new DxfPoint(v[0] / 2, v[0] / 2, 0), v[0] / 2));
    }
  }

  /// <summary>A flat washer: a disc with a hole in the middle. The thickness is the sheet's.</summary>
  public sealed class WasherShape : ShapeKind
  {
    public override string NameKey => "Shape_Washer";

    public override IReadOnlyList<ShapeParameter> Parameters { get; } = new[]
    {
      new ShapeParameter("Shape_OuterDiameter", 30),
      new ShapeParameter("Shape_HoleDiameter", 10),
    };

    /// <summary>M8 to M12: DIN 125-A / ISO 7089 for the standard washer (hole, outside 16/20/24), DIN 9021 /
    /// ISO 7093 for the wide one (outside 24/30/37).</summary>
    public override IReadOnlyList<ShapePreset> Presets { get; } = new[]
    {
      Size("Shape_WasherStandard", "M8", 16, 8.4),
      Size("Shape_WasherStandard", "M10", 20, 10.5),
      Size("Shape_WasherStandard", "M12", 24, 13),
      Size("Shape_WasherWide", "M8", 24, 8.4),
      Size("Shape_WasherWide", "M10", 30, 10.5),
      Size("Shape_WasherWide", "M12", 37, 13),
    };

    public override string Problem(IReadOnlyList<double> v)
      => base.Problem(v) ?? (v[1] < v[0] ? null : "Shape_WasherHoleTooBig");

    public override string PartName(IReadOnlyList<double> v, ShapePreset preset, Func<string, string> text)
    {
      string kind = preset?.LabelKey == "Shape_WasherWide" ? text("Shape_WasherWideName") : text("Shape_Washer");
      string thread = preset == null ? string.Empty : " " + preset.Detail;
      return kind + thread + " D" + N(v[0]) + "-" + N(v[1]);
    }

    public override IEnumerable<DxfEntity> Draw(IReadOnlyList<double> v)
    {
      var centre = new DxfPoint(v[0] / 2, v[0] / 2, 0);
      yield return Cut(new DxfCircle(centre, v[0] / 2));
      yield return Cut(new DxfCircle(centre, v[1] / 2));
    }

    private static ShapePreset Size(string kind, string thread, double outer, double hole)
      => new ShapePreset(kind, thread, new Dictionary<int, double> { [0] = outer, [1] = hole });
  }

  /// <summary>
  /// A blank of perforated mounting tape: length × width cut into strips laid side by side, so neighbouring
  /// strips share one cut along their length. Each strip has a big hole every pitch with four small holes
  /// around it (two either side along the strip, two rows across). The shared cuts keep small bridges so the
  /// strips leave the sheet as one piece and are broken apart afterwards.
  /// </summary>
  public sealed class PerforatedTapeShape : ShapeKind
  {
    public const int Length = 0;
    public const int Width = 1;
    public const int Strip = 2;
    public const int BigHole = 3;
    public const int SmallHole = 4;
    public const int Pitch = 5;
    public const int RowGap = 6;
    public const int SmallOffset = 7;
    public const int Bridge = 8;
    public const int BridgeEvery = 9;

    public override string NameKey => "Shape_Tape";

    public override IReadOnlyList<ShapeParameter> Parameters { get; } = new[]
    {
      new ShapeParameter("Shape_Length", 1000),
      new ShapeParameter("Shape_BlankWidth", 250),
      new ShapeParameter("Shape_StripWidth", 25),
      new ShapeParameter("Shape_BigHole", 8),
      new ShapeParameter("Shape_SmallHole", 3.2),
      new ShapeParameter("Shape_Pitch", 20),
      new ShapeParameter("Shape_RowGap", 10),
      new ShapeParameter("Shape_SmallOffset", 5),
      new ShapeParameter("Shape_Bridge", 0.5),
      new ShapeParameter("Shape_BridgeEvery", 400),
    };

    public override IReadOnlyList<ShapePreset> Presets { get; } = new[]
    {
      // B25: the shop's tape. The small holes sit on a 10 mm square round the big one and the groups repeat every
      // 20 mm, so along each row there is a small hole every 10 mm for a screw wherever it lands; 1.5 mm of metal
      // between big and small, 5.9 mm to the edge. B17 is the drawing the shop sent: 10 x 8 round it, every 15.
      Tape(25, 8, pitch: 20, rowGap: 10),
      Tape(17, 6.2, pitch: 15, rowGap: 8),
    };

    public override string Problem(IReadOnlyList<double> v)
    {
      for (int i = 0; i < v.Count; i++)
      {
        if (!(v[i] > 0) && i != Bridge)
        {
          return "Shape_NeedsSize";
        }
      }

      if (v[Bridge] < 0 || v[Bridge] >= v[BridgeEvery])
      {
        return "Shape_NeedsSize";
      }

      if (Strips(v) < 1)
      {
        return "Shape_TapeBlankTooNarrow";
      }

      // Every hole inside its strip, and none touching another.
      double big = v[BigHole] / 2, small = v[SmallHole] / 2, half = v[Strip] / 2;
      if (big >= half || (v[RowGap] / 2) + small >= half)
      {
        return "Shape_TapeHolesOutside";
      }

      if (Math.Sqrt((v[SmallOffset] * v[SmallOffset]) + (v[RowGap] * v[RowGap] / 4)) <= big + small
        || v[RowGap] <= 2 * small
        || 2 * v[SmallOffset] <= 2 * small
        || v[Pitch] - (2 * v[SmallOffset]) <= 2 * small
        || v[Pitch] <= 2 * big)
      {
        return "Shape_TapeHolesTouch";
      }

      return Groups(v) < 1 ? "Shape_TapeTooShort" : null;
    }

    public override string Describe(IReadOnlyList<double> v, Func<string, string> text, string unit)
    {
      if (Problem(v) != null)
      {
        return null;
      }

      int strips = Strips(v);
      return string.Format(CultureInfo.CurrentCulture, text("Shape_TapeSummary"), strips, N(v[Length]), N(strips * v[Strip]), unit, Groups(v));
    }

    public override string PartName(IReadOnlyList<double> v, ShapePreset preset, Func<string, string> text)
      => text("Shape_Tape") + " B" + N(v[Strip]) + " " + N(v[Length]) + "x" + N(Strips(v) * v[Strip]);

    public override IEnumerable<DxfEntity> Draw(IReadOnlyList<double> v)
    {
      int strips = Strips(v);
      double length = v[Length], strip = v[Strip];
      yield return Cut(Rectangle(length, strips * strip));

      // The shared cut between neighbours, broken by a bridge about every BridgeEvery.
      int bridges = v[Bridge] > 0 ? Math.Max(0, (int)Math.Ceiling((length / v[BridgeEvery]) - 1e-9) - 1) : 0;
      var gaps = Enumerable.Range(1, bridges).Select(j => length * j / (bridges + 1)).ToList();
      for (int k = 1; k < strips; k++)
      {
        double y = k * strip, from = 0;
        foreach (double gap in gaps)
        {
          yield return InnerCut(new DxfLine(new DxfPoint(from, y, 0), new DxfPoint(gap - (v[Bridge] / 2), y, 0)));
          from = gap + (v[Bridge] / 2);
        }

        yield return InnerCut(new DxfLine(new DxfPoint(from, y, 0), new DxfPoint(length, y, 0)));
      }

      int groups = Groups(v);
      double first = (length - ((groups - 1) * v[Pitch])) / 2;
      for (int k = 0; k < strips; k++)
      {
        double yc = (k + 0.5) * strip;
        for (int g = 0; g < groups; g++)
        {
          double xc = first + (g * v[Pitch]);
          yield return Cut(new DxfCircle(new DxfPoint(xc, yc, 0), v[BigHole] / 2));
          foreach (double dx in new[] { -v[SmallOffset], v[SmallOffset] })
          {
            foreach (double dy in new[] { -v[RowGap] / 2, v[RowGap] / 2 })
            {
              yield return Cut(new DxfCircle(new DxfPoint(xc + dx, yc + dy, 0), v[SmallHole] / 2));
            }
          }
        }
      }
    }

    /// <summary>How many whole strips the blank holds.</summary>
    public static int Strips(IReadOnlyList<double> v) => (int)Math.Floor((v[Width] / v[Strip]) + 1e-9);

    /// <summary>How many hole groups fit along the strip, keeping a small hole's width of metal at each end.</summary>
    public static int Groups(IReadOnlyList<double> v)
    {
      double reach = Math.Max(v[SmallOffset] + (v[SmallHole] / 2), v[BigHole] / 2);
      double room = v[Length] - (2 * reach) - (2 * v[SmallHole]);
      return room < 0 ? 0 : (int)Math.Floor((room / v[Pitch]) + 1e-9) + 1;
    }

    private static ShapePreset Tape(double strip, double bigHole, double pitch, double rowGap)
      => new ShapePreset("Shape_TapePreset", "B" + N(strip) + ", Ø" + N(bigHole) + " + 4×Ø3.2", new Dictionary<int, double>
      {
        [Strip] = strip,
        [BigHole] = bigHole,
        [SmallHole] = 3.2,
        [Pitch] = pitch,
        [RowGap] = rowGap,
        [SmallOffset] = 5,
      });
  }
}
