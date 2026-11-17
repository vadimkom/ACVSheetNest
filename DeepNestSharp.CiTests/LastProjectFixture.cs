namespace DeepNestSharp.CiTests
{
  using System;
  using System.IO;
  using DeepNestLib;
  using DeepNestLib.NestProject;
  using FluentAssertions;
  using Xunit;

  /// <summary>The last job is kept in _last.dnest (the user, 2026-11-17); an empty one does not push it out.</summary>
  [Collection("LastProject")]
  public class LastProjectFixture : IDisposable
  {
    private readonly string folder = Path.Combine(Path.GetTempPath(), "SheetNestLast", Guid.NewGuid().ToString("N"));
    private readonly string saved = LastProject.Folder;

    public LastProjectFixture() => LastProject.Folder = this.folder;

    public void Dispose()
    {
      LastProject.Folder = this.saved;
      try
      {
        Directory.Delete(this.folder, true);
      }
      catch (IOException)
      {
      }
    }

    [Fact]
    public void TheLastJobIsKeptAndAnEmptyOneDoesNotReplaceIt()
    {
      LastProject.TryGet(out _, out _).Should().BeFalse();

      var job = new ProjectInfo(new SvgNestConfig());
      job.DetailLoadInfos.Add(new DetailLoadInfo { Path = @"C:\jobs\side.dxf", Quantity = 3 });
      LastProject.Write(job.ToJson(), @"C:\jobs\cap.dnest").Should().BeTrue();

      LastProject.TryGet(out string original, out string at).Should().BeTrue();
      original.Should().Be(@"C:\jobs\cap.dnest");
      at.Should().NotBeEmpty();

      var empty = new ProjectInfo(new SvgNestConfig());
      LastProject.Write(empty.ToJson(), string.Empty).Should().BeFalse("closing an empty new job keeps the one before");

      var back = ProjectInfo.LoadFromFile(new SvgNestConfig(), LastProject.FilePath);
      back.DetailLoadInfos.Should().ContainSingle().Which.Quantity.Should().Be(3);

      var sheetsOnly = new ProjectInfo(new SvgNestConfig());
      sheetsOnly.SheetLoadInfos.Add(new SheetLoadInfo(1250, 2500, 1));
      LastProject.Write(sheetsOnly.ToJson(), null).Should().BeTrue("stock alone is a job begun");
      LastProject.TryGet(out original, out _).Should().BeTrue();
      original.Should().BeEmpty();
    }

    [Fact]
    public void ItsOwnFilesAreNeverRecentProjects()
    {
      LastProject.IsOwnFile(LastProject.FilePath).Should().BeTrue();
      LastProject.IsOwnFile(Path.Combine(this.folder, "autosave", "recovery-12.dnest")).Should().BeTrue();
      LastProject.IsOwnFile(@"C:\jobs\cap.dnest").Should().BeFalse();
    }
  }
}
