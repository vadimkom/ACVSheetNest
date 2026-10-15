namespace DeepNestSharp.Domain.Models
{
  using System;
  using System.Collections.Generic;
  using System.IO;
  using System.Linq;
  using System.Threading.Tasks;
  using DeepNestLib;
  using DeepNestLib.IO;
  using DeepNestLib.NestProject;
  using DeepNestSharp.Domain.Services;

  public class ObservableDetailLoadInfo : ObservablePropertyObject, IWrapper<IDetailLoadInfo, DetailLoadInfo>, IDetailLoadInfo
  {
    private readonly DetailLoadInfo detailLoadInfo;
    private int? netArea;
    private INfp nfp;
    private bool loading;
    private int effectiveColorRgb = -1;

    /// <summary>
    /// Initializes a new instance of the <see cref="ObservableDetailLoadInfo"/> class.
    /// </summary>
    /// <param name="sheetLoadInfo">The ProjectInfo to wrap.</param>
    public ObservableDetailLoadInfo(DetailLoadInfo detailLoadInfo)
    {
      this.detailLoadInfo = detailLoadInfo;
      this.PropertyChanged += this.ObservableDetailLoadInfo_PropertyChanged;
    }

    private void ObservableDetailLoadInfo_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
      if (e.PropertyName != nameof(IsDirty))
      {
        OnPropertyChanged(nameof(IsDirty));
      }
    }

    public IList<AnglesEnum> AnglesList => Enum.GetValues(typeof(AnglesEnum)).OfType<AnglesEnum>().ToList();

    public override bool IsDirty => this.detailLoadInfo.IsDirty;

    public bool IsValid => this.IsExists && !(this.Nfp is InvalidNoFitPolygon);

    public bool IsExists => this.detailLoadInfo.IsExists;

    public bool IsIncluded
    {
      get => detailLoadInfo.IsIncluded;
      set => SetProperty(nameof(IsIncluded), () => detailLoadInfo.IsIncluded, v => detailLoadInfo.IsIncluded = v, value);
    }

    public bool IsMultiplied
    {
      get => detailLoadInfo.IsMultiplied;
      set => SetProperty(nameof(IsMultiplied), () => detailLoadInfo.IsMultiplied, v => detailLoadInfo.IsMultiplied = v, value);
    }

    public bool IsPriority
    {
      get => detailLoadInfo.IsPriority;
      set => SetProperty(nameof(IsPriority), () => detailLoadInfo.IsPriority, v => detailLoadInfo.IsPriority = v, value);
    }

    public string Name
    {
      get => detailLoadInfo.Name;
    }

    public string Path
    {
      get => detailLoadInfo.Path;
      set
      {
        if (detailLoadInfo.Path == value)
        {
          return;
        }

        SetProperty(nameof(Path), () => detailLoadInfo.Path, v => detailLoadInfo.Path = v, value);

        // Another file is another shape (a flipped part is a new file): the loaded one no longer applies.
        this.nfp = null;
        this.netArea = null;
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(PreviewSource));
        OnPropertyChanged(nameof(SizeLabel));
        NotifyCommonCuttingCapabilityChanged();
      }
    }

    public PartFlip Flip
    {
      get => detailLoadInfo.Flip;
      set
      {
        SetProperty(nameof(Flip), () => detailLoadInfo.Flip, v => detailLoadInfo.Flip = v, value);
        OnPropertyChanged(nameof(FlipLabel));
      }
    }

    public string UnflippedPath
    {
      get => detailLoadInfo.UnflippedPath;
      set => SetProperty(nameof(UnflippedPath), () => detailLoadInfo.UnflippedPath, v => detailLoadInfo.UnflippedPath = v, value);
    }

    /// <summary>Says on the row that the part is flipped from its drawing; empty when it is not.</summary>
    public string FlipLabel => detailLoadInfo.Flip switch
    {
      PartFlip.LeftRight => DeepNestSharp.Domain.Services.UiText.Get("Part_FlippedLeftRight"),
      PartFlip.UpDown => DeepNestSharp.Domain.Services.UiText.Get("Part_FlippedUpDown"),
      PartFlip.LeftRight | PartFlip.UpDown => DeepNestSharp.Domain.Services.UiText.Get("Part_FlippedBoth"),
      _ => string.Empty,
    };

    /// <summary>Asks the row to read its words again, after the program's language has changed.</summary>
    public void RefreshTexts()
    {
      OnPropertyChanged(nameof(FlipLabel));
      OnPropertyChanged(nameof(NestingLabel));
      OnPropertyChanged(nameof(FillLabel));
    }

    public int Quantity
    {
      get => detailLoadInfo.Quantity;
      set
      {
        SetProperty(nameof(Quantity), () => detailLoadInfo.Quantity, v => detailLoadInfo.Quantity = v, value);
        OnPropertyChanged(nameof(FillLabel));
        OnPropertyChanged(nameof(NestedInfo));
      }
    }

    public int Extra
    {
      get => detailLoadInfo.Extra;
      set
      {
        SetProperty(nameof(Extra), () => detailLoadInfo.Extra, v => detailLoadInfo.Extra = v, value);
        OnPropertyChanged(nameof(NestedInfo));
      }
    }

    public int MirrorQuantity
    {
      get => detailLoadInfo.MirrorQuantity;
      set
      {
        SetProperty(nameof(MirrorQuantity), () => detailLoadInfo.MirrorQuantity, v => detailLoadInfo.MirrorQuantity = v, value);
        OnPropertyChanged(nameof(NestedInfo));
      }
    }

    public bool FillSheets
    {
      get => detailLoadInfo.FillSheets;
      set
      {
        SetProperty(nameof(FillSheets), () => detailLoadInfo.FillSheets, v => detailLoadInfo.FillSheets = v, value);
        OnPropertyChanged(nameof(FillLabel));
      }
    }

    /// <summary>What the quantity cell says for a part that fills the sheets: "fill", and how many the last nest
    /// put on once there has been one.</summary>
    public string FillLabel => detailLoadInfo.Quantity > 0
      ? DeepNestSharp.Domain.Services.UiText.Format("Part_FillCount", detailLoadInfo.Quantity)
      : DeepNestSharp.Domain.Services.UiText.Get("Part_Fill");

    private int nestedCount;

    /// <summary>
    /// Gets or sets how many copies of this part the on-screen nest placed (0 = no result).
    /// Display-only — never persisted.
    /// </summary>
    public int NestedCount
    {
      get => nestedCount;
      set
      {
        if (nestedCount != value)
        {
          nestedCount = value;
          OnPropertyChanged(nameof(NestedCount));
          OnPropertyChanged(nameof(NestedInfo));
        }
      }
    }

    /// <summary>
    /// Gets the always-visible "Available" indicator: copies still left to nest / total requested
    /// (quantity + spares + mirrored). No result: "30/30"; a nest that placed 26: "4/30".
    /// </summary>
    public string NestedInfo => $"{System.Math.Max(0, Quantity + Extra + MirrorQuantity - nestedCount)}/{Quantity + Extra + MirrorQuantity}";

    public int Rotations
    {
      get => detailLoadInfo.Rotations;
      set => SetProperty(nameof(Rotations), () => detailLoadInfo.Rotations, v => detailLoadInfo.Rotations = v, value);
    }

    public int Priority
    {
      get => detailLoadInfo.Priority;
      set => SetProperty(nameof(Priority), () => detailLoadInfo.Priority, v => detailLoadInfo.Priority = v, value);
    }

    public double Spacing
    {
      get => detailLoadInfo.Spacing;
      set
      {
        SetProperty(nameof(Spacing), () => detailLoadInfo.Spacing, v => detailLoadInfo.Spacing = v, value);
        OnPropertyChanged(nameof(NestingLabel));
      }
    }

    public CommonCuttingMode CommonCutting
    {
      get => detailLoadInfo.CommonCutting;
      set
      {
        SetProperty(nameof(CommonCutting), () => detailLoadInfo.CommonCutting, v => detailLoadInfo.CommonCutting = v, value);
        NotifyEffectiveCommonCuttingChanged();
      }
    }

    /// <summary>The mode with direct-DXF/STEP confirmation taken into account.</summary>
    public CommonCuttingMode EffectiveCommonCutting => detailLoadInfo.EffectiveCommonCutting;

    public bool DirectCommonCuttingEnabled
    {
      get => detailLoadInfo.DirectCommonCuttingEnabled;
      set
      {
        SetProperty(
          nameof(DirectCommonCuttingEnabled),
          () => detailLoadInfo.DirectCommonCuttingEnabled,
          v => detailLoadInfo.DirectCommonCuttingEnabled = v,
          value);
        NotifyEffectiveCommonCuttingChanged();
      }
    }

    public bool SupportsCommonCutting => detailLoadInfo.SupportsCommonCutting;

    public bool CommonLine
    {
      get => detailLoadInfo.CommonLine;
      set
      {
        var targetMode = value ? CommonCuttingMode.Unrestricted : CommonCuttingMode.None;
        bool modeChanged = detailLoadInfo.CommonCutting != targetMode;
        bool directConfirmationChanged = detailLoadInfo.DirectCommonCuttingEnabled != value;
        if (!modeChanged && !directConfirmationChanged)
        {
          return;
        }

        detailLoadInfo.CommonLine = value;
        OnPropertyChanged(nameof(CommonLine));
        OnPropertyChanged(nameof(CommonCutting));
        OnPropertyChanged(nameof(DirectCommonCuttingEnabled));
        NotifyEffectiveCommonCuttingChanged();
      }
    }

    private void NotifyEffectiveCommonCuttingChanged()
    {
      OnPropertyChanged(nameof(EffectiveCommonCutting));
      OnPropertyChanged(nameof(NestingLabel));
    }

    private void NotifyCommonCuttingCapabilityChanged()
    {
      OnPropertyChanged(nameof(SupportsCommonCutting));
      NotifyEffectiveCommonCuttingChanged();
    }

    /// <summary>The colour this part is drawn in (0xRRGGBB), or -1 to take the one for its place in the list.</summary>
    public int ColorRgb
    {
      get => detailLoadInfo.ColorRgb;
      set => SetProperty(nameof(ColorRgb), () => detailLoadInfo.ColorRgb, v => detailLoadInfo.ColorRgb = v, value);
    }

    /// <summary>
    /// The colour this part actually appears in — the chosen one, or the palette colour for its place in
    /// the list. Filled in by the window, which is the only thing that knows the whole list; NOT saved with
    /// the project (only the user's own choice is), and it must not mark the project dirty.
    /// </summary>
    public int EffectiveColorRgb
    {
      get => this.effectiveColorRgb;
      set
      {
        if (this.effectiveColorRgb != value)
        {
          this.effectiveColorRgb = value;
          OnPropertyChanged(nameof(EffectiveColorRgb));
        }
      }
    }

    /// <summary>Human-readable nesting summary for the part card (spacing / common line / rotation).</summary>
    public string NestingLabel
    {
      get
      {
        // Both matter now: the mode says who this part may share a cut with, the spacing says what it
        // keeps to everyone else. Showing only one of them hides half the setting. The EFFECTIVE mode,
        // so a direct DXF/STEP only advertises common line after the operator confirms it, while an
        // unconfirmed legacy value stays quiet.
        string cc = detailLoadInfo.EffectiveCommonCutting switch
        {
          CommonCuttingMode.Unrestricted => UiText.Get("Card_CommonLine") + " · ",
          CommonCuttingMode.SamePart => UiText.Get("Card_CommonLineSame") + " · ",
          _ => string.Empty,
        };
        string spacing = cc + (detailLoadInfo.Spacing < 0 ? UiText.Get("Card_SpacingDefault")
          : UiText.Format("Card_Spacing", detailLoadInfo.Spacing.ToString("0.###", System.Globalization.CultureInfo.CurrentCulture)));
        string rot = detailLoadInfo.Rotations switch
        {
          1 => UiText.Get("Settings_Rot_AsDrawn"),
          2 => UiText.Get("Settings_Rot_0_180"),
          4 => UiText.Get("Rot_4Way"),
          1001 => UiText.Get("Rot_90Only"),   // orientation permission codes (see RotationCodes.PermittedSet)
          1002 => "0°+90°",
          1003 => "90°+270°",
          8 => UiText.Get("Settings_Rot_45"), // NOT free: the engine gets eight discrete angles, and calling this
                                              // "free" here and in the picker is how a user came to report free
                                              // rotation broken
          36 => UiText.Get("Rot_Free"),
          > 4 => UiText.Get("Card_RotOther"),
          _ => UiText.Get("Rot_Job"),         // -1, and it follows Settings > Advanced Settings
        };
        return $"{spacing} · {rot}";
      }
    }

    public int NetArea
    {
      get
      {
        // Zero until the part has loaded in the background; it says so when it has.
        if (netArea == null && this.Nfp is INfp loaded)
        {
          this.netArea = (int)loaded.NetArea;
        }

        return netArea ?? 0;
      }
    }

    /// <summary>Flat part size "W × H" in the drawing's units; empty until loaded.</summary>
    public string SizeLabel
    {
      get
      {
        var n = this.nfp;
        if (n == null)
        {
          // The row asks for the size first, so asking is what starts the part loading; it says so again
          // once the shape is in.
          _ = this.Nfp;
          return string.Empty;
        }

        if (n.Length < 2)
        {
          return string.Empty;
        }

        return $"{n.WidthCalculated:0.00} × {n.HeightCalculated:0.00}";
      }
    }

    public AnglesEnum StrictAngle
    {
      get => detailLoadInfo.StrictAngle;
      set => SetProperty(nameof(StrictAngle), () => detailLoadInfo.StrictAngle, v => detailLoadInfo.StrictAngle = v, value);
    }

    public string SourceDxfPath
    {
      get => detailLoadInfo.SourceDxfPath;
      set => SetProperty(nameof(SourceDxfPath), () => detailLoadInfo.SourceDxfPath, v => detailLoadInfo.SourceDxfPath = v, value);
    }

    public int PartIndex
    {
      get => detailLoadInfo.PartIndex;
      set => SetProperty(nameof(PartIndex), () => detailLoadInfo.PartIndex, v => detailLoadInfo.PartIndex = v, value);
    }

    public DetailLoadInfo Item => detailLoadInfo;

    /// <summary>The thumbnail binds here so it refreshes once the (possibly slow) geometry loads.</summary>
    public IDetailLoadInfo PreviewSource => this;

    /// <summary>
    /// The part's shape, or null while it is still loading; asking starts the load, once.
    /// </summary>
    /// <remarks>
    /// The load runs on the thread pool. <see cref="DxfParser.LoadDxfFile"/> only awaits when it has to
    /// retry a locked file, so awaited here directly it read the drawing on the UI thread and raised "the
    /// size is in" from inside the very getter the row was asking - a notification WPF drops. Rows whose
    /// file happened to be locked by the thumbnail went through the retry and showed their size; the
    /// others stayed blank.
    /// </remarks>
    internal INfp Nfp
    {
      get
      {
        if (this.nfp == null && !this.loading)
        {
          this.loading = true;

          // Answer on the thread that asked, which is the row's. A context fixed once for the whole type took
          // whichever thread first made a part as "main": a project opened through the Open dialog makes its
          // parts off the UI thread, and the answer then never reached the rows, which kept their sizes blank.
          var asker = System.Threading.SynchronizationContext.Current;
          _ = Task.Run(async () =>
          {
            try
            {
              INfp loaded = await this.LoadAsync().ConfigureAwait(false);
              this.netArea = (int)loaded.NetArea;
            }
            catch
            {
              // A shape without an area still has a size to show, or shows none.
            }

            void Announce(object _)
            {
              this.loading = false;
              this.OnPropertyChanged(nameof(this.PreviewSource));
              this.OnPropertyChanged(nameof(this.SizeLabel));
              this.OnPropertyChanged(nameof(this.NetArea));
            }

            if (asker != null)
            {
              asker.Post(Announce, null);
            }
            else
            {
              Announce(null);
            }
          });
        }

        return this.nfp;
      }
    }

    public async Task<INfp> LoadAsync()
    {
      INfp cached = this.nfp;
      if (cached != null)
      {
        return cached;
      }

      string path = detailLoadInfo.Path;
      INfp loaded;
      try
      {
        loaded = new FileInfo(path).Exists
          ? (await DxfParser.LoadDxfFile(path).ConfigureAwait(false)).ToNfp()
          : new NoFitPolygon();
      }
      catch
      {
        loaded = new InvalidNoFitPolygon();
      }

      // A part flipped while it loaded is another file now; its old shape must not stick.
      if (path == detailLoadInfo.Path)
      {
        this.nfp = loaded;
      }

      return loaded;
    }
  }
}
