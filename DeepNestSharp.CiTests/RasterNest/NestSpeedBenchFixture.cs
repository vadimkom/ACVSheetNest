namespace DeepNestSharp.CiTests.RasterNest
{
  using System;
  using System.Diagnostics;
  using System.Globalization;
  using System.IO;
  using System.Linq;
  using System.Text;
  using DeepNestLib;
  using DeepNestLib.NestProject;
  using DeepNestLib.Placement;
  using DeepNestSharp.RasterNest;
  using Xunit;
  using Xunit.Abstractions;

  /// <summary>
  /// Where a real project's nest spends its time, the way the window runs it. Opt-in: SHEETNEST_BENCH_PROJECT
  /// (a .dnest) plus SPARROW_EXE. SHEETNEST_BENCH_EFFORT (Fast/Normal/Best), SHEETNEST_BENCH_SPACING and
  /// SHEETNEST_BENCH_MARGIN (drawing units) and SHEETNEST_BENCH_ROTATIONS stand in for the operator's settings;
  /// SHEETNEST_MAX_CONC reproduces a smaller machine's scheduling (a 4-thread Celeron runs 3 Fast searches).
  /// </summary>
  [Collection(SparrowEngineCollection.Name)]
  public class NestSpeedBenchFixture
  {
    private readonly ITestOutputHelper output;

    public NestSpeedBenchFixture(ITestOutputHelper output)
    {
      this.output = output;
    }

    [Fact]
    public void MeasureProjectNest()
    {
      string exe = SparrowExe.Resolve();
      string projectPath = Environment.GetEnvironmentVariable("SHEETNEST_BENCH_PROJECT");
      if (string.IsNullOrWhiteSpace(exe) || string.IsNullOrWhiteSpace(projectPath) || !File.Exists(projectPath))
      {
        return;
      }

      var effort = Enum.TryParse(Environment.GetEnvironmentVariable("SHEETNEST_BENCH_EFFORT"), true, out NestEffort e) ? e : NestEffort.Fast;
      double spacing = Read("SHEETNEST_BENCH_SPACING", 5);
      double margin = Read("SHEETNEST_BENCH_MARGIN", 6);
      int rotations = (int)Read("SHEETNEST_BENCH_ROTATIONS", 4);
      int runs = (int)Read("SHEETNEST_BENCH_RUNS", 1);
      int qtyMult = Math.Max(1, (int)Read("SHEETNEST_BENCH_QTY_MULT", 1));

      var project = new ProjectInfo(new TestSvgNestConfig());
      project.Load(new TestSvgNestConfig(), projectPath);
      var parts = project.DetailLoadInfos
        .Where(o => o.IsIncluded && !string.IsNullOrWhiteSpace(o.Path))
        .Select(o => new RasterPartInfo { Path = o.Path, Quantity = (o.Quantity + o.Extra) * qtyMult, Rotations = o.Rotations, Priority = o.Priority, Spacing = o.Spacing })
        .ToList();
      var stock = project.SheetLoadInfos
        .Where(s => s.Width > 0 && s.Height > 0 && (s.Unlimited || s.Quantity > 0))
        .Select(s => (s.Width, s.Height, s.Quantity, s.Unlimited, s.Priority))
        .ToList();

      var helper = new NestExecutionHelper();
      var log = new StringBuilder();
      foreach (var p in parts)
      {
        int points = helper.LoadRawDetail(new FileInfo(p.Path)) is { } raw && raw.TryConvertToNfp(0, out INfp nfp)
          ? nfp.Points.Length + (nfp.Children?.Sum(c => c.Points.Length) ?? 0)
          : -1;
        log.AppendLine(FormattableString.Invariant($"# {Path.GetFileName(p.Path)} x{p.Quantity}: {points} points"));
      }

      log.AppendLine(FormattableString.Invariant(
        $"# iters={SparrowNestService.IterationBudget(effort)} tries={Environment.GetEnvironmentVariable("SHEETNEST_NEST_TRIES") ?? "default"} effort={effort} spacing={spacing} margin={margin} rotations={rotations} conc={Environment.GetEnvironmentVariable("SHEETNEST_MAX_CONC") ?? "auto"} cores={Environment.ProcessorCount}"));
      log.AppendLine("run,totalSec,sparrowSec,mapSec,holesSec,loadSec,waves,tries,sheets,placed,unplaced,utilPct,lastSheetPct,lastExtentMm");
      for (int r = 1; r <= runs; r++)
      {
        SparrowNestService.DiagLoadMs = 0;
        SparrowNestService.DiagSparrowMs = 0;
        SparrowNestService.DiagMapMs = 0;
        SparrowNestService.DiagHolesMs = 0;
        SparrowNestService.DiagWaves = 0;
        SparrowNestService.DiagTries = 0;

        var sw = Stopwatch.StartNew();
        // Each run races a different set of seeds, as pressing Nest again in the window does.
        var result = SparrowNestService.Nest(parts, stock, rotations, spacing, margin, 5, exe, out string err, effort: effort, seedOffset: 50 * (r - 1));
        sw.Stop();

        log.AppendLine(FormattableString.Invariant(
          $"{r},{sw.Elapsed.TotalSeconds:F1},{SparrowNestService.DiagSparrowMs / 1000.0:F1},{SparrowNestService.DiagMapMs / 1000.0:F1},{SparrowNestService.DiagHolesMs / 1000.0:F1},{SparrowNestService.DiagLoadMs / 1000.0:F1},{SparrowNestService.DiagWaves},{SparrowNestService.DiagTries},{result?.UsedSheets.Count ?? -1},{result?.TotalPlacedCount ?? -1},{result?.UnplacedParts.Count ?? -1},{(result?.MaterialUtilization ?? 0) * 100:F1},{LastSheetFill(result):F1},{LastExtent(result):F0}{(err == null ? string.Empty : " " + err)}"));
      }

      this.output.WriteLine(log.ToString());
      string outPath = Environment.GetEnvironmentVariable("SHEETNEST_PERF_OUT");
      if (!string.IsNullOrEmpty(outPath))
      {
        File.WriteAllText(outPath, log.ToString());
      }
    }

    /// <summary>Per drawing: reading it, turning it into a polygon, and how much geometry it holds.</summary>
    [Fact]
    public void MeasureProjectLoad()
    {
      string projectPath = Environment.GetEnvironmentVariable("SHEETNEST_BENCH_PROJECT");
      if (string.IsNullOrWhiteSpace(projectPath) || !File.Exists(projectPath))
      {
        return;
      }

      var project = new ProjectInfo(new TestSvgNestConfig());
      project.Load(new TestSvgNestConfig(), projectPath);
      var helper = new NestExecutionHelper();
      var log = new StringBuilder("file,kb,readMs,toNfpMs,outerPts,holes,holePts\n");
      foreach (var path in project.DetailLoadInfos.Select(d => d.Path).Distinct())
      {
        var sw = Stopwatch.StartNew();
        var raw = helper.LoadRawDetail(new FileInfo(path));
        long read = sw.ElapsedMilliseconds;
        sw.Restart();
        bool ok = raw != null && raw.TryConvertToNfp(0, out INfp nfp) && nfp != null;
        long convert = sw.ElapsedMilliseconds;
        INfp shape = null;
        if (ok)
        {
          raw.TryConvertToNfp(0, out shape);
        }

        log.AppendLine(FormattableString.Invariant(
          $"{Path.GetFileName(path)},{new FileInfo(path).Length / 1024},{read},{convert},{shape?.Points.Length ?? -1},{shape?.Children?.Count ?? 0},{shape?.Children?.Sum(c => c.Points.Length) ?? 0}"));
      }

      this.output.WriteLine(log.ToString());
      string outPath = Environment.GetEnvironmentVariable("SHEETNEST_PERF_OUT");
      if (!string.IsNullOrEmpty(outPath))
      {
        File.WriteAllText(outPath + ".load.csv", log.ToString());
      }
    }

    /// <summary>How full the last sheet is: the tail is where a better search leaves a bigger offcut.</summary>
    private static double LastSheetFill(INestResult result)
    {
      var last = result?.UsedSheets.LastOrDefault();
      if (last == null)
      {
        return 0;
      }

      double area = last.Sheet.WidthCalculated * last.Sheet.HeightCalculated;
      return area <= 0 ? 0 : last.PartPlacements.Sum(p => Math.Abs(p.Part.NetArea)) / area * 100;
    }

    /// <summary>How much of the last sheet's long axis its parts take: the rest is the offcut.</summary>
    private static double LastExtent(INestResult result)
    {
      var last = result?.UsedSheets.LastOrDefault();
      if (last == null || last.PartPlacements.Count == 0)
      {
        return 0;
      }

      var (w, h) = SparrowNestService.Extent(last.PartPlacements.ToList());
      return last.Sheet.WidthCalculated >= last.Sheet.HeightCalculated ? w : h;
    }

    private static double Read(string name, double fallback)
      => double.TryParse(Environment.GetEnvironmentVariable(name), NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : fallback;
  }
}
