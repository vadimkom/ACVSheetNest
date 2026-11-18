namespace DeepNestSharp.CiTests.RasterNest
{
  using DeepNestSharp.RasterNest;
  using FluentAssertions;
  using Xunit;

  /// <summary>
  /// The watchdog for engines that are not ours (2026-11-17: a third-party engine ran 15 minutes on a test job
  /// and held the whole nest). Sparrow and RNest keep their own long watchdog and no leash, so their results
  /// do not depend on the machine's speed.
  /// </summary>
  public class EngineLeashFixture
  {
    [Theory]
    [InlineData(@"C:\Program Files\SheetNest\sparrow.exe")]
    [InlineData(@"C:\Program Files\SheetNest\rnest-sparrow.exe")]
    [InlineData(@"C:\Program Files\SheetNest\SPARROW.EXE")]
    public void OurEnginesAreNotLeashed(string exe)
    {
      SparrowNestService.ThirdPartyLeashSeconds(exe).Should().Be(int.MaxValue);
    }

    [Theory]
    [InlineData(@"C:\engines\dsh.exe")]
    [InlineData(@"C:\engines\qwen4-max-preview.exe")]
    [InlineData(@"C:\engines\sparrow-copy.exe")]
    public void AnyOtherEngineGetsTwoMinutes(string exe)
    {
      SparrowNestService.ThirdPartyLeashSeconds(exe).Should().Be(SparrowNestService.ThirdPartyEngineLeashSeconds);
      SparrowNestService.ThirdPartyEngineLeashSeconds.Should().Be(120);
    }

    [Fact]
    public void TheLeashIsThreeTimesTheFastestTryAndAtLeastAMinute()
    {
      var leash = new SparrowNestService.WaveLeash();
      leash.LimitSeconds.Should().Be(double.PositiveInfinity, "no try has answered yet");

      leash.Finished(10);
      leash.LimitSeconds.Should().Be(60, "3 x 10 s is under the minute");

      var slow = new SparrowNestService.WaveLeash();
      slow.Finished(30);
      slow.LimitSeconds.Should().BeApproximately(90, 1e-9);
      slow.Finished(40);
      slow.LimitSeconds.Should().BeApproximately(90, 1e-9, "the fastest try sets it");
      slow.Finished(25);
      slow.LimitSeconds.Should().BeApproximately(75, 1e-9);
    }
  }
}
