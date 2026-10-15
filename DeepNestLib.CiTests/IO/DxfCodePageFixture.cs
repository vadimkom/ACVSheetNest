namespace DeepNestLib.CiTests.IO
{
  using System.IO;
  using System.Text;
  using DeepNestLib.IO;
  using FluentAssertions;
  using IxMilia.Dxf;
  using IxMilia.Dxf.Entities;
  using Xunit;

  /// <summary>
  /// Names on the layout reach SC2000 as letters, not as "\U+041F..." escapes, which it shows as they stand:
  /// a Bulgarian "Правоъгълник" came out as a row of codes.
  /// </summary>
  public class DxfCodePageFixture
  {
    private static byte[] Saved(string label)
    {
      var dxf = new DxfFile();
      dxf.Header.Version = DxfAcadVersion.R2000;
      dxf.Entities.Add(new DxfText(new DxfPoint(0, 0, 0), 5, label));
      using var stream = new MemoryStream();
      DxfExporter.SaveForCam(dxf, stream);
      return stream.ToArray();
    }

    [Theory]
    [InlineData("Правоъгълник 100x50")]
    [InlineData("Шайба широкая M8")]
    public void CyrillicIsWrittenInTheCyrillicCodePage(string label)
    {
      Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
      string text = Encoding.GetEncoding(1251).GetString(Saved(label));

      text.Should().Contain(label);
      text.Should().NotContain(@"\U+");
      text.Should().MatchRegex(@"\$DWGCODEPAGE\r?\n\s*3\r?\nANSI_1251");
    }

    [Fact]
    public void GermanIsWrittenInTheWesternCodePage()
    {
      Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
      string text = Encoding.GetEncoding(1252).GetString(Saved("Unterlegscheibe groß"));

      text.Should().Contain("Unterlegscheibe groß");
      text.Should().MatchRegex(@"\$DWGCODEPAGE\r?\n\s*3\r?\nANSI_1252");
    }

    [Fact]
    public void PlainAsciiKeepsTheLibrarysCodePage()
    {
      string text = Encoding.ASCII.GetString(Saved("Plate 100x50"));

      text.Should().Contain("Plate 100x50");
      text.Should().MatchRegex(@"\$DWGCODEPAGE\r?\n\s*3\r?\nANSI_1252");
    }
  }
}
