namespace DeepNestSharp.CiTests
{
  using System;
  using System.Collections.Generic;
  using System.IO;
  using System.Linq;
  using System.Text;
  using System.Windows.Media;
  using System.Windows.Media.Imaging;
  using DeepNestLib;
  using DeepNestLib.NestProject;
  using DeepNestLib.Placement;
  using DeepNestSharp.Reports;
  using FluentAssertions;
  using Xunit;

  /// <summary>
  /// Pictures for the report (the user, 2026-11-23: "pictures from files and from the clipboard that then go
  /// into the report"): kept in the project, out of the undo history, each printed on its own page after the
  /// layouts, in the PDF as a JPEG (no zlib on the shop PC).
  /// </summary>
  [Collection(UiLanguageCollection.Name)]
  public sealed class ReportPicturesFixture
  {
    [Fact]
    public void APictureImportsShrunkAndRoundTripsThroughTheProject()
    {
      var picture = ReportPicture.Import(Png(3000, 1500, Colors.SteelBlue), "схема");
      var shown = ReportPicture.FromProject(picture);
      shown.PixelWidth.Should().Be(ReportPicture.MaxSide);
      shown.PixelHeight.Should().Be(ReportPicture.MaxSide / 2);

      var config = new SvgNestConfig();
      var project = new ProjectInfo(config) { Pictures = new List<ProjectPicture> { picture } };
      var loaded = ProjectInfo.FromJson(config, project.ToJson());
      loaded.Pictures.Should().ContainSingle();
      loaded.Pictures[0].Caption.Should().Be("схема");
      loaded.Pictures[0].Data.Should().Be(picture.Data);

      var copy = new ProjectInfo(config);
      copy.Load(loaded);
      copy.Pictures.Should().ContainSingle();

      new ProjectInfo(config).ToJson().Should().NotContain("Pictures", "a job without pictures saves as before");
    }

    [Fact]
    public void PicturesAreNotPartOfTheUndoHistory()
    {
      var project = new ProjectInfo(new SvgNestConfig())
      {
        Pictures = new List<ProjectPicture> { ReportPicture.Import(Png(20, 10, Colors.Red), null) },
      };
      ProjectHistory.Rows(project.ToJson()).Should().NotContain("Pictures");
    }

    [Fact]
    public void BadDataIsSkipped()
    {
      ReportPicture.FromProject(new ProjectPicture { Data = Convert.ToBase64String(Encoding.ASCII.GetBytes("not an image")) }).Should().BeNull();
      ReportPicture.FromProject(new ProjectPicture { Data = "@@@" }).Should().BeNull();
      ReportPicture.FromProject((ProjectPicture)null).Should().BeNull();
    }

    [Fact]
    public void EachPictureGetsItsOwnPageAfterTheLayouts()
    {
      var pictures = ReportPicture.FromProject(new[]
      {
        ReportPicture.Import(Png(400, 200, Colors.Green), "Сборка"),
        ReportPicture.Import(Png(100, 300, Colors.Blue), null),
      });

      var plain = new List<RecordingCanvas>();
      NestReportPdf.GenerateReport(() => Add(plain), Layout(), 0, "mm", null);
      var pages = new List<RecordingCanvas>();
      NestReportPdf.GenerateReport(() => Add(pages), Layout(), 0, "mm", null, pictures: pictures);

      pages.Should().HaveCount(plain.Count + 2);
      var first = pages[plain.Count];
      first.Texts.Should().Contain("Сборка");
      var image = first.Images.Single();
      image.W.Should().BeApproximately(2 * image.H, 1e-6, "the picture keeps its proportions");
      image.W.Should().BeLessThanOrEqualTo(400 * 1.5 + 1e-6, "a small picture is not blown up past 1.5 pt a pixel");

      var second = pages[plain.Count + 1];
      second.Texts.Should().Contain(DeepNestSharp.Ui.Localization.Format("Rep_PictureN", 2, 2), "an untitled picture is numbered");
      second.Images.Single().H.Should().BeApproximately(3 * second.Images.Single().W, 1e-6);
      pages.Take(plain.Count).SelectMany(p => p.Images).Should().BeEmpty();
    }

    [Fact]
    public void ThePdfCarriesThePictureAsAJpeg()
    {
      var pictures = ReportPicture.FromProject(new[] { ReportPicture.Import(Png(300, 200, Color.FromArgb(128, 255, 0, 0)), "half clear") });
      byte[] bytes = NestReportPdf.BuildPdfBytes(Layout(), 0, "mm", pictures: pictures);
      string pdf = Encoding.GetEncoding(28591).GetString(bytes);
      if (Environment.GetEnvironmentVariable("SHEETNEST_PICTURE_PDF") is string keep && keep.Length > 0)
      {
        File.WriteAllBytes(keep, bytes); // to look at in a viewer
      }

      pdf.Should().Contain("/Subtype /Image /Width 300 /Height 200");
      pdf.Should().Contain("/Filter /DCTDecode");
      pdf.Should().Contain("/XObject << /Im0 ");
      pdf.Should().Contain("/Im0 Do");
      pdf.Should().Contain("\xFF\xD8\xFF", "the stream is a JPEG");

      var (jpeg, w, h) = pictures[0].PdfJpeg();
      var decoded = BitmapDecoder.Create(new MemoryStream(jpeg), BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames[0];
      (decoded.PixelWidth, decoded.PixelHeight).Should().Be((w, h));
      var px = new byte[3];
      new FormatConvertedBitmap(decoded, PixelFormats.Bgr24, null, 0).CopyPixels(new System.Windows.Int32Rect(150, 100, 1, 1), px, 3, 0);
      px[2].Should().BeInRange(240, 255, "half-clear red lies on white: pink, not dark red");
      px[1].Should().BeInRange(110, 145);
    }

    [Fact]
    public void AnExifTurnedPhotoComesUpright()
    {
      // A phone photo stored lying, with orientation 6 (turn 90° clockwise to view).
      var source = BitmapSource.Create(40, 20, 96, 96, PixelFormats.Bgr24, null, new byte[40 * 20 * 3], 40 * 3);
      var metadata = new BitmapMetadata("jpg");
      metadata.SetQuery("/app1/ifd/{ushort=274}", (ushort)6);
      var encoder = new JpegBitmapEncoder();
      encoder.Frames.Add(BitmapFrame.Create(source, null, metadata, null));
      using var stream = new MemoryStream();
      encoder.Save(stream);

      var shown = ReportPicture.FromProject(ReportPicture.Import(stream.ToArray(), null));
      (shown.PixelWidth, shown.PixelHeight).Should().Be((20, 40));
    }

    private static RecordingCanvas Add(List<RecordingCanvas> pages)
    {
      var c = new RecordingCanvas();
      pages.Add(c);
      return c;
    }

    private static byte[] Png(int w, int h, Color colour)
    {
      var px = new byte[w * h * 4];
      for (int i = 0; i < px.Length; i += 4)
      {
        px[i] = colour.B;
        px[i + 1] = colour.G;
        px[i + 2] = colour.R;
        px[i + 3] = colour.A;
      }

      var bitmap = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, px, w * 4);
      var encoder = new PngBitmapEncoder();
      encoder.Frames.Add(BitmapFrame.Create(bitmap));
      using var stream = new MemoryStream();
      encoder.Save(stream);
      return stream.ToArray();
    }

    private static List<(ISheetPlacement Sheet, int Count, string Name)> Layout()
    {
      var part = new NoFitPolygon(new[] { (0.0, 0.0), (10.0, 0.0), (10.0, 10.0), (0.0, 10.0) }.Select(p => new SvgPoint(p.Item1, p.Item2)).ToList()) { Name = "plate.dxf" };
      var placements = new List<IPartPlacement> { new PartPlacement(part) { X = 0, Y = 0, Source = 0 } };
      var sheet = new SheetPlacement(PlacementTypeEnum.Gravity, Sheet.NewSheet(1, 200, 100), placements, 0, false);
      return new List<(ISheetPlacement Sheet, int Count, string Name)> { (sheet, 1, "A") };
    }

    private sealed class RecordingCanvas : IReportCanvas
    {
      public List<string> Texts { get; } = new();

      public List<(double X, double Y, double W, double H)> Images { get; } = new();

      public void SetFill(double r, double g, double b)
      {
      }

      public void FillRect(double x, double y, double w, double h, double r, double g, double b)
      {
      }

      public void Text(double x, double y, double size, bool bold, string text) => this.Texts.Add(text);

      public void Line(double x1, double y1, double x2, double y2, double width)
      {
      }

      public void Rect(double x, double y, double w, double h, double lineWidth)
      {
      }

      public void Polygon(IEnumerable<(double X, double Y)> pts, bool fill, bool holeFill = false, (byte R, byte G, byte B)? fillColour = null)
      {
      }

      public void Image(double x, double y, double w, double h, ReportPicture picture) => this.Images.Add((x, y, w, h));
    }
  }
}
