namespace DeepNestSharp.CiTests.RasterNest
{
  using System.IO;
  using DeepNestSharp.RasterNest;
  using FluentAssertions;
  using Xunit;

  /// <summary>
  /// The engines' statistics (2026-11-19, the user: "how many times best, how many times run, how many times its
  /// DXF was saved, the size of the job"): totals in engine-stats.json, a line per run in engine-runs.csv.
  /// </summary>
  public class EngineStatsFixture : TempFolderFixture
  {
    [Fact]
    public void RunsBestJobsAndDxfAreCountedPerEngineAndADraftApart()
    {
      string old = EngineStats.Folder;
      EngineStats.Folder = this.NewTempFolder("sheetnest_enginestats");
      try
      {
        string sparrow = EngineStats.Key("Sparrow", false);
        string draft = EngineStats.Key("Sparrow", true);
        string opus = EngineStats.Key("opus55-high", false);

        EngineStats.NoteRun(sparrow, 40, 4, 40, 2, 0.71, 12.5, "job;1.dnest");
        EngineStats.NoteRun(sparrow, 60, 2, 58, 3, 0.65, 20, null);
        EngineStats.NoteRun(draft, 40, 4, 40, 2, 0.6, 2, null);
        EngineStats.NoteRun(opus, 40, 4, 40, 2, 0.73, 9, null);
        EngineStats.NoteFailure(opus);
        EngineStats.NoteJob(new[] { sparrow, opus, opus }, opus);
        EngineStats.NoteJob(new[] { sparrow }, sparrow); // one engine alone: no comparison
        EngineStats.NoteDxf(opus);

        var totals = EngineStats.Load();
        totals[sparrow].Runs.Should().Be(2);
        totals[sparrow].Pieces.Should().Be(100);
        totals[sparrow].MaxPieces.Should().Be(60);
        totals[sparrow].Compared.Should().Be(1);
        totals[sparrow].Best.Should().Be(0);
        totals[draft].Runs.Should().Be(1);
        totals[draft].Compared.Should().Be(0);
        totals[opus].Compared.Should().Be(1);
        totals[opus].Best.Should().Be(1);
        totals[opus].DxfSaved.Should().Be(1);
        totals[opus].Failed.Should().Be(1);

        var lines = File.ReadAllLines(EngineStats.RunsPath);
        lines.Should().HaveCount(5);
        lines[0].Should().StartWith("time;engine;");
        lines[1].Should().Contain(";Sparrow;40;4;40;2;71;12.5;job,1.dnest");
        lines[3].Should().Contain(";Sparrow/draft;");
      }
      finally
      {
        EngineStats.Folder = old;
      }
    }

    [Fact]
    public void ABrokenFileStartsTheCountsAgain()
    {
      string old = EngineStats.Folder;
      EngineStats.Folder = this.NewTempFolder("sheetnest_enginestats_broken");
      try
      {
        File.WriteAllText(EngineStats.TotalsPath, "{ not json");
        EngineStats.Load().Should().BeEmpty();
        EngineStats.NoteDxf("RNest");
        EngineStats.Load()["rnest"].DxfSaved.Should().Be(1);
      }
      finally
      {
        EngineStats.Folder = old;
      }
    }
  }
}
