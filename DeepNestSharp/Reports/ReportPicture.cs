namespace DeepNestSharp.Reports
{
  using System;
  using System.Collections.Generic;
  using System.IO;
  using System.Linq;
  using System.Windows;
  using System.Windows.Media;
  using System.Windows.Media.Imaging;
  using DeepNestLib.NestProject;

  /// <summary>
  /// A picture the report prints after the layouts (the user, 2026-11-23: "pictures from files and from the
  /// clipboard that then go into the report"). Decoded once; the PDF gets it as a JPEG (DCTDecode), which needs
  /// no zlib, so it writes on the shop PC as the rest of the report does.
  /// </summary>
  public sealed class ReportPicture
  {
    /// <summary>The longer side a picture is kept at: plenty for a page of A4, a phone photo shrinks to a fifth.</summary>
    public const int MaxSide = 2400;

    private const int PngLimit = 1_500_000;

    private byte[] jpeg;
    private int jpegWidth;
    private int jpegHeight;

    private ReportPicture(BitmapSource bitmap, string caption)
    {
      this.Bitmap = bitmap;
      this.Caption = caption;
    }

    public BitmapSource Bitmap { get; }

    public string Caption { get; }

    public int PixelWidth => this.Bitmap.PixelWidth;

    public int PixelHeight => this.Bitmap.PixelHeight;

    /// <summary>The project's pictures that can be read, in their order.</summary>
    public static List<ReportPicture> FromProject(IEnumerable<ProjectPicture> pictures)
      => (pictures ?? Enumerable.Empty<ProjectPicture>()).Select(FromProject).Where(p => p != null).ToList();

    /// <summary>A project's picture, or null when its data is not an image.</summary>
    public static ReportPicture FromProject(ProjectPicture picture)
    {
      if (string.IsNullOrEmpty(picture?.Data))
      {
        return null;
      }

      try
      {
        return new ReportPicture(Decode(Convert.FromBase64String(picture.Data)), picture.Caption);
      }
      catch (Exception ex) when (ex is FormatException || ex is NotSupportedException || ex is IOException
        || ex is ArgumentException || ex is InvalidOperationException || ex is System.Runtime.InteropServices.COMException)
      {
        return null;
      }
    }

    /// <summary>A picture file for the project: turned upright as the camera meant it, no longer than
    /// <see cref="MaxSide"/>. Throws when the file is not an image.</summary>
    public static ProjectPicture Import(string path)
      => Import(File.ReadAllBytes(path), Path.GetFileNameWithoutExtension(path));

    /// <summary>An image's bytes (any format WIC reads) for the project. A JPEG already upright and small enough is
    /// kept as it is; anything else is written as PNG, or as JPEG when the PNG would be large (a photo).</summary>
    public static ProjectPicture Import(byte[] bytes, string caption)
    {
      BitmapFrame frame;
      using (var ms = new MemoryStream(bytes, writable: false))
      {
        var decoder = BitmapDecoder.Create(ms, BitmapCreateOptions.PreservePixelFormat | BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.OnLoad);
        frame = decoder.Frames[0];
      }

      int orientation = Orientation(frame);
      bool jpegAsIs = bytes.Length > 3 && bytes[0] == 0xFF && bytes[1] == 0xD8
        && orientation <= 1 && Math.Max(frame.PixelWidth, frame.PixelHeight) <= MaxSide;
      if (jpegAsIs)
      {
        return new ProjectPicture { Caption = caption, Data = Convert.ToBase64String(bytes) };
      }

      return Import(Upright(frame, orientation), caption);
    }

    /// <summary>A bitmap (from the clipboard) for the project.</summary>
    public static ProjectPicture Import(BitmapSource bitmap, string caption)
    {
      if (bitmap == null)
      {
        throw new ArgumentNullException(nameof(bitmap));
      }

      bitmap = Shrunk(bitmap);
      byte[] png = Encode(new PngBitmapEncoder(), bitmap);
      byte[] data = png.Length <= PngLimit ? png : EncodeJpeg(bitmap, 90).Bytes;
      return new ProjectPicture { Caption = caption, Data = Convert.ToBase64String(data) };
    }

    /// <summary>The picture for the PDF: a baseline JPEG in RGB, see-through parts on white.</summary>
    public (byte[] Bytes, int Width, int Height) PdfJpeg()
    {
      if (this.jpeg == null)
      {
        (this.jpeg, this.jpegWidth, this.jpegHeight) = EncodeJpeg(this.Bitmap, 92);
      }

      return (this.jpeg, this.jpegWidth, this.jpegHeight);
    }

    private static BitmapSource Decode(byte[] bytes)
    {
      var image = new BitmapImage();
      using (var ms = new MemoryStream(bytes, writable: false))
      {
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
        image.StreamSource = ms;
        image.EndInit();
      }

      image.Freeze();
      return image;
    }

    /// <summary>The EXIF orientation of a photo (1 = upright), 0 when the image has none.</summary>
    private static int Orientation(BitmapFrame frame)
    {
      try
      {
        if (frame.Metadata is BitmapMetadata meta && meta.ContainsQuery("/app1/ifd/{ushort=274}"))
        {
          return Convert.ToInt32(meta.GetQuery("/app1/ifd/{ushort=274}"), System.Globalization.CultureInfo.InvariantCulture);
        }
      }
      catch (Exception ex) when (ex is NotSupportedException || ex is InvalidOperationException || ex is ArgumentException
        || ex is FormatException || ex is InvalidCastException || ex is System.Runtime.InteropServices.COMException)
      {
      }

      return 0;
    }

    /// <summary>The frame turned (and mirrored) as its EXIF orientation says, so a phone photo stands as it was taken.</summary>
    private static BitmapSource Upright(BitmapSource frame, int orientation)
    {
      var t = new TransformGroup();
      switch (orientation)
      {
        case 2: t.Children.Add(new ScaleTransform(-1, 1)); break;
        case 3: t.Children.Add(new RotateTransform(180)); break;
        case 4: t.Children.Add(new ScaleTransform(1, -1)); break;
        case 5: t.Children.Add(new ScaleTransform(-1, 1)); t.Children.Add(new RotateTransform(270)); break;
        case 6: t.Children.Add(new RotateTransform(90)); break;
        case 7: t.Children.Add(new ScaleTransform(-1, 1)); t.Children.Add(new RotateTransform(90)); break;
        case 8: t.Children.Add(new RotateTransform(270)); break;
        default: return frame;
      }

      var turned = new TransformedBitmap(frame, t);
      turned.Freeze();
      return turned;
    }

    private static BitmapSource Shrunk(BitmapSource bitmap)
    {
      int side = Math.Max(bitmap.PixelWidth, bitmap.PixelHeight);
      if (side <= MaxSide)
      {
        return bitmap;
      }

      double k = (double)MaxSide / side;
      var small = new TransformedBitmap(bitmap, new ScaleTransform(k, k));
      small.Freeze();
      return small;
    }

    private static byte[] Encode(BitmapEncoder encoder, BitmapSource bitmap)
    {
      encoder.Frames.Add(BitmapFrame.Create(bitmap));
      using var ms = new MemoryStream();
      encoder.Save(ms);
      return ms.ToArray();
    }

    /// <summary>JPEG in 24-bit RGB with any transparency laid on white: what a PDF reader and a printer take as it is.</summary>
    private static (byte[] Bytes, int Width, int Height) EncodeJpeg(BitmapSource bitmap, int quality)
    {
      var bgra = new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
      int w = bgra.PixelWidth, h = bgra.PixelHeight;
      var src = new byte[w * h * 4];
      bgra.CopyPixels(src, w * 4, 0);

      int stride = ((w * 3) + 3) & ~3;
      var dst = new byte[stride * h];
      for (int y = 0; y < h; y++)
      {
        for (int x = 0; x < w; x++)
        {
          int s = ((y * w) + x) * 4, d = (y * stride) + (x * 3);
          int a = src[s + 3];
          dst[d] = (byte)(((src[s] * a) + (255 * (255 - a))) / 255);
          dst[d + 1] = (byte)(((src[s + 1] * a) + (255 * (255 - a))) / 255);
          dst[d + 2] = (byte)(((src[s + 2] * a) + (255 * (255 - a))) / 255);
        }
      }

      var rgb = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgr24, null, dst, stride);
      return (Encode(new JpegBitmapEncoder { QualityLevel = quality }, rgb), w, h);
    }
  }
}
