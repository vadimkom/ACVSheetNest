namespace DeepNestSharp.Reports
{
  using System;
  using System.Collections.Generic;
  using System.Globalization;
  using System.IO;
  using System.IO.Compression;
  using System.Linq;
  using System.Text;

  /// <summary>
  /// A TrueType font read just far enough to embed it in a PDF as a Unicode (Identity-H) font: the glyph
  /// for each character, how wide each glyph is, and the numbers a PDF font descriptor asks for. The
  /// standard Helvetica the report used before only covers Latin-1, so every Cyrillic part name and label
  /// came out as question marks.
  /// </summary>
  internal sealed class PdfTrueTypeFont
  {
    private readonly Dictionary<int, ushort> glyphByChar = new Dictionary<int, ushort>();
    private readonly ushort[] advance;
    private readonly Dictionary<ushort, int> usedGlyphs = new Dictionary<ushort, int>();

    private PdfTrueTypeFont(string postScriptName, byte[] file)
    {
      this.PostScriptName = postScriptName;
      this.File = file;

      var tables = ReadTableDirectory(file);
      int head = tables["head"].Offset;
      this.UnitsPerEm = U16(file, head + 18);
      this.BBox = (S16(file, head + 36), S16(file, head + 38), S16(file, head + 40), S16(file, head + 42));

      int hhea = tables["hhea"].Offset;
      this.Ascent = S16(file, hhea + 4);
      this.Descent = S16(file, hhea + 6);
      int metrics = U16(file, hhea + 34);

      int numGlyphs = U16(file, tables["maxp"].Offset + 4);
      int hmtx = tables["hmtx"].Offset;
      this.advance = new ushort[numGlyphs];
      for (int g = 0; g < numGlyphs; g++)
      {
        this.advance[g] = (ushort)U16(file, hmtx + (4 * Math.Min(g, metrics - 1)));
      }

      this.CapHeight = (int)(this.UnitsPerEm * 0.716);
      if (tables.TryGetValue("OS/2", out var os2) && U16(file, os2.Offset) >= 2 && os2.Length >= 90)
      {
        this.CapHeight = S16(file, os2.Offset + 88);
      }

      ReadCmap(file, tables["cmap"].Offset, this.glyphByChar);
    }

    public string PostScriptName { get; }

    public byte[] File { get; }

    public int UnitsPerEm { get; }

    public (int XMin, int YMin, int XMax, int YMax) BBox { get; }

    public int Ascent { get; }

    public int Descent { get; }

    public int CapHeight { get; }

    /// <summary>Arial and Arial Bold from the Windows fonts folder, or null for each that cannot be used.</summary>
    public static (PdfTrueTypeFont Regular, PdfTrueTypeFont Bold) LoadSystemArial()
    {
      string fonts = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
      return (TryLoad(Path.Combine(fonts, "arial.ttf"), "ArialMT"), TryLoad(Path.Combine(fonts, "arialbd.ttf"), "Arial-BoldMT"));
    }

    /// <summary>The font at <paramref name="path"/>, or null when it is missing, unreadable, or its licence
    /// forbids embedding (OS/2 fsType "restricted").</summary>
    public static PdfTrueTypeFont TryLoad(string path, string postScriptName)
    {
      try
      {
        if (!System.IO.File.Exists(path))
        {
          return null;
        }

        byte[] file = System.IO.File.ReadAllBytes(path);
        var tables = ReadTableDirectory(file);
        if (tables.TryGetValue("OS/2", out var os2) && (U16(file, os2.Offset + 8) & 0x000F) == 0x0002)
        {
          return null;
        }

        return new PdfTrueTypeFont(postScriptName, file);
      }
      catch (Exception)
      {
        return null;
      }
    }

    /// <summary>A copy that records its own glyph use, so each PDF lists only the widths it needs.</summary>
    public PdfTrueTypeFont ForDocument() => new PdfTrueTypeFont(this);

    /// <summary>The string as two-byte glyph ids in hex, the form an Identity-H text operand takes.</summary>
    public string Encode(string text)
    {
      var sb = new StringBuilder(text.Length * 4);
      for (int i = 0; i < text.Length; i++)
      {
        int cp = char.IsSurrogatePair(text, i) ? char.ConvertToUtf32(text, i++) : text[i];
        ushort gid = this.glyphByChar.TryGetValue(cp, out var g) ? g : (ushort)0;
        if (!this.usedGlyphs.ContainsKey(gid))
        {
          this.usedGlyphs[gid] = cp;
        }

        sb.Append(gid.ToString("X4", CultureInfo.InvariantCulture));
      }

      return sb.ToString();
    }

    /// <summary>The five objects of one embedded font, numbered from <paramref name="firstObject"/>; the
    /// first is the one a page's /Font resource refers to.</summary>
    public List<byte[]> BuildObjects(int firstObject)
    {
      int type0 = firstObject, cid = firstObject + 1, descriptor = firstObject + 2, fontFile = firstObject + 3, toUnicode = firstObject + 4;
      double scale = 1000.0 / this.UnitsPerEm;
      string N(double v) => Math.Round(v * scale).ToString(CultureInfo.InvariantCulture);

      var widths = new StringBuilder();
      foreach (var gid in this.usedGlyphs.Keys.OrderBy(g => g))
      {
        widths.Append(gid).Append(" [").Append(N(this.advance[Math.Min(gid, this.advance.Length - 1)])).Append("] ");
      }

      var objects = new List<byte[]>
      {
        Ascii($"<< /Type /Font /Subtype /Type0 /BaseFont /{this.PostScriptName} /Encoding /Identity-H /DescendantFonts [{cid} 0 R] /ToUnicode {toUnicode} 0 R >>"),
        Ascii($"<< /Type /Font /Subtype /CIDFontType2 /BaseFont /{this.PostScriptName} /CIDSystemInfo << /Registry (Adobe) /Ordering (Identity) /Supplement 0 >> /FontDescriptor {descriptor} 0 R /DW 1000 /W [{widths}] /CIDToGIDMap /Identity >>"),
        Ascii($"<< /Type /FontDescriptor /FontName /{this.PostScriptName} /Flags 32 /FontBBox [{N(this.BBox.XMin)} {N(this.BBox.YMin)} {N(this.BBox.XMax)} {N(this.BBox.YMax)}] /ItalicAngle 0 /Ascent {N(this.Ascent)} /Descent {N(this.Descent)} /CapHeight {N(this.CapHeight)} /StemV 80 /FontFile2 {fontFile} 0 R >>"),
        FontFileStream(this.File),
        Stream(string.Empty, Ascii(this.ToUnicodeCMap())),
      };
      return objects;
    }

    private PdfTrueTypeFont(PdfTrueTypeFont shared)
    {
      this.PostScriptName = shared.PostScriptName;
      this.File = shared.File;
      this.glyphByChar = shared.glyphByChar;
      this.advance = shared.advance;
      this.UnitsPerEm = shared.UnitsPerEm;
      this.BBox = shared.BBox;
      this.Ascent = shared.Ascent;
      this.Descent = shared.Descent;
      this.CapHeight = shared.CapHeight;
    }

    private string ToUnicodeCMap()
    {
      var sb = new StringBuilder();
      sb.Append("/CIDInit /ProcSet findresource begin\n12 dict begin\nbegincmap\n");
      sb.Append("/CIDSystemInfo << /Registry (Adobe) /Ordering (UCS) /Supplement 0 >> def\n");
      sb.Append("/CMapName /Adobe-Identity-UCS def\n/CMapType 2 def\n");
      sb.Append("1 begincodespacerange\n<0000> <FFFF>\nendcodespacerange\n");
      var pairs = this.usedGlyphs.Where(p => p.Key != 0).OrderBy(p => p.Key).ToList();
      for (int i = 0; i < pairs.Count; i += 100)
      {
        var chunk = pairs.Skip(i).Take(100).ToList();
        sb.Append(chunk.Count).Append(" beginbfchar\n");
        foreach (var p in chunk)
        {
          string utf16 = string.Concat(char.ConvertFromUtf32(p.Value).Select(ch => ((int)ch).ToString("X4", CultureInfo.InvariantCulture)));
          sb.Append('<').Append(p.Key.ToString("X4", CultureInfo.InvariantCulture)).Append("> <").Append(utf16).Append(">\n");
        }

        sb.Append("endbfchar\n");
      }

      sb.Append("endcmap\nCMapName currentdict /CMap defineresource pop\nend\nend");
      return sb.ToString();
    }

    private static byte[] Stream(string extraDictEntries, byte[] data)
    {
      var ms = new MemoryStream();
      byte[] head = Ascii($"<< /Length {data.Length} {extraDictEntries}>>\nstream\n");
      ms.Write(head, 0, head.Length);
      ms.Write(data, 0, data.Length);
      byte[] tail = Ascii("\nendstream");
      ms.Write(tail, 0, tail.Length);
      return ms.ToArray();
    }

    /// <summary>The font program, compressed when zlib can be had. .NET's zlib is a native DLL, and on a shop PC
    /// it failed to load ("The underlying compression routine could not be loaded correctly"), which took the
    /// whole report with it. Uncompressed the PDF is only a few hundred KB bigger, so that is the fallback.</summary>
    internal static byte[] FontFileStream(byte[] font, Func<byte[], byte[]> deflate = null)
    {
      try
      {
        return Stream($"/Length1 {font.Length} /Filter /FlateDecode", (deflate ?? Deflate)(font));
      }
      catch (Exception ex) when (ex is InvalidDataException || ex is DllNotFoundException || ex is EntryPointNotFoundException
                                 || ex is TypeInitializationException || ex is BadImageFormatException || ex is IOException)
      {
        return Stream($"/Length1 {font.Length}", font);
      }
    }

    private static byte[] Deflate(byte[] data)
    {
      var ms = new MemoryStream();
      using (var z = new ZLibStream(ms, CompressionLevel.Optimal, leaveOpen: true))
      {
        z.Write(data, 0, data.Length);
      }

      return ms.ToArray();
    }

    private static byte[] Ascii(string s) => Encoding.ASCII.GetBytes(s);

    private static Dictionary<string, (int Offset, int Length)> ReadTableDirectory(byte[] f)
    {
      if (U32(f, 0) == 0x74746366)
      {
        throw new InvalidDataException("font collections are not supported");
      }

      int count = U16(f, 4);
      var tables = new Dictionary<string, (int, int)>(StringComparer.Ordinal);
      for (int i = 0; i < count; i++)
      {
        int rec = 12 + (16 * i);
        string tag = Encoding.ASCII.GetString(f, rec, 4);
        tables[tag] = ((int)U32(f, rec + 8), (int)U32(f, rec + 12));
      }

      foreach (var required in new[] { "head", "hhea", "maxp", "hmtx", "cmap" })
      {
        if (!tables.ContainsKey(required))
        {
          throw new InvalidDataException("missing " + required);
        }
      }

      return tables;
    }

    /// <summary>The Windows Unicode BMP subtable (platform 3, encoding 1, format 4).</summary>
    private static void ReadCmap(byte[] f, int cmap, Dictionary<int, ushort> map)
    {
      int count = U16(f, cmap + 2);
      int sub = -1;
      for (int i = 0; i < count; i++)
      {
        int rec = cmap + 4 + (8 * i);
        if (U16(f, rec) == 3 && U16(f, rec + 2) == 1)
        {
          sub = cmap + (int)U32(f, rec + 4);
          break;
        }
      }

      if (sub < 0 || U16(f, sub) != 4)
      {
        throw new InvalidDataException("no Unicode cmap");
      }

      int segCount = U16(f, sub + 6) / 2;
      int ends = sub + 14;
      int starts = ends + (2 * segCount) + 2;
      int deltas = starts + (2 * segCount);
      int rangeOffsets = deltas + (2 * segCount);
      for (int s = 0; s < segCount; s++)
      {
        int end = U16(f, ends + (2 * s));
        int start = U16(f, starts + (2 * s));
        int delta = S16(f, deltas + (2 * s));
        int rangeOffsetPos = rangeOffsets + (2 * s);
        int rangeOffset = U16(f, rangeOffsetPos);
        for (int c = start; c <= end && c != 0xFFFF; c++)
        {
          int glyph;
          if (rangeOffset == 0)
          {
            glyph = (c + delta) & 0xFFFF;
          }
          else
          {
            int at = rangeOffsetPos + rangeOffset + (2 * (c - start));
            glyph = U16(f, at);
            if (glyph != 0)
            {
              glyph = (glyph + delta) & 0xFFFF;
            }
          }

          if (glyph != 0)
          {
            map[c] = (ushort)glyph;
          }
        }
      }
    }

    private static int U16(byte[] f, int at) => (f[at] << 8) | f[at + 1];

    private static int S16(byte[] f, int at) => (short)U16(f, at);

    private static uint U32(byte[] f, int at) => ((uint)f[at] << 24) | ((uint)f[at + 1] << 16) | ((uint)f[at + 2] << 8) | f[at + 3];
  }
}
