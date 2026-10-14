namespace DeepNestLib.CiTests.IO
{
  using System;
  using System.IO;
  using System.Linq;
  using DeepNestLib.IO;
  using FluentAssertions;
  using Xunit;

  /// <summary>
  /// The parts library is a folder: groups are subfolders, drawings are parts, patterns are shapes drawn to
  /// the offcut they fill. Perforated tape is kept as a pattern and drawn to whatever piece it is cut from.
  /// </summary>
  public class PartLibraryFixture
  {
    private static string Text(string key) => key switch
    {
      "Library_TapeGroup" => "Tape",
      "Shape_Tape" => "Tape",
      _ => key,
    };

    private static string NewFolder() => Path.Combine(Path.GetTempPath(), "sheetnest_library_" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void ANewLibraryStartsWithTheShopsTapesAsPatterns()
    {
      string folder = NewFolder();
      PartLibrary.CreateIfMissing(folder, Text);

      var groups = PartLibrary.Groups(folder, "Loose");
      var tape = groups.Should().ContainSingle().Subject;
      tape.Name.Should().Be("Tape");
      tape.Items.Should().HaveCount(2).And.OnlyContain(i => i.Pattern != null);
      tape.Items.Select(i => i.Name).Should().Contain("Tape B25, Ø8 + 4×Ø3.2");
    }

    [Fact]
    public void AnExistingFolderIsLeftAsItIs()
    {
      string folder = NewFolder();
      Directory.CreateDirectory(folder);
      PartLibrary.CreateIfMissing(folder, Text);
      PartLibrary.Groups(folder, "Loose").Should().BeEmpty();
    }

    [Fact]
    public void GroupsAreSubfoldersAndLooseDrawingsAGroupOfTheirOwn()
    {
      string folder = NewFolder();
      string brackets = Path.Combine(folder, "Brackets");
      Directory.CreateDirectory(brackets);
      PartShapes.Write(brackets, "Gusset 100x100", PartShapes.Rectangle, new double[] { 100, 100 }, millimetres: true);
      PartShapes.Write(folder, "Plate", PartShapes.Rectangle, new double[] { 200, 50 }, millimetres: true);
      File.WriteAllText(Path.Combine(brackets, "notes.txt"), "not a part");
      File.WriteAllText(Path.Combine(brackets, "broken" + ShapePattern.Extension), "{ not json");
      Directory.CreateDirectory(Path.Combine(folder, "Empty"));

      var groups = PartLibrary.Groups(folder, "Loose");
      groups.Select(g => g.Name).Should().Equal("Loose", "Brackets");
      groups[0].Items.Single().Name.Should().Be("Plate");
      groups[1].Items.Single().Name.Should().Be("Gusset 100x100", "a pattern that does not read is left out, the rest still offered");
    }

    [Fact]
    public void ATapePatternIsDrawnToThePieceItFills()
    {
      var tape = PartShapes.PerforatedTape;
      var values = tape.Parameters.Select(p => p.DefaultMm).ToArray();
      var pattern = ShapePattern.Of(tape, values, millimetres: true);
      pattern.ValuesMm.Should().NotContainKey("Shape_Length").And.NotContainKey("Shape_BlankWidth");

      string folder = NewFolder();
      string path = PartLibrary.SavePattern(folder, "My tape", pattern);
      var read = ShapePattern.Load(path);

      var drawn = read.Realize(1240, 240, millimetres: true);
      drawn[PerforatedTapeShape.Length].Should().Be(1240);
      drawn[PerforatedTapeShape.Width].Should().Be(240);
      drawn[PerforatedTapeShape.Strip].Should().Be(25);
      PerforatedTapeShape.Strips(drawn).Should().Be(9);
      tape.Problem(drawn).Should().BeNull();

      var inches = read.Realize(48, 10, millimetres: false);
      inches[PerforatedTapeShape.Strip].Should().BeApproximately(25 / 25.4, 1e-9, "a pattern is kept in millimetres");
    }

    [Fact]
    public void OnlyShapesDrawnToThePieceArePatterns()
    {
      PartShapes.Washer.Fit.Should().BeNull();
      new ShapePattern { Shape = PartShapes.Washer.Id }.Realize(100, 100, true).Should().BeNull();
      new ShapePattern { Shape = "no-such-shape" }.Realize(100, 100, true).Should().BeNull();
    }

    [Fact]
    public void ATapePatternIsNamedByItsSection()
    {
      var tape = PartShapes.PerforatedTape;
      var values = tape.Parameters.Select(p => p.DefaultMm).ToArray();
      values[PerforatedTapeShape.Strip] = 30;
      tape.PatternName(values, Text).Should().Be("Tape B30, Ø8 + 4×Ø3.2");
    }
  }
}
