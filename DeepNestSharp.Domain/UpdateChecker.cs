namespace DeepNestSharp.Domain
{
  using System;
  using System.IO;
  using System.Net.Http;
  using System.Threading;
  using System.Reflection;
  using System.Text.Json;
  using System.Threading.Tasks;

  /// <summary>
  /// Checks GitHub for a newer SheetNest release, and on the operator's word downloads its installer and
  /// installs it. Nothing about the user is sent: anonymous GETs to the public releases. Every failure of
  /// the check (offline shop PC, rate limit, bad JSON) returns null so callers can stay silent.
  /// </summary>
  public static class UpdateChecker
  {
    public const string ReleasesPageUrl = "https://github.com/vadimkom/ACVSheetNest/releases";

    private const string LatestReleaseApi = "https://api.github.com/repos/vadimkom/ACVSheetNest/releases/latest";

    /// <summary>Installers are taken from our own releases only, whatever the answer from the API says.</summary>
    public const string DownloadPrefix = "https://github.com/vadimkom/ACVSheetNest/releases/download/";

    /// <summary>Gets the running app version (the exe's, not this library's), normalized to 3 fields.</summary>
    public static Version CurrentVersion
    {
      get
      {
        var v = Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(0, 0, 0);
        return new Version(v.Major, v.Minor < 0 ? 0 : v.Minor, v.Build < 0 ? 0 : v.Build);
      }
    }

    /// <summary>Latest published release (version, its page, its installer or null), or null on any failure.</summary>
    public static async Task<(Version Latest, string Url, string MsiUrl)?> CheckAsync()
    {
      try
      {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        http.DefaultRequestHeaders.Add("User-Agent", "SheetNest");
        http.DefaultRequestHeaders.Add("Accept", "application/vnd.github+json");
        var json = await http.GetStringAsync(LatestReleaseApi).ConfigureAwait(false);
        return TryParseLatest(json);
      }
      catch
      {
        return null; // offline / rate-limited / anything — the caller stays silent
      }
    }

    /// <summary>Parses the GitHub "latest release" JSON into (version, page url, installer url); null if
    /// unusable. The installer is the release's .msi from our own releases; null when there is none.</summary>
    public static (Version Latest, string Url, string MsiUrl)? TryParseLatest(string json)
    {
      try
      {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("tag_name", out var tagProp))
        {
          return null;
        }

        var tag = (tagProp.GetString() ?? string.Empty).Trim().TrimStart('v', 'V');
        if (!Version.TryParse(tag, out var latest))
        {
          return null;
        }

        string url = ReleasesPageUrl;
        if (doc.RootElement.TryGetProperty("html_url", out var urlProp) && !string.IsNullOrWhiteSpace(urlProp.GetString()))
        {
          url = urlProp.GetString();
        }

        string msi = null;
        if (doc.RootElement.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
        {
          foreach (var asset in assets.EnumerateArray())
          {
            string link = asset.TryGetProperty("browser_download_url", out var l) ? l.GetString() : null;
            if (!string.IsNullOrEmpty(link)
              && link.StartsWith(DownloadPrefix, StringComparison.OrdinalIgnoreCase)
              && link.EndsWith(".msi", StringComparison.OrdinalIgnoreCase))
            {
              msi = link;
              break;
            }
          }
        }

        return (latest, url, msi);
      }
      catch
      {
        return null;
      }
    }

    /// <summary>True when the published release is newer than the running app (3-field compare).</summary>
    public static bool IsNewer(Version latest, Version current)
    {
      static Version Norm(Version v) => new Version(v.Major, v.Minor < 0 ? 0 : v.Minor, v.Build < 0 ? 0 : v.Build);
      return Norm(latest) > Norm(current);
    }

    /// <summary>
    /// Downloads a release's installer into %TEMP%\SheetNest-update and returns where it is. Only a link into
    /// our own releases is fetched (<see cref="DownloadPrefix"/>).
    /// </summary>
    public static async Task<string> DownloadAsync(string msiUrl, IProgress<double> progress, CancellationToken cancel)
    {
      if (string.IsNullOrEmpty(msiUrl) || !msiUrl.StartsWith(DownloadPrefix, StringComparison.OrdinalIgnoreCase))
      {
        throw new InvalidOperationException("Not a SheetNest release download: " + msiUrl);
      }

      string folder = Path.Combine(Path.GetTempPath(), "SheetNest-update");
      Directory.CreateDirectory(folder);
      string path = Path.Combine(folder, Path.GetFileName(new Uri(msiUrl).LocalPath));
      string partial = path + ".part";

      using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
      http.DefaultRequestHeaders.Add("User-Agent", "SheetNest");
      using var response = await http.GetAsync(msiUrl, HttpCompletionOption.ResponseHeadersRead, cancel).ConfigureAwait(false);
      response.EnsureSuccessStatusCode();
      long total = response.Content.Headers.ContentLength ?? -1;
      using (var source = await response.Content.ReadAsStreamAsync(cancel).ConfigureAwait(false))
      using (var target = File.Create(partial))
      {
        var buffer = new byte[81920];
        long done = 0;
        int read;
        while ((read = await source.ReadAsync(buffer, cancel).ConfigureAwait(false)) > 0)
        {
          await target.WriteAsync(buffer.AsMemory(0, read), cancel).ConfigureAwait(false);
          done += read;
          if (total > 0)
          {
            progress?.Report((double)done / total);
          }
        }
      }

      File.Move(partial, path, overwrite: true);
      return path;
    }

    /// <summary>
    /// Writes the script that installs a downloaded update once this copy has closed, and starts SheetNest
    /// again from where the installer put it. The installer only shows its progress (/passive): it replaces
    /// the previous version in place, no questions. Returns the script; run it hidden, then close.
    /// </summary>
    public static string WriteInstallScript(string msiPath, int processId)
    {
      string script = Path.Combine(Path.GetDirectoryName(msiPath) ?? Path.GetTempPath(), "install-update.cmd");
      var lines = new[]
      {
        "@echo off",
        "chcp 65001 >nul",
        "rem Waits for SheetNest to close, installs the update, starts SheetNest again.",
        ":wait",
        $@"tasklist /fi ""PID eq {processId}"" | %SystemRoot%\System32\find.exe ""{processId}"" >nul && (ping -n 2 127.0.0.1 >nul & goto wait)",
        $"start \"\" /wait msiexec /i \"{msiPath}\" /passive /norestart",
        "set DIR=",
        @"for /f ""tokens=2,*"" %%a in ('reg query ""HKCU\Software\SheetNest"" /v InstallFolder 2^>nul ^| %SystemRoot%\System32\find.exe ""InstallFolder""') do set DIR=%%b",
        "if defined DIR start \"\" \"%DIR%SheetNest.exe\"",
      };
      File.WriteAllLines(script, lines, new System.Text.UTF8Encoding(false)); // UTF-8 after chcp: a Cyrillic user folder survives
      return script;
    }

    /// <summary>Runs the install script with no window of its own.</summary>
    public static void RunInstallScript(string script)
    {
      System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe", "/c \"" + script + "\"")
      {
        UseShellExecute = false,
        CreateNoWindow = true,
      });
    }

    /// <summary>Opens the release page in the default browser (net6 needs UseShellExecute for URLs).</summary>
    public static void OpenDownloadPage(string url)
    {
      try
      {
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
      }
      catch
      {
        // no browser / blocked — nothing sensible to do
      }
    }
  }
}
