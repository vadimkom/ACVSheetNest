namespace DeepNestSharp.CiTests
{
  using DeepNestSharp.Reports;
  using FluentAssertions;
  using Xunit;

  /// <summary>The report's cut length: metres for a millimetre job, where "44,155 mm" looked like a typo.</summary>
  [Collection(UiLanguageCollection.Name)]
  public class ReportLengthFixture
  {
    [Theory]
    [InlineData(44155.0, "44.16")]
    [InlineData(850.0, "0.85")]
    public void ALaserPathInMillimetresIsShownInMetres(double mm, string metres)
    {
      using (new UiLanguageScope(DeepNestSharp.Ui.Localization.LangEnglish))
      {
        NestReportPdf.FormatLength(mm, "mm").Should().Be(metres + " m");
      }
    }

    [Fact]
    public void ALaserPathInInchesStaysInInches()
    {
      NestReportPdf.FormatLength(1250, "in").Should().StartWith("1250", "no thousands separator: the shop does not use one");
    }
  }
}
