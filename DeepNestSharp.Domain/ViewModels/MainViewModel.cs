namespace DeepNestSharp.Domain.ViewModels
{
  using System;
  using System.Collections.Generic;
  using System.IO;
  using System.Linq;
  using System.Threading.Tasks;
  using System.Windows.Input;
  using DeepNestLib;
  using DeepNestLib.IO;
  using DeepNestLib.NestProject;
  using DeepNestLib.Placement;
  using DeepNestSharp.Domain.Docking;
  using DeepNestSharp.Domain.Services;
  using Microsoft.Toolkit.Mvvm.ComponentModel;
  using Microsoft.Toolkit.Mvvm.Input;

  public abstract class MainViewModel : ObservableRecipient, IMainViewModel
  {
    private readonly IFileIoService fileIoService;
    private readonly IMessageService messageService;
    private RelayCommand exitCommand;
    private AsyncRelayCommand loadNestProjectCommand;
    private RelayCommand activeDocumentSaveCommand;
    private RelayCommand activeDocumentSaveAsCommand;
    private RelayCommand createNestProjectCommand;
    private RelayCommand aboutDialogCommand;

    private IFileViewModel activeDocument;
    private INestMonitorViewModel nestMonitorViewModel;

    public MainViewModel(IMessageService messageService, IDispatcherService dispatcherService, ISvgNestConfig config, IFileIoService fileIoService)
    {
      SvgNestConfigViewModel = new SvgNestConfigViewModel(config);

      this.messageService = messageService;
      this.DispatcherService = dispatcherService;
      this.fileIoService = fileIoService;
      this.ActiveDocumentChanged += this.MainViewModel_ActiveDocumentChanged;
    }

    public event EventHandler ActiveDocumentChanged;

    public IAboutDialogService AboutDialogService { get; set; }

    public ICommand AboutDialogCommand => aboutDialogCommand ?? (aboutDialogCommand = new RelayCommand(() => OnAboutDialog()));

    public ICommand ActiveDocumentSaveCommand => activeDocumentSaveCommand ?? (activeDocumentSaveCommand = new RelayCommand(() => Save(this.ActiveDocument, false), () => this.ActiveDocument?.IsDirty ?? false));

    public ICommand ActiveDocumentSaveAsCommand => activeDocumentSaveAsCommand ?? (activeDocumentSaveAsCommand = new RelayCommand(() => Save(this.ActiveDocument, true), () => true));

    public INestMonitorViewModel NestMonitorViewModel
    {
      get
      {
        if (nestMonitorViewModel == null)
        {
          nestMonitorViewModel = new NestMonitorViewModel(this);
        }

        return nestMonitorViewModel;
      }
    }

    public ISvgNestConfigViewModel SvgNestConfigViewModel { get; }

    public ICommand CreateNestProjectCommand => createNestProjectCommand ?? (createNestProjectCommand = new RelayCommand(OnCreateNestProject));

    public ICommand LoadNestProjectCommand => loadNestProjectCommand ?? (loadNestProjectCommand = new AsyncRelayCommand(OnLoadNestProjectAsync));

    public ICommand ExitCommand => exitCommand ?? (exitCommand = new RelayCommand(OnExit));

    protected abstract void OnExit();

    /// <summary>The one project on screen. Replacing it lets the previous one go: there are no tabs to
    /// keep it in.</summary>
    public IFileViewModel ActiveDocument
    {
      get => activeDocument;
      set
      {
        if (activeDocument != value)
        {
          if (activeDocument is NestProjectViewModel previous)
          {
            previous.PropertyChanged -= this.NestProjectViewModel_PropertyChanged;
          }

          activeDocument = value;
          if (activeDocument is NestProjectViewModel current)
          {
            current.PropertyChanged += this.NestProjectViewModel_PropertyChanged;
          }

          OnPropertyChanged(nameof(ActiveDocument));
          OnPropertyChanged(nameof(Title));
          ActiveDocumentChanged?.Invoke(this, EventArgs.Empty);
        }
      }
    }

    public IDispatcherService DispatcherService { get; }

    public IMessageService MessageService => this.messageService;

    // 3-field version (1.1.2, not 1.1.2.0).
    public string Title => $"SheetNest {this.GetType().Assembly.GetName().Version?.ToString(3)}";

    private void OnAboutDialog()
    {
      this.AboutDialogService.ShowDialog();
    }

    public async Task OnLoadNestProjectAsync()
    {
      if (!ConfirmNestResultSaved())
      {
        return;
      }

      var filePath = await fileIoService.GetOpenFilePathAsync(ProjectInfo.FileDialogFilter, SvgNestConfigViewModel.SvgNestConfig.LastNestFilePath).ConfigureAwait(false);
      OnLoadNestProject(filePath);
    }

    /// <inheritdoc />
    public void LoadNestProjectInteractive(string filePath)
    {
      if (!ConfirmNestResultSaved())
      {
        return;
      }

      OnLoadNestProject(filePath);
    }

    public void OnLoadNestProject(string filePath)
    {
      if (DispatcherService.InvokeRequired)
      {
        DispatcherService.Invoke(() => OnLoadNestProject(filePath));
      }
      else
      {
        string locatedFilePath;
        if (TryLocateFile(filePath, out locatedFilePath))
        {
          this.ActiveDocument = new NestProjectViewModel(this, locatedFilePath, fileIoService);
        }
      }
    }

    /// <summary>
    /// Exports every distinct layout to its own file after ONE save dialog. The name typed there is the base
    /// ("test"), and each layout becomes <c>test_1_1250x2500_x3.dxf</c>: its number in the report, the sheet
    /// size, and how many times it is cut. It used to ask for a file name once per layout, with nothing on
    /// the files to say which layout or how many to cut.
    /// </summary>
    public async Task ExportLayoutsAsync(IReadOnlyList<(ISheetPlacement Sheet, int Count, IReadOnlyCollection<OffcutLine> Offcut)> layouts)
    {
      try
      {
        var sheets = (layouts ?? Array.Empty<(ISheetPlacement, int, IReadOnlyCollection<OffcutLine>)>())
          .Where(l => l.Sheet != null)
          .ToList();
        if (sheets.Count == 0)
        {
          return;
        }

        var parts = sheets.SelectMany(l => l.Sheet.PartPlacements).Select(o => o.Part).ToList();
        if (parts.ContainsDxfs() && parts.ContainsSvgs())
        {
          MessageService.DisplayMessageBox(UiText.Get("Msg_MixedSvgDxf"), UiText.Get("Title_Export"), MessageBoxIcon.Information);
          return;
        }

        IExport probe = ExporterFactory.GetExporter(parts);
        string extension = probe is DxfExporter ? ".dxf" : ".svg";
        string suggested = this.ActiveDocument?.FilePath is string project && !string.IsNullOrWhiteSpace(project)
          ? Path.GetFileNameWithoutExtension(project)
          : null;
        var chosen = fileIoService.GetSaveFilePath(probe.SaveFileDialogFilter, suggested);
        if (string.IsNullOrWhiteSpace(chosen))
        {
          return;
        }

        string folder = Path.GetDirectoryName(chosen);
        string baseName = Path.GetFileNameWithoutExtension(chosen);
        var paths = sheets
          .Select((l, i) => Path.Combine(folder, LayoutFileName(baseName, i + 1, l.Sheet.Sheet.WidthCalculated, l.Sheet.Sheet.HeightCalculated, l.Count, extension)))
          .ToList();

        var existing = paths.Where(File.Exists).Select(Path.GetFileName).ToList();
        if (existing.Count > 0
            && MessageService.DisplayOkCancel(
              UiText.Format("Msg_ExportOverwrite", string.Join("\n", existing)),
              UiText.Get("Title_Export"),
              MessageBoxIcon.Question) != MessageBoxResult.OK)
        {
          return;
        }

        for (int i = 0; i < sheets.Count; i++)
        {
          IExport exporter = ExporterFactory.GetExporter(sheets[i].Sheet.PartPlacements.Select(o => o.Part).ToList());
          if (exporter is DxfExporter dxfExporter)
          {
            dxfExporter.PartNameExport = SvgNest.Config.DxfPartNameExport;
          }

          await exporter.Export(
            paths[i],
            sheets[i].Sheet,
            MergeLinesForExport(SvgNest.Config.MergeLines, sheets[i].Sheet),
            SvgNest.Config.DifferentiateChildren,
            sheets[i].Offcut);
        }
      }
      catch (Exception ex)
      {
        MessageService.DisplayMessageBox(ex.Message, UiText.Get("Title_SaveError"), MessageBoxIcon.Exclamation);
      }
    }

    /// <summary><c>base_N_WxH_xCount.ext</c>, sizes without trailing zeros ("1250x2500", "1250.5x2500").</summary>
    internal static string LayoutFileName(string baseName, int index, double sheetWidth, double sheetHeight, int count, string extension)
    {
      string Size(double v) => v.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
      string name = string.IsNullOrWhiteSpace(baseName) ? "layout" : baseName.Trim();
      return $"{name}_{index}_{Size(sheetWidth)}x{Size(sheetHeight)}_x{Math.Max(1, count)}{extension}";
    }

    /// <summary>
    /// A layout known to contain shared edges is authoritative for its own export. The application
    /// preference may opt ordinary layouts into merging, but turning it off must not duplicate a cut that
    /// this particular nest deliberately shares.
    /// </summary>
    internal static bool MergeLinesForExport(bool configuredMergeLines, ISheetPlacement sheetPlacement)
      => configuredMergeLines || (sheetPlacement?.RequiresMergedLines ?? false);

    /// <summary>
    /// Saves the document; returns true when the file was actually written (false = the user
    /// cancelled the Save As dialog, or there was nothing to save).
    /// </summary>
    public bool Save(IFileViewModel fileToSave, bool saveAsFlag = false)
    {
      if (fileToSave == null)
      {
        return false;
      }

      // FilePath is never null for a new document (the getter coalesces to string.Empty), so the
      // old `== null` test skipped the Save As dialog entirely and the save silently did nothing.
      if (string.IsNullOrEmpty(fileToSave.FilePath) || saveAsFlag)
      {
        // An already-saved document pre-fills its own name and folder, so plain Enter overwrites it
        // and typing renames it; new documents start in the last-used nest folder.
        string suggestedName = null;
        string initialDirectory = SvgNestConfigViewModel.SvgNestConfig.LastNestFilePath;
        if (!string.IsNullOrEmpty(fileToSave.FilePath))
        {
          suggestedName = Path.GetFileName(fileToSave.FilePath);
          initialDirectory = Path.GetDirectoryName(fileToSave.FilePath);
        }

        var filePath = fileIoService.GetSaveFilePath(fileToSave.FileDialogFilter, suggestedName, initialDirectory);
        if (string.IsNullOrWhiteSpace(filePath))
        {
          return false; // dialog cancelled — do NOT fall through to overwrite the old path
        }

        fileToSave.FilePath = filePath;
      }

      if (string.IsNullOrEmpty(fileToSave.FilePath))
      {
        return false;
      }

      // A project carries the nest result that is on screen (or clears it when there is none), so
      // reopening the .dnest brings the nesting back exactly as saved.
      if (fileToSave is NestProjectViewModel projectViewModel)
      {
        projectViewModel.ProjectInfo.LastNestResultJson =
          (NestMonitorViewModel.SelectedItem as NestResult)?.ToJson(false) ?? string.Empty;
      }

      File.WriteAllText(fileToSave.FilePath, fileToSave.TextContent);
      fileToSave.IsDirty = false;

      return true;
    }

    private bool TryLocateFile(string filePath, out string locatedFilePath)
    {
      if (!string.IsNullOrWhiteSpace(filePath) && new FileInfo(filePath).Exists)
      {
        locatedFilePath = filePath;
        return true;
      }

      if (string.IsNullOrWhiteSpace(filePath))
      {
        locatedFilePath = filePath;
        return false;
      }

      this.MessageService.DisplayMessageBox(UiText.Format("Msg_FileMissing", filePath), UiText.Get("Title_FileMissing"), MessageBoxIcon.Information);
      locatedFilePath = string.Empty;
      return false;
    }

    /// <summary>
    /// New/Open are about to replace the on-screen nest result — offer to save the project first.
    /// True = go ahead (saved, or the user chose not to save); false = the user cancelled.
    /// </summary>
    private bool ConfirmNestResultSaved()
    {
      if (NestMonitorViewModel.SelectedItem == null || ActiveDocument == null)
      {
        return true;
      }

      var answer = MessageService.DisplayYesNoCancel(
        UiText.Get("Msg_SaveFirst"),
        "SheetNest",
        MessageBoxIcon.Question);
      if (answer == MessageBoxResult.Yes)
      {
        return Save(ActiveDocument, true); // cancelling the Save As dialog cancels the New/Open too
      }

      return answer == MessageBoxResult.No;
    }

    private void OnCreateNestProject()
    {
      if (!ConfirmNestResultSaved())
      {
        return;
      }

      this.ActiveDocument = new NestProjectViewModel(this, fileIoService);
      this.NestMonitorViewModel.Reset(); // clear the previous nest result + preview so it doesn't linger
    }

    private void MainViewModel_ActiveDocumentChanged(object sender, EventArgs e)
    {
      this.activeDocumentSaveCommand?.NotifyCanExecuteChanged();
      this.activeDocumentSaveAsCommand?.NotifyCanExecuteChanged();
    }

    private void NestProjectViewModel_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
      if (e.PropertyName == $"{nameof(IFileViewModel.IsDirty)}")
      {
        activeDocumentSaveCommand?.NotifyCanExecuteChanged();
        activeDocumentSaveAsCommand?.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(Title)); // the title's unsaved-changes asterisk tracks IsDirty
      }
    }
  }
}
