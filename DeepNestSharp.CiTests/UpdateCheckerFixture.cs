namespace DeepNestSharp.CiTests
{
  using System;
  using DeepNestSharp.Domain;
  using FluentAssertions;
  using Xunit;

  public class UpdateCheckerFixture
  {
    [Fact]
    public void TryParseLatest_ReadsTagAndUrl()
    {
      var json = "{\"tag_name\":\"v1.1.4\",\"html_url\":\"https://github.com/ManuelMRosa/SheetNest/releases/tag/v1.1.4\",\"name\":\"SheetNest 1.1.4\"}";

      var result = UpdateChecker.TryParseLatest(json);

      result.Should().NotBeNull();
      result.Value.Latest.Should().Be(new Version(1, 1, 4));
      result.Value.Url.Should().Be("https://github.com/ManuelMRosa/SheetNest/releases/tag/v1.1.4");
    }

    [Fact]
    public void TryParseLatest_FallsBackToReleasesPageWhenNoUrl()
    {
      var result = UpdateChecker.TryParseLatest("{\"tag_name\":\"1.2.0\"}");

      result.Should().NotBeNull();
      result.Value.Latest.Should().Be(new Version(1, 2, 0));
      result.Value.Url.Should().Be(UpdateChecker.ReleasesPageUrl);
    }

    [Theory]
    [InlineData("not json at all")]
    [InlineData("{}")]
    [InlineData("{\"tag_name\":\"beta\"}")]
    public void TryParseLatest_ReturnsNullOnGarbage(string json)
    {
      UpdateChecker.TryParseLatest(json).Should().BeNull();
    }

    [Fact]
    public void TryParseLatest_TakesTheInstallerFromOurReleasesOnly()
    {
      var json = "{\"tag_name\":\"v1.1.58\",\"assets\":["
        + "{\"browser_download_url\":\"https://example.com/SheetNest-1.1.58-win-x64.msi\"},"
        + "{\"browser_download_url\":\"https://github.com/vadimkom/ACVSheetNest/releases/download/v1.1.58/notes.txt\"},"
        + "{\"browser_download_url\":\"https://github.com/vadimkom/ACVSheetNest/releases/download/v1.1.58/SheetNest-1.1.58-win-x64.msi\"}]}";

      var result = UpdateChecker.TryParseLatest(json);

      result.Value.MsiUrl.Should().Be("https://github.com/vadimkom/ACVSheetNest/releases/download/v1.1.58/SheetNest-1.1.58-win-x64.msi");
    }

    [Fact]
    public void TryParseLatest_NoInstallerIsNoInstaller()
    {
      UpdateChecker.TryParseLatest("{\"tag_name\":\"v1.1.58\",\"assets\":[]}").Value.MsiUrl.Should().BeNull();
    }

    [Fact]
    public async System.Threading.Tasks.Task DownloadAsync_RefusesAnythingButOurReleases()
    {
      System.Func<System.Threading.Tasks.Task> act = () => UpdateChecker.DownloadAsync("https://example.com/SheetNest.msi", null, default);
      await act.Should().ThrowAsync<System.InvalidOperationException>();
    }

    [Fact]
    public void TheInstallScriptWaitsInstallsAndStartsAgain()
    {
      string folder = System.IO.Directory.CreateTempSubdirectory("sheetnest_update").FullName;
      string msi = System.IO.Path.Combine(folder, "SheetNest-1.1.58-win-x64.msi");

      string script = UpdateChecker.WriteInstallScript(msi, 4242);
      string text = System.IO.File.ReadAllText(script);

      text.Should().Contain("PID eq 4242", "it waits for this copy to close");
      text.Should().Contain($"msiexec /i \"{msi}\" /passive /norestart");
      text.Should().Contain("SheetNest.exe", "and starts the program again");
      text.IndexOf("PID eq", System.StringComparison.Ordinal).Should().BeLessThan(text.IndexOf("msiexec", System.StringComparison.Ordinal));
    }

    [Fact]
    public void IsNewer_ComparesThreeFields()
    {
      UpdateChecker.IsNewer(new Version(1, 2, 0), new Version(1, 1, 4)).Should().BeTrue();
      UpdateChecker.IsNewer(new Version(1, 1, 4), new Version(1, 1, 4)).Should().BeFalse();
      UpdateChecker.IsNewer(new Version(1, 1, 3), new Version(1, 1, 4)).Should().BeFalse();
      UpdateChecker.IsNewer(new Version(1, 1, 4, 9), new Version(1, 1, 4)).Should().BeFalse("the 4th field is ignored");
    }
  }
}
