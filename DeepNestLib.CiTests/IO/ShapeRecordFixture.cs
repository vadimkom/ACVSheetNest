namespace DeepNestLib.CiTests.IO
{
  using System.IO;
  using DeepNestLib.IO;
  using FluentAssertions;
  using Xunit;

  /// <summary>
  /// A shape keeps what it was made from beside its drawing, so double-clicking it opens the shape dialog as it
  /// was typed: the sheet for tape, the preset, the units.
  /// </summary>
  public class ShapeRecordFixture
  {
    private static string Drawing() => Path.Combine(Directory.CreateTempSubdirectory("sheetnest_record").FullName, "tape.dxf");

    [Fact]
    public void ARecordComesBackAsTyped()
    {
      var kind = PartShapes.PerforatedTape;
      var typed = new[] { 1250.0, 2500, 26, 8.2, 3.5, 25, 18, 12.5, 0.5, 400 };
      string drawing = Drawing();
      ShapeRecord.Of(kind, typed, kind.Presets[0], millimetres: true, forSheet: true).SaveFor(drawing);

      var back = ShapeRecord.LoadFor(drawing);
      back.Should().NotBeNull();
      back.Kind.Should().BeSameAs(kind);
      back.ForSheet.Should().BeTrue();
      back.Preset.Should().Be(kind.Presets[0].Detail);
      back.ValuesIn(millimetres: true).Should().Equal(typed);
      back.ValuesIn(millimetres: false)[0].Should().BeApproximately(1250 / 25.4, 1e-9);
      File.ReadAllText(ShapeRecord.PathFor(drawing)).Should().NotContain("Parameters", "the kind is named, not copied");
    }

    [Fact]
    public void AnOrdinaryPartHasNoRecord()
    {
      ShapeRecord.LoadFor(Drawing()).Should().BeNull();
      ShapeRecord.LoadFor(null).Should().BeNull();
    }

    [Fact]
    public void ARecordOfAShapeNoLongerKnownIsNoRecord()
    {
      string drawing = Drawing();
      File.WriteAllText(ShapeRecord.PathFor(drawing), "{\"Shape\":\"gone\",\"Values\":{}}");
      ShapeRecord.LoadFor(drawing).Should().BeNull();
    }
  }
}
