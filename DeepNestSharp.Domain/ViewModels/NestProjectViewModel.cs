namespace DeepNestSharp.Domain.ViewModels
{
  using System;
  using System.Linq;
  using System.Threading.Tasks;
  using DeepNestLib;
  using DeepNestLib.NestProject;
  using DeepNestSharp.Domain.Models;
  using DeepNestSharp.Domain.Services;
  using DeepNestSharp.Ui.Docking;
  using DeepNestSharp.Ui.Models;
  using Light.GuardClauses;
  using Microsoft.Toolkit.Mvvm.Input;

  public class NestProjectViewModel : FileViewModel, INestProjectViewModel
  {
    private int selectedDetailLoadInfoIndex;
    private IDetailLoadInfo selectedDetailLoadInfo;
    private int selectedSheetLoadInfoIndex;
    private ISheetLoadInfo selectedSheetLoadInfo;
    private AsyncRelayCommand addPartCommand;
    private RelayCommand addSheetCommand;
    private RelayCommand clearPartsCommand;
    private RelayCommand<IDetailLoadInfo> removePartCommand;
    private RelayCommand<ISheetLoadInfo> removeSheetCommand;
    private IFileIoService fileIoService;
    private ObservableProjectInfo observableProjectInfo;

    /// <summary>
    /// Initializes a new instance of the <see cref="NestProjectViewModel"/> class.
    /// </summary>
    /// <param name="mainViewModel">MainViewModel singleton; the primary context; access this via the activeDocument property.</param>
    public NestProjectViewModel(IMainViewModel mainViewModel, IFileIoService fileIoService)
      : base(mainViewModel)
    {
      Initialise(mainViewModel, fileIoService);
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="NestProjectViewModel"/> class.
    /// </summary>
    /// <param name="mainViewModel">MainViewModel singleton; the primary context; access this via the activeDocument property.</param>
    /// <param name="filePath">Path to the file to open.</param>
    public NestProjectViewModel(IMainViewModel mainViewModel, string filePath, IFileIoService fileIoService)
      : base(mainViewModel, filePath)
    {
      Initialise(mainViewModel, fileIoService);
    }

    public IAsyncRelayCommand AddPartCommand => addPartCommand ?? (addPartCommand = new AsyncRelayCommand(OnAddPartAsync));

    public IRelayCommand AddSheetCommand => addSheetCommand ?? (addSheetCommand = new RelayCommand(OnAddSheet));

    public IRelayCommand ClearPartsCommand => clearPartsCommand ?? (clearPartsCommand = new RelayCommand(OnClearParts));

    public IRelayCommand<IDetailLoadInfo> RemovePartCommand => removePartCommand ?? (removePartCommand = new RelayCommand<IDetailLoadInfo>(OnRemovePart));

    public IRelayCommand<ISheetLoadInfo> RemoveSheetCommand => removeSheetCommand ?? (removeSheetCommand = new RelayCommand<ISheetLoadInfo>(OnRemoveSheet));

    public override string FileDialogFilter => DeepNestLib.NestProject.ProjectInfo.FileDialogFilter;

    public IProjectInfo ProjectInfo => observableProjectInfo ?? (observableProjectInfo = new ObservableProjectInfo(MainViewModel));

    public IDetailLoadInfo SelectedDetailLoadInfo
    {
#pragma warning disable CS8603 // Possible null reference return.
      get => selectedDetailLoadInfo;
#pragma warning restore CS8603 // Possible null reference return.
      set => SetProperty(ref selectedDetailLoadInfo, value, nameof(SelectedDetailLoadInfo));
    }

    public int SelectedDetailLoadInfoIndex
    {
      get => selectedDetailLoadInfoIndex;
      set => SetProperty(ref selectedDetailLoadInfoIndex, value);
    }

    public ISheetLoadInfo SelectedSheetLoadInfo
    {
#pragma warning disable CS8603 // Possible null reference return.
      get => selectedSheetLoadInfo;
#pragma warning restore CS8603 // Possible null reference return.
      set => SetProperty(ref selectedSheetLoadInfo, value, nameof(SelectedSheetLoadInfo));
    }

    public int SelectedSheetLoadInfoIndex
    {
      get => selectedSheetLoadInfoIndex;
      set => SetProperty(ref selectedSheetLoadInfoIndex, value);
    }

    public override string TextContent { get => this.ProjectInfo.ToJson(); }

    public bool UsePriority => this.MainViewModel.SvgNestConfigViewModel.SvgNestConfig.UsePriority;

    protected override void LoadContent()
    {
      this.ProjectInfo.Load(this.MainViewModel.SvgNestConfigViewModel.SvgNestConfig, this.FilePath);
    }

    protected override void NotifyContentUpdated()
    {
      Contextualise();
      OnPropertyChanged(nameof(SelectedDetailLoadInfoIndex));
      OnPropertyChanged(nameof(SelectedDetailLoadInfo));
    }

    private void Initialise(IMainViewModel mainViewModel, IFileIoService fileIoService)
    {
      this.ProjectInfo.MustBe(observableProjectInfo);
      if (this.observableProjectInfo != null)
      {
        this.observableProjectInfo.IsDirtyChanged += this.ObservableProjectInfo_IsDirtyChanged;
      }

      this.fileIoService = fileIoService;
    }

    private void ObservableProjectInfo_IsDirtyChanged(object sender, EventArgs e)
    {
      this.IsDirty = true;
    }

    private Task OnAddPartAsync() => AddPartsAsync(NoFitPolygon.FileDialogFilter);

    private async Task AddPartsAsync(string fileDialogFilter)
    {
      await AddPartFilesAsync(await this.fileIoService.GetOpenFilePathsAsync(fileDialogFilter));
    }

    /// <summary>Adds the given drawings as parts (dialog or drag-and-drop), refusing each one that cannot
    /// be nested with a message saying why.</summary>
    public async Task AddPartFilesAsync(System.Collections.Generic.IEnumerable<string> filePaths)
    {
      foreach (var filePath in filePaths)
      {
        if (string.IsNullOrWhiteSpace(filePath) || !this.fileIoService.Exists(filePath))
        {
          continue;
        }

        // Read it now and refuse what cannot be used, saying why.
        var reason = await Task.Run(() => DescribeLoadFailure(filePath));
        if (reason != null)
        {
          this.MainViewModel.MessageService.DisplayMessageBox(reason, UiText.Get("Title_AddPart"), MessageBoxIcon.Stop);
          continue;
        }

        AddPartRows(filePath);
      }

      Contextualise();
      this.IsDirty = true;
    }

    /// <summary>
    /// One row per part in the file. A drawing holding several parts becomes several rows, each its own
    /// file, named and counted from the label drawn inside it.
    /// </summary>
    /// <remarks>
    /// A drawing holding ONE part is added exactly as it always was: same path, same name, and its
    /// label ignored. A stray note inside a part must not rename it or change its quantity for someone
    /// who imports that file happily today; a label is there to tell parts apart inside a consolidated
    /// drawing, which is the only case that needs telling apart.
    /// </remarks>
    private void AddPartRows(string filePath)
    {
      System.Collections.Generic.IReadOnlyList<DeepNestLib.IO.DxfPartSplitter.SplitPartFile> parts = SplitOrNothing(filePath);
      if (parts.Count == 0)
      {
        observableProjectInfo?.DetailLoadInfos.Add(new DetailLoadInfo() { Path = filePath });
        return;
      }

      foreach (var part in parts)
      {
        observableProjectInfo?.DetailLoadInfos.Add(new DetailLoadInfo()
        {
          Path = part.Path,
          Quantity = part.Quantity,
          SourceDxfPath = filePath,
          PartIndex = part.PartIndex,
        });
      }
    }

    /// <summary>The parts a drawing was split into, or none at all. Splitting is a convenience: a file
    /// that cannot be split still imports whole rather than refusing.</summary>
    private static System.Collections.Generic.IReadOnlyList<DeepNestLib.IO.DxfPartSplitter.SplitPartFile> SplitOrNothing(string filePath)
    {
      try
      {
        return DeepNestLib.IO.DxfFileExtensions.IsDxf(filePath)
          ? DeepNestLib.IO.DxfPartSplitter.WriteParts(filePath)
          : Array.Empty<DeepNestLib.IO.DxfPartSplitter.SplitPartFile>();
      }
      catch (Exception)
      {
        return Array.Empty<DeepNestLib.IO.DxfPartSplitter.SplitPartFile>();
      }
    }

    /// <summary>Null when the file yields a shape that can be nested; otherwise why it cannot, in words an
    /// operator can act on.</summary>
    private static string DescribeLoadFailure(string filePath)
    {
      var name = System.IO.Path.GetFileName(filePath);
      try
      {
        var detail = new NestExecutionHelper().LoadRawDetail(new System.IO.FileInfo(filePath));
        if (detail == null || !detail.TryConvertToNfp(0, out INfp nfp) || nfp.Points.Length < 3)
        {
          return UiText.Format("Msg_NoShape", name);
        }

        return null;
      }
      catch (Exception ex)
      {
        return UiText.Format("Msg_CannotUse", name, ex.GetBaseException().Message);
      }
    }

    private void OnAddSheet()
    {
      var newSheet = new SheetLoadInfo(this.ProjectInfo.Config);
      observableProjectInfo?.SheetLoadInfos.Add(newSheet);

      Contextualise();
      this.IsDirty = true;
    }

    private void OnClearParts()
    {
      observableProjectInfo?.DetailLoadInfos.Clear();
      Contextualise();
      this.IsDirty = true;
    }

    private void Contextualise()
    {
      OnPropertyChanged(nameof(ProjectInfo));
    }

    private void OnRemovePart(IDetailLoadInfo arg)
    {
      if (arg != null)
      {
        this.ProjectInfo.DetailLoadInfos.Remove(arg);
        Contextualise();
      }
    }

    private void OnRemoveSheet(ISheetLoadInfo arg)
    {
      if (arg != null)
      {
        this.ProjectInfo.SheetLoadInfos.Remove(arg);
        Contextualise();
      }
    }

    protected override void SaveState()
    {
      observableProjectInfo.SaveState();
    }
  }
}