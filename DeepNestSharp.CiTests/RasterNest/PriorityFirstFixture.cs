namespace DeepNestSharp.CiTests.RasterNest
{
  using System;
  using System.Collections.Generic;
  using System.IO;
  using System.Linq;
  using DeepNestSharp.RasterNest;
  using FluentAssertions;
  using Xunit;
  using Xunit.Abstractions;

  /// <summary>
  /// A priority the operator sets is kept: an order of 125 % of the one sheet in stock (a pagoda cap,
  /// 2026-11-02) gave the four sides priority 1, and one side came back uncut while lamellas of priority 5 were
  /// on the sheet. The parts of the better priority go on; the worse ones take what room is left.
  /// </summary>
  [Collection(SparrowEngineCollection.Name)]
  public class PriorityFirstFixture
  {
    private readonly ITestOutputHelper output;

    public PriorityFirstFixture(ITestOutputHelper output)
    {
      this.output = output;
    }

    [SparrowTheory]
    [InlineData(true)]
    [InlineData(false)]
    public void SidesOfPriorityOneAllGoOnTheOneSheet(bool nest)
    {
      string data = FindData();
      data.Should().NotBeNull("test-data/priority holds the cap's flat patterns");
      string Part(string name) => Path.Combine(data, $"Flat-Pattern - {name}^shapka.pagoda.DXF");
      var parts = new List<RasterPartInfo>
      {
        new RasterPartInfo { Path = Part("vert"), Quantity = 2, Priority = 3 },
        new RasterPartInfo { Path = Part("vert"), Quantity = 2, Priority = 3, Mirrored = true },
        new RasterPartInfo { Path = Part("top"), Quantity = 1, Priority = 5 },
        new RasterPartInfo { Path = Part("lamelD"), Quantity = 6, Priority = 5 },
        new RasterPartInfo { Path = Part("lamelW"), Quantity = 6, Priority = 5 },
        new RasterPartInfo { Path = Part("sideD"), Quantity = 2, Priority = 1 },
        new RasterPartInfo { Path = Part("sideW"), Quantity = 2, Priority = 1 },
      };

      var result = SparrowNestService.Nest(
        parts,
        new[] { (1250, 1250, 1, false, 5) },
        rotations: 4,
        spacing: 5,
        margin: 5,
        perSheetBudgetSec: 5,
        SparrowExe.Resolve(),
        out string error,
        effort: nest ? NestEffort.Fast : NestEffort.Draft);

      result.Should().NotBeNull(error);
      var names = result.UsedSheets.SelectMany(s => s.PartPlacements).Select(p => Path.GetFileNameWithoutExtension(p.Part.Name)).ToList();
      foreach (var g in names.GroupBy(n => n))
      {
        this.output.WriteLine($"{g.Key}: {g.Count()}");
      }

      names.Count(n => n.Contains("sideD")).Should().Be(2, "both sides of priority 1 are cut");
      names.Count(n => n.Contains("sideW")).Should().Be(2, "both sides of priority 1 are cut");
      names.Count(n => n.Contains("vert")).Should().BeGreaterThan(0, "the room left goes to the next priority");
    }

    private static string FindData()
    {
      for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
      {
        string candidate = Path.Combine(d.FullName, "test-data", "priority");
        if (Directory.Exists(candidate))
        {
          return candidate;
        }
      }

      return null;
    }
  }
}
