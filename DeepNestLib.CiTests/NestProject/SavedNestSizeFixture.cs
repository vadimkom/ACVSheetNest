namespace DeepNestLib.CiTests.NestProject
{
  using System.IO;
  using System.Linq;
  using System.Text.Json;
  using DeepNestLib.Placement;
  using FluentAssertions;
  using Xunit;

  /// <summary>
  /// A saved nest carries each part's geometry once. An operator's three-sheet job (a front with 1048 holes)
  /// saved at 4.4 MB, half of it a second copy of every part written for the exporter and never read back.
  /// </summary>
  public class SavedNestSizeFixture
  {
    private static string Project([System.Runtime.CompilerServices.CallerFilePath] string here = "")
      => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here), "..", "..", "test-data", "test1", "test1.dnest"));

    [Fact]
    public void ASavedNestIsHalfTheSizeAndReadsBackTheSame()
    {
      if (!File.Exists(Project()))
      {
        return;
      }

      using var doc = JsonDocument.Parse(File.ReadAllText(Project()));
      string before = doc.RootElement.GetProperty("LastNestResultJson").GetString();
      NestResult original = NestResult.FromJson(before);

      string after = original.ToJson(false);
      after.Length.Should().BeLessThan(before.Length / 2, "each part's geometry is written once, not twice");
      after.Should().NotContain("PolygonsForExport").And.NotContain("\"Hull\"");

      NestResult reread = NestResult.FromJson(after);
      reread.UsedSheets.Should().HaveCount(original.UsedSheets.Count);
      for (int s = 0; s < original.UsedSheets.Count; s++)
      {
        var a = original.UsedSheets[s];
        var b = reread.UsedSheets[s];
        b.Sheet.WidthCalculated.Should().Be(a.Sheet.WidthCalculated);
        b.Sheet.HeightCalculated.Should().Be(a.Sheet.HeightCalculated);
        b.PartPlacements.Should().HaveCount(a.PartPlacements.Count);
        for (int p = 0; p < a.PartPlacements.Count; p++)
        {
          var pa = a.PartPlacements[p];
          var pb = b.PartPlacements[p];
          pb.X.Should().Be(pa.X);
          pb.Y.Should().Be(pa.Y);
          pb.Rotation.Should().Be(pa.Rotation);
          pb.IsMirrored.Should().Be(pa.IsMirrored);
          pb.Part.Name.Should().Be(pa.Part.Name);
          pb.Part.Points.Length.Should().Be(pa.Part.Points.Length);
          pb.Part.Children.Count.Should().Be(pa.Part.Children.Count);
          pb.PlacedPart.MinX.Should().BeApproximately(pa.PlacedPart.MinX, 1e-6);
          pb.PlacedPart.MaxY.Should().BeApproximately(pa.PlacedPart.MaxY, 1e-6);
        }
      }
    }
  }
}
