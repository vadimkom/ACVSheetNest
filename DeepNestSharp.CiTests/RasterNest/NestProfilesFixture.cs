namespace DeepNestSharp.CiTests.RasterNest
{
  using System.IO;
  using DeepNestSharp.RasterNest;
  using FluentAssertions;
  using Xunit;

  /// <summary>
  /// Two buttons: Draft for a quote and Nest for the cut, each driven by a profile the operator can edit in a
  /// file. Nest is what Fast was, number for number.
  /// </summary>
  public class NestProfilesFixture
  {
    [Fact]
    public void TheNestButtonIsWhatFastWas()
    {
      var p = NestProfiles.NormalDefaults();
      p.Iterations.Should().Be(300);
      p.Tries.Should().Be(2);
      p.LastSheetTries.Should().Be(4);
      p.WorkersPerSearch.Should().Be(1);
      p.LastChanceIterations.Should().Be(600, "Fast's last chance searched at Normal's budget");
      p.EngineFlags.Should().Equal("--shrink-step", "0.005", "--max-fails", "3", "--compress-share", "0", "--no-improve-limit", "50");
    }

    [Fact]
    public void TheDraftSearchesFarLessThanTheNest()
    {
      var draft = NestProfiles.DraftDefaults();
      var nest = NestProfiles.NormalDefaults();
      draft.Iterations.Should().BeLessThan(nest.Iterations);
      draft.Tries.Should().Be(1);
      draft.LastChanceIterations.Should().Be(0, "a quote does not pay for the harder last pack");
      SparrowNestService.IterationBudget(NestEffort.Draft).Should().Be(NestProfiles.Draft.Iterations);
    }

    [Fact]
    public void AFirstReadWritesTheDefaultsForTheOperatorToEdit()
    {
      string dir = Directory.CreateTempSubdirectory("sheetnest_profiles").FullName;
      var (draft, normal, error) = NestProfiles.Read(dir);

      error.Should().BeNull();
      File.Exists(Path.Combine(dir, NestProfiles.FileName)).Should().BeTrue();
      draft.Iterations.Should().Be(NestProfiles.DraftDefaults().Iterations);
      normal.Iterations.Should().Be(NestProfiles.NormalDefaults().Iterations);
    }

    [Fact]
    public void AnEditedValueIsTakenAndANonsensicalOneFallsBack()
    {
      string dir = Directory.CreateTempSubdirectory("sheetnest_profiles").FullName;
      File.WriteAllText(
        Path.Combine(dir, NestProfiles.FileName),
        "{ // a comment is fine\n \"Draft\": { \"Iterations\": 50, \"Tries\": 0 },\n \"Normal\": { \"Iterations\": 400, \"FillUp\": false, },\n}");

      var (draft, normal, error) = NestProfiles.Read(dir);

      error.Should().BeNull();
      draft.Iterations.Should().Be(50);
      draft.Tries.Should().Be(NestProfiles.DraftDefaults().Tries, "zero tries would stall the engine");
      normal.Iterations.Should().Be(400);
      normal.FillUp.Should().BeFalse();
      normal.EngineFlags.Should().Equal(NestProfiles.NormalDefaults().EngineFlags, "a value left out keeps its default");
    }

    [Fact]
    public void AFileThatDoesNotReadLeavesTheDefaultsAndSaysWhy()
    {
      string dir = Directory.CreateTempSubdirectory("sheetnest_profiles").FullName;
      File.WriteAllText(Path.Combine(dir, NestProfiles.FileName), "{ \"Draft\": { \"Iterations\": ");

      var (draft, _, error) = NestProfiles.Read(dir);

      error.Should().NotBeNullOrEmpty();
      draft.Iterations.Should().Be(NestProfiles.DraftDefaults().Iterations);
    }
  }
}
