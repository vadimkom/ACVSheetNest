namespace DeepNestSharp.CiTests.RasterNest
{
  using System;
  using System.Collections.Generic;
  using System.IO;
  using System.Linq;
  using DeepNestLib.IO;
  using DeepNestSharp.RasterNest;
  using FluentAssertions;
  using Xunit;

  /// <summary>
  /// An engine that answers nothing (2026-11-18, the user: "just a notice, and a log to fix it; try another"):
  /// the sheet is noted for the window and the job, command line and output are kept in a folder.
  /// where.exe stands in for a crashing engine: it rejects Sparrow's options and exits 2 at once.
  /// </summary>
  public class EngineFailuresFixture : TempFolderFixture
  {
    private static readonly string Crashing = Path.Combine(Environment.SystemDirectory, "where.exe");

    [Fact]
    public void ACrashingEngineLeavesTheJobAndItsOutputAndNamesTheSheet()
    {
      string dir = this.NewTempFolder("sheetnest_enginefail");
      string root = Path.Combine(dir, "failures");
      string oldRoot = EngineFailures.Root;
      EngineFailures.Root = root;
      List<SheetEngineFailure> failures;
      try
      {
        var parts = new List<RasterPartInfo>
        {
          new RasterPartInfo { Path = PartShapes.Write(dir, "disc", PartShapes.Circle, new double[] { 120 }, millimetres: true), Quantity = 3 },
        };

        failures = EngineFailures.Collect();
        try
        {
          SparrowNestService.Nest(
            parts,
            new[] { (500, 400, 1, false, 5) },
            rotations: 4,
            spacing: 5,
            margin: 5,
            perSheetBudgetSec: 5,
            Crashing,
            out string _,
            effort: NestEffort.Fast);
        }
        finally
        {
          EngineFailures.StopCollecting();
        }
      }
      finally
      {
        EngineFailures.Root = oldRoot;
      }

      failures.Should().NotBeEmpty();
      var first = failures[0];
      first.First.Engine.Should().Be("where");
      first.First.Kind.Should().Be(EngineFailureKind.NoAnswer);
      first.First.ExitCode.Should().NotBe(0);
      (first.SheetWidth, first.SheetHeight).Should().Be((500d, 400d));

      first.First.Folder.Should().NotBeNull();
      Directory.GetParent(first.First.Folder!)!.FullName.Should().Be(root);
      File.Exists(Path.Combine(first.First.Folder!, "job.json")).Should().BeTrue();
      File.ReadAllText(Path.Combine(first.First.Folder!, "params.txt")).Should().Contain("NoAnswer").And.Contain("job.json");
      File.ReadAllLines(Path.Combine(first.First.Folder!, "log.txt")).Should().NotBeEmpty("where.exe says why it refused");
    }

    [Fact]
    public void OnlyTheNewestFoldersAreKept()
    {
      string dir = this.NewTempFolder("sheetnest_enginefail_prune");
      string oldRoot = EngineFailures.Root;
      EngineFailures.Root = Path.Combine(dir, "failures");
      try
      {
        for (int i = 0; i < EngineFailures.Keep + 5; i++)
        {
          EngineFailures.Save(Crashing, Path.Combine(dir, "none.json"), new[] { "-i", "none.json" }, EngineFailureKind.NoAnswer, 2, 0.1, "test", new[] { "line" })
            .Folder.Should().NotBeNull();
        }

        Directory.GetDirectories(EngineFailures.Root).Should().HaveCount(EngineFailures.Keep);
      }
      finally
      {
        EngineFailures.Root = oldRoot;
      }
    }

    [Fact]
    public void NothingIsNotedWhenNobodyCollects()
    {
      EngineFailures.StopCollecting();
      var failure = new EngineFailure("x", EngineFailureKind.TimedOut, -1, 1, null);
      EngineFailures.NoteSheet(failure, 1, 1);

      var list = EngineFailures.Collect();
      EngineFailures.StopCollecting();
      list.Should().BeEmpty();
    }
  }
}
