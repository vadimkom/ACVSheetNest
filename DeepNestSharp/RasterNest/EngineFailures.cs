namespace DeepNestSharp.RasterNest
{
  using System;
  using System.Collections.Generic;
  using System.Globalization;
  using System.IO;
  using System.Linq;
  using System.Text;
  using System.Threading;

  /// <summary>How an engine run ended without an answer.</summary>
  internal enum EngineFailureKind
  {
    /// <summary>The process could not be started.</summary>
    NotStarted,

    /// <summary>It exited without writing output\final_job.json (a crash, an error message).</summary>
    NoAnswer,

    /// <summary>The watchdog killed it.</summary>
    TimedOut,

    /// <summary>Stopped because another try of the same sheet had long finished (<see cref="SparrowNestService.WaveLeash"/>).</summary>
    Outrun,
  }

  /// <summary>One engine run that gave no answer, with the folder its job and output were kept in.</summary>
  internal sealed record EngineFailure(string Engine, EngineFailureKind Kind, int ExitCode, double Seconds, string? Folder);

  /// <summary>A sheet for which no try of the engine answered: the nest could not lay it.</summary>
  internal sealed record SheetEngineFailure(EngineFailure First, double SheetWidth, double SheetHeight);

  /// <summary>
  /// What is kept when the nesting engine fails (2026-11-18, the user: "just a notice, and a log to fix it;
  /// try another"). Every failed run leaves a folder under %LOCALAPPDATA%\SheetNest\engine-failures with the
  /// job it was handed, its command line and its output, so that whoever wrote the engine can run the same
  /// thing again. A sheet that no try could lay is noted for the window, which says which engine failed and
  /// offers the folder. Nothing is laid by another engine in its place.
  /// </summary>
  internal static class EngineFailures
  {
    /// <summary>The most failure folders kept; the oldest go first.</summary>
    internal const int Keep = 30;

    /// <summary>The most output lines kept from one run: the end of it, where a crash says why.</summary>
    internal const int OutputLines = 500;

    private static readonly AsyncLocal<List<SheetEngineFailure>?> Collector = new AsyncLocal<List<SheetEngineFailure>?>();

    /// <summary>Where the folders go; tests point it elsewhere.</summary>
    internal static string Root { get; set; } = Path.Combine(
      Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SheetNest", "engine-failures");

    /// <summary>The engine's name as the operator sees it: its file name without ".exe".</summary>
    internal static string EngineName(string exe) => Path.GetFileNameWithoutExtension(exe ?? string.Empty);

    /// <summary>
    /// Starts noting the sheets that fail in the work started from here on (an <see cref="AsyncLocal{T}"/>,
    /// so a nest in the background, such as the virtual sheets, keeps its own). Returns the list that fills.
    /// </summary>
    internal static List<SheetEngineFailure> Collect()
    {
      var list = new List<SheetEngineFailure>();
      Collector.Value = list;
      return list;
    }

    /// <summary>Stops noting sheets for the current flow.</summary>
    internal static void StopCollecting() => Collector.Value = null;

    /// <summary>Notes a sheet that no try of the engine could lay, if someone is collecting.</summary>
    internal static void NoteSheet(EngineFailure? first, double sheetWidth, double sheetHeight)
    {
      if (first == null || Collector.Value is not List<SheetEngineFailure> list)
      {
        return;
      }

      lock (list)
      {
        list.Add(new SheetEngineFailure(first, sheetWidth, sheetHeight));
      }
    }

    /// <summary>
    /// Keeps the job, the command line and the output of a run that failed, in a new folder, and returns the
    /// failure with that folder (null when nothing could be written: a failure to log must not fail the nest).
    /// </summary>
    internal static EngineFailure Save(
      string exe,
      string inputPath,
      IEnumerable<string> arguments,
      EngineFailureKind kind,
      int exitCode,
      double seconds,
      string reason,
      IReadOnlyCollection<string> output)
    {
      string engine = EngineName(exe);
      string? folder = null;
      try
      {
        Directory.CreateDirectory(Root);
        string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture);
        folder = Path.Combine(Root, $"{Safe(engine)}-{stamp}");
        for (int n = 2; Directory.Exists(folder); n++)
        {
          folder = Path.Combine(Root, $"{Safe(engine)}-{stamp}-{n}");
        }

        Directory.CreateDirectory(folder);
        if (File.Exists(inputPath))
        {
          File.Copy(inputPath, Path.Combine(folder, "job.json"));
        }

        var p = new StringBuilder();
        p.AppendLine("engine:  " + exe);
        p.AppendLine("args:    " + string.Join(" ", arguments.Select(Quote)).Replace(inputPath, "job.json", StringComparison.OrdinalIgnoreCase));
        p.AppendLine("result:  " + kind + (kind == EngineFailureKind.NoAnswer ? FormattableString.Invariant($" (exit {exitCode})") : string.Empty));
        p.AppendLine("reason:  " + reason);
        p.AppendLine(FormattableString.Invariant($"seconds: {seconds:F1}"));
        p.AppendLine("when:    " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
        p.AppendLine("app:     SheetNest " + typeof(EngineFailures).Assembly.GetName().Version);
        p.AppendLine("machine: " + Environment.ProcessorCount.ToString(CultureInfo.InvariantCulture) + " logical cores");
        p.AppendLine();
        p.AppendLine("Run it again from this folder (an empty working folder; the answer goes to output\\final_job.json):");
        p.AppendLine("  mkdir try; cd try; & '" + exe + "' " + string.Join(" ", arguments.Select(Quote)).Replace(inputPath, "..\\job.json", StringComparison.OrdinalIgnoreCase));
        File.WriteAllText(Path.Combine(folder, "params.txt"), p.ToString());
        File.WriteAllLines(Path.Combine(folder, "log.txt"), output);
        Prune();
      }
      catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
      {
        folder = null;
      }

      return new EngineFailure(engine, kind, exitCode, seconds, folder);
    }

    /// <summary>Keeps the newest <see cref="Keep"/> folders.</summary>
    private static void Prune()
    {
      var old = new DirectoryInfo(Root).GetDirectories()
        .OrderByDescending(d => d.CreationTimeUtc)
        .ThenByDescending(d => d.Name, StringComparer.Ordinal)
        .Skip(Keep);
      foreach (var d in old)
      {
        try
        {
          d.Delete(true);
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
        {
        }
      }
    }

    private static string Safe(string name)
    {
      var invalid = Path.GetInvalidFileNameChars();
      string s = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
      return s.Length == 0 ? "engine" : s;
    }

    private static string Quote(string a) => a.Contains(' ', StringComparison.Ordinal) ? "\"" + a + "\"" : a;
  }
}
