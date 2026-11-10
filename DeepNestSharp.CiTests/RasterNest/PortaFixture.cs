namespace DeepNestSharp.CiTests.RasterNest
{
  using System;
  using System.Collections.Generic;
  using System.IO;
  using System.Linq;
  using DeepNestLib;
  using DeepNestLib.Placement;
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
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void SixteenPanelsGoOnTwoSheets(bool nest, bool fromRight)
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
            seedOffset: seed * 100,
            fromRight: fromRight);

          result.Should().NotBeNull(error);

          // From the right the layout is the mirror image: read every box as the mirror sees it.
          double Left(IPartPlacement p) => fromRight ? 1250 - p.MaxX : p.MinX;
          double Right(IPartPlacement p) => fromRight ? 1250 - p.MinX : p.MaxX;
          int placed = result.UsedSheets.Sum(s => s.PartPlacements.Count);
          this.output.WriteLine($"seed {seed}: {placed} placed on {result.UsedSheets.Count} sheets");
          foreach (var sheet in result.UsedSheets)
          {
            this.output.WriteLine("  sheet");
            foreach (var p in sheet.PartPlacements.OrderBy(p => Left(p)).ThenBy(p => p.MinY))
            {
              this.output.WriteLine($"    {Path.GetFileNameWithoutExtension(p.Part.Name)}: x {p.MinX:0.#}..{p.MaxX:0.#}, y {p.MinY:0.#}..{p.MaxY:0.#}");
            }
          }

          if (placed != 16)
          {
            failed.Add($"seed {seed}: {placed}");
          }

          // The tall columns stand first, from the left, so the metal left over them is one staircase.
          foreach (var sheet in result.UsedSheets)
          {
            var tops = new List<(double MaxX, double Top)>();
            foreach (var p in sheet.PartPlacements.OrderBy(p => Left(p)))
            {
              if (tops.Count > 0 && Left(p) < tops[^1].MaxX + 5 - 1e-6)
              {
                tops[^1] = (Math.Max(tops[^1].MaxX, Right(p)), Math.Max(tops[^1].Top, p.MaxY));
              }
              else
              {
                tops.Add((Right(p), p.MaxY));
              }
            }

            if (sheet.PartPlacements.Min(p => Left(p)) > 5 + 0.5)
            {
              failed.Add($"seed {seed}: nothing against the {(fromRight ? "right" : "left")} margin");
            }

            this.output.WriteLine($"  column tops: {string.Join(", ", tops.Select(t => t.Top.ToString("0")))}");
            if (tops.Zip(tops.Skip(1), (a, b) => b.Top > a.Top + 0.5).Any(rises => rises))
            {
              failed.Add($"seed {seed}: a column taller than the one on its left");
            }
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

    /// <summary>
    /// From the right, a part that is not its own mirror image still comes out as drawn, only turned: an L
    /// mirrored for the engine and not mirrored back would be cut the wrong way round. Twelve Ls on a lying
    /// sheet: each outline is the drawing turned by its rotation, none overlaps another, all lie in the
    /// sheet, and they are packed against the right edge.
    /// </summary>
    [SparrowTheory]
    [InlineData(0)]
    [InlineData(1)]
    public void FromTheRightAnLComesOutAsDrawn(int seed)
    {
      string dir = Path.Combine(Path.GetTempPath(), "sheetnest-mirror-" + Guid.NewGuid().ToString("N"));
      Directory.CreateDirectory(dir);
      try
      {
        var l = new[] { (0.0, 0.0), (200.0, 0.0), (200.0, 50.0), (50.0, 50.0), (50.0, 150.0), (0.0, 150.0) };
        var parts = new List<RasterPartInfo> { new RasterPartInfo { Path = WritePolygon(dir, "L", l), Quantity = 12, Priority = 5 } };
        const int W = 1000;
        const int H = 500;
        var result = SparrowNestService.Nest(
          parts,
          new[] { (W, H, 1, false, 5) },
          rotations: 4,
          spacing: 5,
          margin: 5,
          perSheetBudgetSec: 5,
          SparrowExe.Resolve(),
          out string error,
          effort: NestEffort.Draft,
          seedOffset: seed * 100,
          fromRight: true);

        result.Should().NotBeNull(error);
        var sheet = result.UsedSheets.Should().ContainSingle().Subject;
        sheet.PartPlacements.Count.Should().Be(12);
        foreach (var p in sheet.PartPlacements)
        {
          double a = p.Rotation * Math.PI / 180;
          var expected = l.Select(q => ((q.Item1 * Math.Cos(a)) - (q.Item2 * Math.Sin(a)), (q.Item1 * Math.Sin(a)) + (q.Item2 * Math.Cos(a)))).ToList();
          double ex = expected.Min(q => q.Item1);
          double ey = expected.Min(q => q.Item2);
          var actual = p.Part.Points.Select(q => (q.X + p.X, q.Y + p.Y)).ToList();
          foreach (var q in expected)
          {
            actual.Should().Contain(
              c => Math.Abs(c.Item1 - (q.Item1 - ex + p.MinX)) < 0.01 && Math.Abs(c.Item2 - (q.Item2 - ey + p.MinY)) < 0.01,
              $"the L at {p.Rotation}° is the drawing turned, not mirrored");
          }

          p.MinX.Should().BeGreaterThanOrEqualTo(5 - 0.01);
          p.MaxX.Should().BeLessThanOrEqualTo(W - 5 + 0.01);
          p.MinY.Should().BeGreaterThanOrEqualTo(5 - 0.01);
          p.MaxY.Should().BeLessThanOrEqualTo(H - 5 + 0.01);
        }

        PlacementAcceptance.FindUnfit(sheet.PartPlacements, W, H, 5, (x, y) => 5 - 0.05).All.Should().BeEmpty();
        sheet.PartPlacements.Max(p => p.MaxX).Should().BeApproximately(W - 5, 1, "packed against the right edge");
        sheet.PartPlacements.Min(p => p.MinX).Should().BeGreaterThan(W / 3.0, "the offcut is at the left");
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
      => WritePolygon(dir, name, new[] { (0.0, 0.0), (width, 0.0), (width, height), (0.0, height) });

    private static string WritePolygon(string dir, string name, IEnumerable<(double X, double Y)> points)
    {
      var file = new IxMilia.Dxf.DxfFile();
      var pts = points.Select(p => new IxMilia.Dxf.DxfPoint(p.X, p.Y, 0)).ToList();
      pts.Add(pts[0]);
      file.Entities.Add(new IxMilia.Dxf.Entities.DxfPolyline(pts.Select(p => new IxMilia.Dxf.Entities.DxfVertex(p))) { IsClosed = true });
      string path = Path.Combine(dir, name + ".dxf");
      file.Save(path);
      return path;
    }
  }
}
