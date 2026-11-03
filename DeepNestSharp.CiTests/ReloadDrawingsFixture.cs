namespace DeepNestSharp.CiTests
{
  using System;
  using System.IO;
  using System.Threading.Tasks;
  using DeepNestLib.NestProject;
  using DeepNestSharp.Domain.Models;
  using FluentAssertions;
  using IxMilia.Dxf;
  using IxMilia.Dxf.Entities;
  using Xunit;

  /// <summary>A drawing changed in CAD is read again on "Reload drawings": the row forgets the shape it had.</summary>
  public class ReloadDrawingsFixture
  {
    [Fact]
    public async Task ARowReadsItsDrawingAgainAfterReload()
    {
      string path = Path.Combine(Path.GetTempPath(), "sheetnest_reload_" + Guid.NewGuid().ToString("N") + ".dxf");
      try
      {
        Write(path, 100, 50);
        var row = new ObservableDetailLoadInfo(new DetailLoadInfo { Path = path });
        (await row.LoadAsync()).WidthCalculated.Should().BeApproximately(100, 1e-6);

        Write(path, 300, 50);
        (await row.LoadAsync()).WidthCalculated.Should().BeApproximately(100, 1e-6, "the shape read once is kept until asked to reload");

        row.Reload();
        (await row.LoadAsync()).WidthCalculated.Should().BeApproximately(300, 1e-6, "reloaded, the row reads the drawing as it is now");
      }
      finally
      {
        File.Delete(path);
      }
    }

    private static void Write(string path, double w, double h)
    {
      var file = new DxfFile();
      file.Header.Version = DxfAcadVersion.R2007;
      file.Entities.Add(new DxfLwPolyline(new[]
      {
        new DxfLwPolylineVertex { X = 0, Y = 0 },
        new DxfLwPolylineVertex { X = w, Y = 0 },
        new DxfLwPolylineVertex { X = w, Y = h },
        new DxfLwPolylineVertex { X = 0, Y = h },
      })
      {
        IsClosed = true,
        Layer = "0",
      });
      file.Save(path, true);
    }
  }
}
