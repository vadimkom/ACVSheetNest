namespace DeepNestSharp.RasterNest
{
  using System;
  using System.Collections.Generic;
  using System.Globalization;
  using System.IO;
  using System.Linq;
  using System.Text.Json;

  /// <summary>
  /// How each nesting engine fares in the shop (2026-11-19, the user: "keep statistics per engine: how many times
  /// best, how many times run, how many times its DXF was saved, the size of the job; then we will see").
  /// Totals go to %LOCALAPPDATA%\SheetNest\engine-stats.json and every finished run is a line of
  /// engine-runs.csv beside it, for a closer look later. An engine is keyed by its name as the Engine column
  /// shows it; a Draft is its own key ("Sparrow/draft").
  /// </summary>
  internal static class EngineStats
  {
    /// <summary>The suffix of a Draft's key.</summary>
    internal const string DraftSuffix = "/draft";

    private static readonly object Gate = new object();

    /// <summary>Where the files go; tests point it elsewhere.</summary>
    internal static string Folder { get; set; } = Path.Combine(
      Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SheetNest");

    internal static string TotalsPath => Path.Combine(Folder, "engine-stats.json");

    internal static string RunsPath => Path.Combine(Folder, "engine-runs.csv");

    /// <summary>The key of an engine's runs: its name, and <see cref="DraftSuffix"/> for a Draft.</summary>
    internal static string Key(string engine, bool draft) => (engine ?? string.Empty) + (draft ? DraftSuffix : string.Empty);

    /// <summary>One engine's totals.</summary>
    internal sealed class Entry
    {
      /// <summary>Gets or sets the finished runs (a result came back).</summary>
      public int Runs { get; set; }

      /// <summary>Gets or sets the runs that gave no result for some sheet.</summary>
      public int Failed { get; set; }

      /// <summary>Gets or sets the jobs it was run on beside at least one other engine.</summary>
      public int Compared { get; set; }

      /// <summary>Gets or sets how many of those jobs its best run was the best of all.</summary>
      public int Best { get; set; }

      /// <summary>Gets or sets how many times a result of it was exported to DXF.</summary>
      public int DxfSaved { get; set; }

      /// <summary>Gets or sets the pieces over all runs (for the average job).</summary>
      public long Pieces { get; set; }

      /// <summary>Gets or sets the kinds of part over all runs.</summary>
      public long Kinds { get; set; }

      /// <summary>Gets or sets the most pieces in one run.</summary>
      public int MaxPieces { get; set; }

      /// <summary>Gets or sets the seconds over all runs.</summary>
      public double Seconds { get; set; }

      /// <summary>Gets or sets when it last ran.</summary>
      public DateTime? LastRun { get; set; }
    }

    /// <summary>All the totals, by key. Never throws: an unreadable file reads as empty.</summary>
    internal static Dictionary<string, Entry> Load()
    {
      lock (Gate)
      {
        return LoadUnlocked();
      }
    }

    /// <summary>A finished run: one line in the runs file and the engine's totals.</summary>
    internal static void NoteRun(string key, int pieces, int kinds, int placed, int sheets, double utilization, double seconds, string? project)
    {
      Update(key, e =>
      {
        e.Runs++;
        e.Pieces += pieces;
        e.Kinds += kinds;
        e.MaxPieces = Math.Max(e.MaxPieces, pieces);
        e.Seconds += seconds;
        e.LastRun = DateTime.Now;
      });

      try
      {
        lock (Gate)
        {
          Directory.CreateDirectory(Folder);
          bool fresh = !File.Exists(RunsPath);
          string line = string.Join(
            ";",
            DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
            Csv(key),
            pieces.ToString(CultureInfo.InvariantCulture),
            kinds.ToString(CultureInfo.InvariantCulture),
            placed.ToString(CultureInfo.InvariantCulture),
            sheets.ToString(CultureInfo.InvariantCulture),
            (utilization * 100).ToString("0.##", CultureInfo.InvariantCulture),
            seconds.ToString("0.#", CultureInfo.InvariantCulture),
            Csv(project ?? string.Empty));
          File.AppendAllText(RunsPath, (fresh ? "time;engine;pieces;kinds;placed;sheets;util%;seconds;project\r\n" : string.Empty) + line + "\r\n");
        }
      }
      catch (Exception)
      {
        // Statistics never stop a nest.
      }
    }

    /// <summary>A run that gave no result for some sheet.</summary>
    internal static void NoteFailure(string key) => Update(key, e => { e.Failed++; e.LastRun = DateTime.Now; });

    /// <summary>A result of the engine went out as DXF.</summary>
    internal static void NoteDxf(string key) => Update(key, e => e.DxfSaved++);

    /// <summary>
    /// A job is done with (the list was cleared, another job opened, the program closed). When two engines or more
    /// were run on it, each counts it as compared and the one whose run is starred as the best.
    /// </summary>
    internal static void NoteJob(IEnumerable<string> keys, string? best)
    {
      var all = keys.Where(k => !string.IsNullOrEmpty(k)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
      if (all.Count < 2 || string.IsNullOrEmpty(best))
      {
        return;
      }

      lock (Gate)
      {
        var totals = LoadUnlocked();
        foreach (string k in all)
        {
          var e = Get(totals, k);
          e.Compared++;
          if (string.Equals(k, best, StringComparison.OrdinalIgnoreCase))
          {
            e.Best++;
          }
        }

        SaveUnlocked(totals);
      }
    }

    private static void Update(string key, Action<Entry> change)
    {
      if (string.IsNullOrEmpty(key))
      {
        return;
      }

      lock (Gate)
      {
        var totals = LoadUnlocked();
        change(Get(totals, key));
        SaveUnlocked(totals);
      }
    }

    private static Entry Get(Dictionary<string, Entry> totals, string key)
    {
      if (!totals.TryGetValue(key, out var e))
      {
        e = new Entry();
        totals[key] = e;
      }

      return e;
    }

    private static Dictionary<string, Entry> LoadUnlocked()
    {
      try
      {
        if (File.Exists(TotalsPath))
        {
          var read = JsonSerializer.Deserialize<Dictionary<string, Entry>>(File.ReadAllText(TotalsPath));
          if (read != null)
          {
            return new Dictionary<string, Entry>(read, StringComparer.OrdinalIgnoreCase);
          }
        }
      }
      catch (Exception)
      {
        // A broken file starts the counts again rather than stopping the program.
      }

      return new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
    }

    private static void SaveUnlocked(Dictionary<string, Entry> totals)
    {
      try
      {
        Directory.CreateDirectory(Folder);
        string tmp = TotalsPath + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(totals, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(tmp, TotalsPath, true);
      }
      catch (Exception)
      {
        // Statistics never stop the program.
      }
    }

    private static string Csv(string s) => s.Replace(";", ",").Replace("\r", " ").Replace("\n", " ");
  }
}
