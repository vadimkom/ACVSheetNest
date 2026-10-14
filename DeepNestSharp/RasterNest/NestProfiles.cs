namespace DeepNestSharp.RasterNest
{
  using System;
  using System.IO;
  using System.Text.Json;
  using System.Text.Json.Serialization;

  /// <summary>
  /// How hard one of the two nest buttons searches. Every number the engine is driven by for that button is
  /// here, so the operator can tune it in a file without a build.
  /// </summary>
  internal sealed class NestProfile
  {
    /// <summary>Engine iterations per sheet: the main dial between time and tightness.</summary>
    public int Iterations { get; set; }

    /// <summary>Searches raced per sheet from different starts; the best one is kept.</summary>
    public int Tries { get; set; }

    /// <summary>Searches raced for the last, part-filled sheet, which nothing is replicated from.</summary>
    public int LastSheetTries { get; set; }

    /// <summary>Processor threads each search uses.</summary>
    public int WorkersPerSearch { get; set; }

    /// <summary>Iterations of the harder second pack of the last sheet when parts would otherwise be left
    /// uncut; 0 skips it.</summary>
    public int LastChanceIterations { get; set; }

    /// <summary>Offer a sheet the big parts still waiting when it came back with room (see FillUp).</summary>
    public bool FillUp { get; set; }

    /// <summary>The share of a sheet a part has to be for FillUp to offer it.</summary>
    public double FillUpMinShare { get; set; }

    /// <summary>The most extra packs FillUp may spend on one sheet.</summary>
    public int FillUpMaxAttempts { get; set; }

    /// <summary>
    /// Search switches handed to the engine as they are. Shrink step: how far it squeezes the strip at a time
    /// (bigger is coarser and quicker). Max fails: how many failed squeezes end the search. No-improve limit:
    /// iterations without gain before it stops. Left out of a common-cutting sheet, whose references were
    /// measured without them.
    /// </summary>
    public string[] EngineFlags { get; set; }
  }

  /// <summary>
  /// The two buttons the operator nests with. "Draft" answers how much material and cutting an order needs,
  /// for a quote, in seconds. "Nest" is the layout that gets cut, with what used to be called Fast.
  /// </summary>
  internal static class NestProfiles
  {
    internal const string FileName = "nest-profiles.json";

    /// <summary>The draft for a quote: area, cut length and a number of sheets, as quickly as possible.</summary>
    internal static NestProfile Draft { get; private set; } = DraftDefaults();

    /// <summary>The layout that gets cut: what the Fast effort was, measured as the best value for the time.</summary>
    internal static NestProfile Normal { get; private set; } = NormalDefaults();

    /// <summary>The profile for an effort, or null for the older efforts, which keep their built-in numbers.</summary>
    internal static NestProfile For(NestEffort effort) => effort switch
    {
      NestEffort.Draft => Draft,
      NestEffort.Fast => Normal,
      _ => null,
    };

    internal static NestProfile DraftDefaults() => new NestProfile
    {
      Iterations = 100,
      Tries = 1,
      LastSheetTries = 1,
      WorkersPerSearch = 1,
      LastChanceIterations = 0,
      FillUp = true,
      FillUpMinShare = 0.05,
      FillUpMaxAttempts = 2,
      EngineFlags = new[] { "--shrink-step", "0.01", "--max-fails", "2", "--compress-share", "0", "--no-improve-limit", "20" },
    };

    internal static NestProfile NormalDefaults() => new NestProfile
    {
      Iterations = 300,
      Tries = 2,
      LastSheetTries = 4,
      WorkersPerSearch = 1,
      LastChanceIterations = 600,
      FillUp = true,
      FillUpMinShare = 0.05,
      FillUpMaxAttempts = 4,
      EngineFlags = new[] { "--shrink-step", "0.005", "--max-fails", "3", "--compress-share", "0", "--no-improve-limit", "50" },
    };

    /// <summary>
    /// Reads the operator's file, writing it with the defaults first if there is none, so there is always a
    /// file to edit. Read before every nest: an edit takes effect on the next press. A file that does not
    /// read leaves the defaults in force and says why.
    /// </summary>
    internal static string Load(string folder)
    {
      var (draft, normal, error) = Read(folder);
      Draft = draft;
      Normal = normal;
      return error;
    }

    /// <summary>What the file in the folder says, without putting it in force; see <see cref="Load"/>.</summary>
    internal static (NestProfile Draft, NestProfile Normal, string Error) Read(string folder)
    {
      string path = Path.Combine(folder, FileName);
      try
      {
        if (!File.Exists(path))
        {
          Directory.CreateDirectory(folder);
          File.WriteAllText(path, JsonSerializer.Serialize(new ProfileFile { Draft = DraftDefaults(), Normal = NormalDefaults() }, Options));
        }

        var file = JsonSerializer.Deserialize<ProfileFile>(File.ReadAllText(path), Options);
        return (Sane(file?.Draft, DraftDefaults()), Sane(file?.Normal, NormalDefaults()), null);
      }
      catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is JsonException)
      {
        return (DraftDefaults(), NormalDefaults(), ex.Message);
      }
    }

    /// <summary>Back to the built-in numbers (tests, and before the file is read).</summary>
    internal static void Reset()
    {
      Draft = DraftDefaults();
      Normal = NormalDefaults();
    }

    /// <summary>A missing or nonsensical value falls back to its default rather than stalling the engine.</summary>
    private static NestProfile Sane(NestProfile p, NestProfile d)
    {
      if (p == null)
      {
        return d;
      }

      return new NestProfile
      {
        Iterations = p.Iterations > 0 ? p.Iterations : d.Iterations,
        Tries = Math.Clamp(p.Tries > 0 ? p.Tries : d.Tries, 1, 16),
        LastSheetTries = Math.Clamp(p.LastSheetTries > 0 ? p.LastSheetTries : d.LastSheetTries, 1, 16),
        WorkersPerSearch = Math.Clamp(p.WorkersPerSearch > 0 ? p.WorkersPerSearch : d.WorkersPerSearch, 1, 8),
        LastChanceIterations = Math.Max(0, p.LastChanceIterations),
        FillUp = p.FillUp,
        FillUpMinShare = p.FillUpMinShare > 0 && p.FillUpMinShare < 1 ? p.FillUpMinShare : d.FillUpMinShare,
        FillUpMaxAttempts = Math.Clamp(p.FillUpMaxAttempts, 0, 16),
        EngineFlags = p.EngineFlags ?? d.EngineFlags,
      };
    }

    private static readonly JsonSerializerOptions Options = new JsonSerializerOptions
    {
      WriteIndented = true,
      ReadCommentHandling = JsonCommentHandling.Skip,
      AllowTrailingCommas = true,
    };

    private sealed class ProfileFile
    {
      [JsonPropertyOrder(-1)]
      public string Help { get; set; } =
        "Draft = the quick button for quotes; Normal = the Nest button. Iterations per sheet is the main dial. "
        + "Edits apply on the next press. Delete this file to get the defaults back.";

      public NestProfile Draft { get; set; }

      public NestProfile Normal { get; set; }
    }
  }
}
