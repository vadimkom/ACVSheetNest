namespace DeepNestSharp.CiTests
{
  using System.Linq;
  using DeepNestLib;
  using DeepNestLib.NestProject;
  using FluentAssertions;
  using Xunit;

  /// <summary>Ctrl+Z on the job (the user, 2026-11-17): each change goes back in turn, the settings and the
  /// saved nest are not part of it.</summary>
  public class ProjectHistoryFixture
  {
    [Fact]
    public void ChangesGoBackOneAtATime()
    {
      var config = new SvgNestConfig();
      var project = new ProjectInfo(config);
      project.DetailLoadInfos.Add(new DetailLoadInfo { Path = @"C:\a.dxf", Quantity = 2 });
      var history = new ProjectHistory(() => ProjectHistory.Rows(project.ToJson()));

      history.Note().Should().BeFalse("nothing changed yet");
      project.DetailLoadInfos[0].Quantity = 5;
      history.Note().Should().BeTrue();
      project.DetailLoadInfos.Add(new DetailLoadInfo { Path = @"C:\b.dxf" });
      project.SheetLoadInfos.Add(new SheetLoadInfo(1250, 2500, 3));
      project.Material = "galvanized";
      // Not noted yet: the undo notes it first, so it is what goes.

      void Restore(string json)
      {
        var source = ProjectInfo.FromJson(config, json);
        source.LastNestResultJson = project.LastNestResultJson;
        project.Load(source);
      }

      project.LastNestResultJson = "{\"kept\":1}";
      history.Undo(Restore).Should().BeTrue();
      project.DetailLoadInfos.Select(d => d.Quantity).Should().Equal(5);
      project.SheetLoadInfos.Should().BeEmpty();
      project.Material.Should().NotBe("galvanized");
      project.LastNestResultJson.Should().Be("{\"kept\":1}", "the nest is not undone");

      history.Undo(Restore).Should().BeTrue();
      project.DetailLoadInfos.Single().Quantity.Should().Be(2);
      history.CanUndo.Should().BeFalse();
      history.Undo(Restore).Should().BeFalse();
    }

    [Fact]
    public void JobSettingsAreNotPartOfIt()
    {
      var project = new ProjectInfo(new SvgNestConfig()) { LastNestResultJson = "{}" };
      project.ToJson().Should().Contain("\"Config\"");
      string rows = ProjectHistory.Rows(project.ToJson());
      rows.Should().NotContain("\"Config\"", "Advanced Settings have their own OK and Cancel");
      rows.Should().NotContain("LastNestResultJson", "an undo changes the job, not the layouts");
      rows.Should().Contain("DetailLoadInfos");
    }

    [Fact]
    public void ItKeepsTheLastFifty()
    {
      var project = new ProjectInfo(new SvgNestConfig());
      project.DetailLoadInfos.Add(new DetailLoadInfo { Path = @"C:\a.dxf" });
      var history = new ProjectHistory(() => ProjectHistory.Rows(project.ToJson()));
      for (int i = 2; i < 80; i++)
      {
        project.DetailLoadInfos[0].Quantity = i;
        history.Note();
      }

      history.Count.Should().Be(ProjectHistory.Depth);
    }
  }
}
