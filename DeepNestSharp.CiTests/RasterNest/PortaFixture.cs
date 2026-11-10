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
  /// The shop's door panels (2026-11-10): eight strips (six 290 × 1915, two 225 × 1775) and eight panels
  /// 290 × 425, every one its own drawing, on two standing 1250 × 2500 sheets. By hand each sheet takes four
  /// strips side by side and four panels standing in a row above them; the program placed 13 of 16, leaving a
  /// band of 1250 × 570 empty on the first sheet. The drawings' outlines are plain rectangles (the decoration is
  /// cut inside them), so the test draws the rectangles itself.
  /// </summary>
  [Collection(SparrowEngineCollection.Name)]
  public class PortaFixture
  {
    private readonly ITestOutputHelper output;

    public PortaFixture(ITestOutputHelper output)
    {
      this.output = output;
    }

    [SparrowTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void SixteenPanelsGoOnTwoSheets(bool nest)
    {
      const int seeds = 3;
      string dir = Path.Combine(Path.GetTempPath(), "sheetnest-porta-" + Guid.NewGuid().ToString("N"));
      Directory.CreateDirectory(dir);
      try
      {
        var parts = new List<RasterPartInfo>();
        for (int i = 1; i <= 8; i++)
        {
          parts.Add(new RasterPartInfo { Path = WriteRectangle(dir, $"1.{i}", 290, 425), Quantity = 1, Priority = 5 });
        }

        for (int i = 1; i <= 8; i++)
        {
          parts.Add(new RasterPartInfo { Path = WriteRectangle(dir, $"2.{i}", i <= 2 ? 225 : 290, i <= 2 ? 1775 : 1915), Quantity = 1, Priority = 5 });
        }

        var failed = new List<string>();
        for (int seed = 0; seed < seeds; seed++)
        {
          var result = SparrowNestService.Nest(
            parts,
            new[] { (1250, 2500, 2, false, 5) },
            rotations: 4,
            spacing: 5,
            margin: 5,
            perSheetBudgetSec: 5,
            SparrowExe.Resolve(),
            out string error,
            effort: nest ? NestEffort.Fast : NestEffort.Draft,
            seedOffset: seed * 100);

          result.Should().NotBeNull(error);
          int placed = result.UsedSheets.Sum(s => s.PartPlacements.Count);
          this.output.WriteLine($"seed {seed}: {placed} placed on {result.UsedSheets.Count} sheets");
          foreach (var sheet in result.UsedSheets)
          {
            this.output.WriteLine("  sheet");
            foreach (var p in sheet.PartPlacements.OrderBy(p => p.MinX).ThenBy(p => p.MinY))
            {
              this.output.WriteLine($"    {Path.GetFileNameWithoutExtension(p.Part.Name)}: x {p.MinX:0.#}..{p.MaxX:0.#}, y {p.MinY:0.#}..{p.MaxY:0.#}");
            }
          }

          if (placed != 16)
          {
            failed.Add($"seed {seed}: {placed}");
          }
        }

        failed.Should().BeEmpty("four strips and four panels go on each sheet, as the operator lays them by hand");
      }
      finally
      {
        try
        {
          Directory.Delete(dir, true);
        }
        catch (IOException)
        {
        }
      }
    }

    private static string WriteRectangle(string dir, string name, double width, double height)
    {
      var file = new IxMilia.Dxf.DxfFile();
      var pts = new[]
      {
        new IxMilia.Dxf.DxfPoint(0, 0, 0), new IxMilia.Dxf.DxfPoint(width, 0, 0),
        new IxMilia.Dxf.DxfPoint(width, height, 0), new IxMilia.Dxf.DxfPoint(0, height, 0),
        new IxMilia.Dxf.DxfPoint(0, 0, 0),
      };
      file.Entities.Add(new IxMilia.Dxf.Entities.DxfPolyline(pts.Select(p => new IxMilia.Dxf.Entities.DxfVertex(p))) { IsClosed = true });
      string path = Path.Combine(dir, name + ".dxf");
      file.Save(path);
      return path;
    }
  }
}
