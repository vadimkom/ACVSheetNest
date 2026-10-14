namespace DeepNestLib.CiTests.NestProject
{
  using DeepNestLib.NestProject;
  using FluentAssertions;
  using Xunit;

  /// <summary>A job is cut from one material in one gauge; the project keeps both and the report weighs with them.</summary>
  public class SheetMaterialsFixture
  {
    [Fact]
    public void ASheetWeighsItsAreaTimesThicknessTimesDensity()
    {
      // 2500 x 1250 mm of 1.5 mm galvanised steel: 3.125 m² × 1.5 mm × 7.85 = 36.8 kg.
      SheetMaterials.Kilograms(2500.0 * 1250.0, SheetMaterials.Galvanized, 1.5).Should().BeApproximately(36.80, 0.01);
      SheetMaterials.Kilograms(1_000_000, SheetMaterials.Aluminium, 2).Should().BeApproximately(5.4, 1e-9);
      SheetMaterials.Kilograms(1_000_000, null, 2).Should().BeNull("no material, no weight");
      SheetMaterials.Kilograms(1_000_000, SheetMaterials.Black, 0).Should().BeNull("no thickness, no weight");
    }

    [Fact]
    public void TheShopsGaugesAreOffered()
    {
      SheetMaterials.StandardThicknesses.Should().Equal(0.6, 0.7, 0.8, 1.0, 1.2, 1.5, 2.0, 3.0, 4.0, 5.0);
      SheetMaterials.All.Should().HaveCount(4);
    }

    [Fact]
    public void AProjectKeepsItsMaterialAndThickness()
    {
      var config = SvgNest.Config;
      var project = new ProjectInfo(config) { Material = SheetMaterials.Stainless, Thickness = 1.2 };

      var reopened = ProjectInfo.FromJson(config, project.ToJson());

      reopened.Material.Should().Be(SheetMaterials.Stainless);
      reopened.Thickness.Should().Be(1.2);
    }

    [Fact]
    public void AProjectWithoutAMaterialWritesNothingAboutIt()
    {
      var config = SvgNest.Config;
      string json = new ProjectInfo(config).ToJson();

      json.Should().NotContain("\"Material\"").And.NotContain("\"Thickness\"");
      ProjectInfo.FromJson(config, json).Material.Should().BeNull();
    }
  }
}
