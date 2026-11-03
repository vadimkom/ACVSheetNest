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
  }
}
