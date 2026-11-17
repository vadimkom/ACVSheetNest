namespace DeepNestSharp.CiTests
{
  using System;
  using System.IO;
  using System.Linq;
  using System.Text.Json.Nodes;
  using DeepNestLib;
  using DeepNestLib.NestProject;
  using FluentAssertions;
  using Xunit;

  /// <summary>
  /// A job copied to a flash drive opens with paths to where its drawings were. Each missing one is found by name
  /// in the project's folder and below; the saved nest is pointed at them too, since the DXF export reads them back.
  /// </summary>
  public class ProjectPathsFixture : IDisposable
  {
    private readonly string flash = Path.Combine(Path.GetTempPath(), "SheetNestFlash", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
      try
      {
        Directory.Delete(this.flash, true);
      }
      catch (IOException)
      {
      }
    }

    [Fact]
    public void DrawingsMissingAtTheirPathsAreFoundBesideTheProject()
    {
      string job = Path.Combine(this.flash, "Поръчки", "cap");
      Directory.CreateDirectory(Path.Combine(job, "parts"));
      Directory.CreateDirectory(Path.Combine(job, "old", "parts"));
      File.WriteAllText(Path.Combine(job, "parts", "side.dxf"), "0\nEOF\n");
      File.WriteAllText(Path.Combine(job, "old", "side.dxf"), "0\nEOF\n"); // same name, folders match less
      File.WriteAllText(Path.Combine(job, "offcut.dxf"), "0\nEOF\n");
      string kept = Path.Combine(this.flash, "kept.dxf");
      File.WriteAllText(kept, "0\nEOF\n");

      const string oldSide = @"D:\LaserWorks\cap\parts\side.dxf";
      var project = new JsonObject
      {
        ["DetailLoadInfos"] = new JsonArray(
          new JsonObject { ["Path"] = oldSide, ["Quantity"] = 2 },
          new JsonObject { ["Path"] = @"D:\LaserWorks\cap\parts\gone.dxf", ["Quantity"] = 1 },
          new JsonObject { ["Path"] = kept, ["Quantity"] = 1 }),
        ["SheetLoadInfos"] = new JsonArray(new JsonObject { ["Width"] = 800, ["Height"] = 600, ["Quantity"] = 1, ["ShapePath"] = @"C:\Users\x\Desktop\offcut.dxf" }),
        ["LastNestResultJson"] = new JsonObject { ["UsedSheets"] = new JsonArray(new JsonObject { ["Part"] = new JsonObject { ["Name"] = oldSide } }) }.ToJsonString(),
      };
      string file = Path.Combine(job, "cap.dnest");
      File.WriteAllText(file, project.ToJsonString());

      var opened = new ProjectInfo(new SvgNestConfig());
      opened.Load(new SvgNestConfig(), file);

      opened.DetailLoadInfos[0].Path.Should().Be(Path.Combine(job, "parts", "side.dxf"), "found below the project, the folder that matches the old path");
      opened.DetailLoadInfos[1].Path.Should().Be(@"D:\LaserWorks\cap\parts\gone.dxf", "nothing of that name is there: left as it was");
      opened.DetailLoadInfos[2].Path.Should().Be(kept, "a drawing still at its path stays");
      opened.SheetLoadInfos.Single().ShapePath.Should().Be(Path.Combine(job, "offcut.dxf"));
      JsonNode.Parse(opened.LastNestResultJson)["UsedSheets"][0]["Part"]["Name"].GetValue<string>()
        .Should().Be(Path.Combine(job, "parts", "side.dxf"), "the saved nest reads the drawing back for the DXF");
    }

    /// <summary>Saved with its drawings, a job carried to another PC opens there with every drawing found
    /// (the user, 2026-11-17: "save the project with its parts, to move it to another computer").</summary>
    [Fact]
    public void AProjectSavedWithItsDrawingsOpensWhereverTheFolderGoes()
    {
      string here = Path.Combine(this.flash, "office");
      Directory.CreateDirectory(Path.Combine(here, "a"));
      Directory.CreateDirectory(Path.Combine(here, "b"));
      string sideA = Path.Combine(here, "a", "side.dxf");
      string sideB = Path.Combine(here, "b", "side.dxf");
      string offcut = Path.Combine(here, "offcut.dxf");
      File.WriteAllText(sideA, "A");
      File.WriteAllText(sideB, "B");
      File.WriteAllText(offcut, "O");
      File.WriteAllText(sideA + DeepNestLib.IO.ShapeRecord.Suffix, "{}");

      var project = new ProjectInfo(new SvgNestConfig());
      project.DetailLoadInfos.Add(new DetailLoadInfo { Path = sideA, Quantity = 2 });
      project.DetailLoadInfos.Add(new DetailLoadInfo { Path = sideB, Quantity = 1 });
      project.DetailLoadInfos.Add(new DetailLoadInfo { Path = Path.Combine(here, "gone.dxf") });
      project.SheetLoadInfos.Add(new SheetLoadInfo(800, 600, 1) { ShapePath = offcut });
      project.LastNestResultJson = new JsonObject { ["Part"] = new JsonObject { ["Name"] = sideB } }.ToJsonString();

      string packed = Path.Combine(this.flash, "usb");
      var copy = ProjectInfo.FromJson(new SvgNestConfig(), project.ToJson());
      var (copied, missing) = ProjectPaths.Pack(copy, Path.Combine(packed, "cap files"));
      File.WriteAllText(Path.Combine(packed, "cap.dnest"), copy.ToJson());

      copied.Should().Be(3);
      missing.Should().Equal(Path.Combine(here, "gone.dxf"));
      project.DetailLoadInfos[0].Path.Should().Be(sideA, "the open project is not touched");

      // Another PC: the folder lands somewhere else and the office is gone.
      string there = Path.Combine(this.flash, "laser", "jobs");
      Directory.CreateDirectory(Path.GetDirectoryName(there));
      Directory.Move(packed, there);
      Directory.Delete(here, true);

      var opened = new ProjectInfo(new SvgNestConfig());
      opened.Load(new SvgNestConfig(), Path.Combine(there, "cap.dnest"));
      File.ReadAllText(opened.DetailLoadInfos[0].Path).Should().Be("A");
      File.ReadAllText(opened.DetailLoadInfos[1].Path).Should().Be("B", "two drawings of one name stay apart");
      File.Exists(opened.DetailLoadInfos[0].Path + DeepNestLib.IO.ShapeRecord.Suffix).Should().BeTrue();
      File.ReadAllText(opened.SheetLoadInfos.Single().ShapePath).Should().Be("O");
      JsonNode.Parse(opened.LastNestResultJson)["Part"]["Name"].GetValue<string>().Should().Be(opened.DetailLoadInfos[1].Path);
    }
  }
}
