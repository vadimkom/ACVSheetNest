namespace DeepNestSharp.CiTests
{
  using DeepNestLib.NestProject;
  using DeepNestSharp.Domain.Models;
  using DeepNestSharp.Domain.Services;
  using DeepNestSharp.Domain.ViewModels;
  using FluentAssertions;
  using Xunit;

  /// <summary>
  /// The part card is where the operator reads back what a part is set to. It has to describe what will
  /// actually happen, not what the file happens to say.
  /// </summary>
  public class NestingLabelCommonCuttingFixture
  {
    [Fact]
    public void APlainDxfDoesNotClaimAnyCommonLine()
    {
      var sut = new ObservableDetailLoadInfo(new DetailLoadInfo
      {
        Path = "a.dxf",
        CommonCutting = CommonCuttingMode.Unrestricted, // as a 1.1.7 project would bring it back
      });

      sut.NestingLabel.Should().NotContain(UiText.Get("Card_CommonLine"), "the saved legacy mode has not been confirmed for direct cutting");
    }

    [Theory]
    [InlineData(CommonCuttingMode.Unrestricted, "Card_CommonLine")]
    [InlineData(CommonCuttingMode.SamePart, "Card_CommonLineSame")]
    public void AConfirmedDirectPartSaysWhatItShares(CommonCuttingMode stored, string expectedKey)
    {
      var sut = new ObservableDetailLoadInfo(new DetailLoadInfo
      {
        Path = "a.dxf",
        CommonCutting = stored,
        DirectCommonCuttingEnabled = true,
      });

      sut.NestingLabel.Should().StartWith(UiText.Get(expectedKey) + " · ");
    }

    [Fact]
    public void AConfirmedDirectSvgStillReportsNoCommonLineThroughTheWrapper()
    {
      var sut = new ObservableDetailLoadInfo(new DetailLoadInfo
      {
        Path = "a.svg",
        CommonCutting = CommonCuttingMode.Unrestricted,
        DirectCommonCuttingEnabled = true,
      });

      sut.SupportsCommonCutting.Should().BeFalse();
      sut.EffectiveCommonCutting.Should().Be(CommonCuttingMode.None);
      sut.NestingLabel.Should().NotContain(UiText.Get("Card_CommonLine"));
    }

    [Fact]
    public void TheObservableBoolCompatibilityApiCanConfirmAndDisableALegacyDirectMode()
    {
      var sut = new ObservableDetailLoadInfo(new DetailLoadInfo
      {
        Path = "a.dxf",
        CommonCutting = CommonCuttingMode.Unrestricted,
      });

      sut.CommonLine = true;

      sut.DirectCommonCuttingEnabled.Should().BeTrue();
      sut.EffectiveCommonCutting.Should().Be(CommonCuttingMode.Unrestricted);
      sut.NestingLabel.Should().Contain(UiText.Get("Card_CommonLine"));

      sut.CommonLine = false;

      sut.DirectCommonCuttingEnabled.Should().BeFalse();
      sut.EffectiveCommonCutting.Should().Be(CommonCuttingMode.None);
      sut.NestingLabel.Should().NotContain(UiText.Get("Card_CommonLine"));
    }
  }
}
