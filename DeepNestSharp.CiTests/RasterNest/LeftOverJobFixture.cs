namespace DeepNestSharp.CiTests.RasterNest
{
  using System;
  using System.Collections.Generic;
  using System.IO;
  using System.Linq;
  using System.Text.Json.Nodes;
  using DeepNestLib;
  using DeepNestLib.NestProject;
  using DeepNestSharp.RasterNest;
  using FluentAssertions;
  using Xunit;
  using Xunit.Abstractions;

  /// <summary>
  /// When the stock runs out, what is left goes on virtual 1250 x 2500 sheets (how many to buy) and can be saved
  /// as a job of its own, to nest when the material comes: the cap of 125 % of the one sheet in stock.
  /// </summary>
  [Collection(SparrowEngineCollection.Name)]
  public class LeftOverJobFixture
  {
    private readonly ITestOutputHelper output;

    public LeftOverJobFixture(ITestOutputHelper output)
    {
      this.output = output;
    }

    [SparrowTheory]
    [InlineData(1250)]
    public void WhatTheStockRanShortOfGoesOnVirtualSheetsAndIntoAJobForLater(int stockSide)
    {
      string data = FindData();
      string Part(string name) => Path.Combine(data, $"Flat-Pattern - {name}^shapka.pagoda.DXF");
      var populations = new List<RasterPartInfo>
      {
        new RasterPartInfo { Path = Part("vert"), Quantity = 2, Priority = 3 },
        new RasterPartInfo { Path = Part("vert"), Quantity = 2, Priority = 3, Mirrored = true },
        new RasterPartInfo { Path = Part("top"), Quantity = 1, Priority = 5 },
        new RasterPartInfo { Path = Part("lamelD"), Quantity = 6, Priority = 5 },
        new RasterPartInfo { Path = Part("lamelW"), Quantity = 6, Priority = 5 },
        new RasterPartInfo { Path = Part("sideD"), Quantity = 2, Priority = 1 },
        new RasterPartInfo { Path = Part("sideW"), Quantity = 2, Priority = 1 },
      };

      var nest = SparrowNestService.Nest(populations, new[] { (stockSide, stockSide, 1, false, 5) }, 4, 5, 5, 5, SparrowExe.Resolve(), out string error, effort: NestEffort.Draft);
      nest.Should().NotBeNull(error);
      var owed = LeftOverJob.Owed(populations, nest);
      owed.Sum(p => p.Quantity).Should().Be(nest.UnplacedParts.Count, "what is owed is exactly what the nest left");
      populations.Sum(p => p.Quantity).Should().Be(21, "the populations are not changed");

      var virtualNest = SparrowNestService.Nest(owed, new[] { (1250, 2500, 1, true, 5) }, 4, 5, 5, 5, SparrowExe.Resolve(), out error, effort: NestEffort.Draft);
      virtualNest.Should().NotBeNull(error);
      virtualNest.UnplacedParts.Should().BeEmpty();
      this.output.WriteLine($"left over {owed.Sum(p => p.Quantity)} on {virtualNest.UsedSheets.Count} virtual sheets");
      virtualNest.UsedSheets.Count.Should().Be(1);

      // The job for later: the same rows cut down to what is owed, the virtual sheets as its stock, no nest.
      var project = new JsonObject
      {
        ["DetailLoadInfos"] = new JsonArray(
          Row(Part("vert"), 2, 0, 2),
          Row(Part("top"), 1, 0, 0),
          Row(Part("lamelD"), 5, 1, 0),
          Row(Part("lamelW"), 6, 0, 0),
          Row(Part("sideD"), 2, 0, 0),
          Row(Part("sideW"), 2, 0, 0),
          Row(Part("washer"), 1, 0, 0, fill: true)),
        ["SheetLoadInfos"] = new JsonArray(new JsonObject { ["Width"] = 1250, ["Height"] = 1250, ["Quantity"] = 1 }),
        ["LastNestResultJson"] = "{}",
      };
      string json = LeftOverJob.ProjectJson(project.ToJsonString(), owed, virtualNest.UsedSheets.Count, 1250, 2500);
      var later = JsonNode.Parse(json).AsObject();
      later.ContainsKey("LastNestResultJson").Should().BeFalse();
      later["SheetLoadInfos"].AsArray().Single()["Height"].GetValue<int>().Should().Be(2500);
      var rows = later["DetailLoadInfos"].AsArray().Select(r => r.AsObject()).ToList();
      rows.Should().NotContain(r => r["Path"].GetValue<string>().Contains("washer"), "parts that fill the sheets are not owed");
      rows.Sum(r => r["Quantity"].GetValue<int>() + r["MirrorQuantity"].GetValue<int>()).Should().Be(owed.Sum(p => p.Quantity));
      rows.Should().OnlyContain(r => r["Extra"].GetValue<int>() == 0);
      rows.Should().NotContain(r => r["Path"].GetValue<string>().Contains("side"), "the sides of priority 1 were all cut");

      var reopened = ProjectInfo.FromJson(new SvgNestConfig(), json);
      reopened.DetailLoadInfos.Count.Should().Be(rows.Count);
      reopened.SheetLoadInfos.Single().Quantity.Should().Be(virtualNest.UsedSheets.Count);
    }

    private static JsonObject Row(string path, int quantity, int extra, int mirrored, bool fill = false)
      => new JsonObject
      {
        ["Path"] = path,
        ["Quantity"] = quantity,
        ["Extra"] = extra,
        ["MirrorQuantity"] = mirrored,
        ["FillSheets"] = fill,
        ["IsIncluded"] = true,
      };

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
