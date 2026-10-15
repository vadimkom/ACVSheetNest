namespace DeepNestSharp
{
  using System;
  using System.Diagnostics;
  using System.IO;
  using System.Linq;
  using System.Text.Json;

  /// <summary>
  /// AutoCAD-style autosave: every few minutes the dirty project is snapshotted to
  /// %LOCALAPPDATA%\SheetNest\autosave\recovery-{process}.dnest. A clean save/close deletes the snapshot, so
  /// one left behind by a process that is no longer running means that copy died with unsaved work — offer
  /// to recover it.
  /// </summary>
  /// <remarks>
  /// Each running copy has its own snapshot. There used to be one file for all of them, so a second copy
  /// (opened to compare a job, or a test run) wiped the first one's snapshot on a clean close, or offered it
  /// for recovery while it was still open; the operator then got "recover?" and, on yes, "file not found".
  /// </remarks>
  public static class Autosave
  {
    private static string Dir => Path.Combine(
      Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SheetNest", "autosave");

    private static int Pid => Environment.ProcessId;

    /// <summary>This copy's snapshot.</summary>
    private static string OwnPath => Path.Combine(Dir, $"recovery-{Pid}.dnest");

    private static string MetaOf(string snapshot) => Path.ChangeExtension(snapshot, ".meta.json");

    /// <summary>Snapshots the project json; remembers the original file so recovery can re-point it.</summary>
    public static void Write(string projectJson, string originalFilePath)
    {
      try
      {
        Directory.CreateDirectory(Dir);
        File.WriteAllText(OwnPath, projectJson);
        File.WriteAllText(MetaOf(OwnPath), JsonSerializer.Serialize(new Meta
        {
          OriginalPath = originalFilePath ?? string.Empty,
          SavedAtLocal = DateTime.Now.ToString("yyyy-MM-dd HH:mm"),
          ProcessId = Pid,
          ProcessStartUtc = Process.GetCurrentProcess().StartTime.ToUniversalTime().Ticks,
        }));
      }
      catch
      {
        // autosave must never disturb the user
      }
    }

    /// <summary>The newest snapshot left by a copy that is no longer running, with its details; false when
    /// there is none. A running copy's snapshot is its own business.</summary>
    public static bool TryGetPending(out string snapshotPath, out string originalPath, out string savedAt)
    {
      snapshotPath = string.Empty;
      originalPath = string.Empty;
      savedAt = string.Empty;
      try
      {
        if (!Directory.Exists(Dir))
        {
          return false;
        }

        foreach (string file in Directory.EnumerateFiles(Dir, "recovery*.dnest").OrderByDescending(File.GetLastWriteTimeUtc))
        {
          Meta meta = null;
          if (File.Exists(MetaOf(file)))
          {
            meta = JsonSerializer.Deserialize<Meta>(File.ReadAllText(MetaOf(file)));
          }

          if (meta != null && IsRunning(meta))
          {
            continue;
          }

          snapshotPath = file;
          originalPath = meta?.OriginalPath ?? string.Empty;
          savedAt = meta?.SavedAtLocal ?? string.Empty;
          return true;
        }
      }
      catch
      {
        // fall through: nothing to offer
      }

      return false;
    }

    /// <summary>Removes this copy's snapshot (clean save or close).</summary>
    public static void Clear() => Remove(OwnPath);

    /// <summary>Removes a snapshot left behind by a copy that died, once it is recovered or declined.</summary>
    public static void Remove(string snapshotPath)
    {
      try
      {
        if (string.IsNullOrEmpty(snapshotPath))
        {
          return;
        }

        if (File.Exists(snapshotPath))
        {
          File.Delete(snapshotPath);
        }

        if (File.Exists(MetaOf(snapshotPath)))
        {
          File.Delete(MetaOf(snapshotPath));
        }
      }
      catch
      {
        // best effort
      }
    }

    private static bool IsRunning(Meta meta)
    {
      if (meta.ProcessId <= 0 || meta.ProcessId == Pid)
      {
        return false;
      }

      try
      {
        using var process = Process.GetProcessById(meta.ProcessId);

        // A process id comes round again: only the same process started at the same time is the writer.
        return Math.Abs(process.StartTime.ToUniversalTime().Ticks - meta.ProcessStartUtc) < TimeSpan.TicksPerSecond;
      }
      catch
      {
        return false;
      }
    }

    private sealed class Meta
    {
      public string OriginalPath { get; set; }

      public string SavedAtLocal { get; set; }

      public int ProcessId { get; set; }

      public long ProcessStartUtc { get; set; }
    }
  }
}
