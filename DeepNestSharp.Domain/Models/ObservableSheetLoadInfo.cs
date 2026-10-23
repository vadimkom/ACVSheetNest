namespace DeepNestSharp.Domain.Models
{
  using DeepNestLib.NestProject;

  public class ObservableSheetLoadInfo : ObservablePropertyObject, IWrapper<ISheetLoadInfo, SheetLoadInfo>, ISheetLoadInfo
  {
    private readonly SheetLoadInfo sheetLoadInfo;

    /// <summary>
    /// Initializes a new instance of the <see cref="ObservableSheetLoadInfo"/> class.
    /// </summary>
    /// <param name="sheetLoadInfo">The ProjectInfo to wrap.</param>
    public ObservableSheetLoadInfo(SheetLoadInfo sheetLoadInfo) => this.sheetLoadInfo = sheetLoadInfo;

    public int Height
    {
      get => sheetLoadInfo.Height;
      set => SetProperty(nameof(Height), () => sheetLoadInfo.Height, v => sheetLoadInfo.Height = v, value);
    }

    public override bool IsDirty => true;

    public int Quantity
    {
      get => sheetLoadInfo.Quantity;
      set
      {
        SetProperty(nameof(Quantity), () => sheetLoadInfo.Quantity, v => sheetLoadInfo.Quantity = v, value);
        OnPropertyChanged(nameof(NestedInfo));
      }
    }

    public bool Unlimited
    {
      get => sheetLoadInfo.Unlimited;
      set
      {
        SetProperty(nameof(Unlimited), () => sheetLoadInfo.Unlimited, v => sheetLoadInfo.Unlimited = v, value);
        OnPropertyChanged(nameof(NestedInfo));
      }
    }

    public int Width
    {
      get => sheetLoadInfo.Width;
      set => SetProperty(nameof(Width), () => sheetLoadInfo.Width, v => sheetLoadInfo.Width = v, value);
    }

    public int Priority
    {
      get => sheetLoadInfo.Priority;
      set => SetProperty(nameof(Priority), () => sheetLoadInfo.Priority, v => sheetLoadInfo.Priority = v, value);
    }

    private int nestedCount;

    /// <summary>
    /// Gets or sets how many sheets of this size the on-screen nest consumed (0 = no result).
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
    /// Gets the always-visible "used / in stock" indicator: how many sheets of this size the nest on screen
    /// took, of the quantity entered. A nest no longer takes them off the quantity (that summed up across
    /// nests and confused the operator): no result "0/31", a nest of 28 "28/31".
    /// <para>An unlimited size has no stock level, so it reports only what the nest took.</para>
    /// </summary>
    public string NestedInfo => Unlimited
      ? DeepNestSharp.Domain.Services.UiText.Format("Sheet_UnlimitedUsed", nestedCount)
      : $"{nestedCount}/{Quantity}";

    public SheetLoadInfo Item => this.sheetLoadInfo;
  }
}
