namespace DeepNestSharp.CiTests.RasterNest
{
  using DeepNestSharp.RasterNest;
  using FluentAssertions;
  using Xunit;

  /// <summary>
  /// How many sparrow searches run at once. Scheduling only: none of this may change a nest, so these
  /// tests pin how busy the machine is asked to be, not what comes out.
  /// </summary>
  public class NestConcurrencyFixture
  {
    [Theory]
    [InlineData(4, (int)NestEffort.Normal, 1)] // a 4-thread Celeron: one two-thread search, a core left over
    [InlineData(4, (int)NestEffort.Best, 1)]
    [InlineData(4, (int)NestEffort.Fast, 3)]
    [InlineData(2, (int)NestEffort.Normal, 1)] // never fewer than one
    [InlineData(1, (int)NestEffort.Fast, 1)]
    [InlineData(12, (int)NestEffort.Normal, 5)]
    [InlineData(12, (int)NestEffort.Fast, 8)] // capped at one wave
    [InlineData(64, (int)NestEffort.Best, 8)]
    public void AutoLeavesACoreFreeAndCountsEachSearchsThreads(int cores, int effort, int expected)
    {
      SparrowNestService.AutoConcurrency(cores, (NestEffort)effort).Should().Be(expected);
    }

    [Fact]
    public void TheOperatorsSettingWinsOverAuto()
    {
      SparrowNestService.ResolveConcurrency(null, 2, 12, NestEffort.Normal).Should().Be(2);
    }

    [Fact]
    public void ZeroMeansAuto()
    {
      SparrowNestService.ResolveConcurrency(null, 0, 4, NestEffort.Normal)
        .Should().Be(SparrowNestService.AutoConcurrency(4, NestEffort.Normal));
    }

    [Fact]
    public void ASettingAboveOneWaveIsCappedAtTheWave()
    {
      SparrowNestService.ResolveConcurrency(null, 99, 64, NestEffort.Fast)
        .Should().Be(SparrowNestService.MaxConcurrencyLimit);
    }

    [Fact]
    public void TheEnvironmentVariableStillWinsForTesting()
    {
      SparrowNestService.ResolveConcurrency("3", 1, 12, NestEffort.Normal).Should().Be(3);
      SparrowNestService.ResolveConcurrency("junk", 1, 12, NestEffort.Normal).Should().Be(1);
    }
  }
}
