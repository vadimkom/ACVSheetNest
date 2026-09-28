namespace DeepNestSharp.CiTests.RasterNest
{
  using DeepNestSharp.RasterNest;
  using FluentAssertions;
  using Xunit;

  /// <summary>
  /// Fast stops chasing fractions of a millimetre (see <see cref="SparrowNestService.EngineSearchFlags"/>);
  /// Normal and Best run the engine as it comes, so a job nested at those efforts is nested as before.
  /// </summary>
  public class EngineSearchFlagsFixture
  {
    [Fact]
    public void FastTradesTheLastMillimetreForTime()
    {
      SparrowNestService.EngineSearchFlags(NestEffort.Fast).Should().Equal(
        "--shrink-step", "0.005", "--max-fails", "3", "--compress-share", "0", "--no-improve-limit", "50");
    }

    [Theory]
    [InlineData((int)NestEffort.Normal)]
    [InlineData((int)NestEffort.Best)]
    public void TheOtherEffortsKeepTheEnginesOwnSearch(int effort)
    {
      SparrowNestService.EngineSearchFlags((NestEffort)effort).Should().BeEmpty();
    }
  }
}
