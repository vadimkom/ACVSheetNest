namespace DeepNestSharp.Domain.ViewModels
{
  using System;
  using System.Threading.Tasks;
  using System.Windows.Input;
  using DeepNestLib;
  using DeepNestLib.Placement;
  using DeepNestSharp.Domain.Docking;
  using DeepNestSharp.Domain.Services;

  public interface IMainViewModel
  {
    event EventHandler ActiveDocumentChanged;

    IAboutDialogService AboutDialogService { get; set; }

    IFileViewModel ActiveDocument { get; set; }

    ICommand ActiveDocumentSaveAsCommand { get; }

    ICommand ActiveDocumentSaveCommand { get; }

    ICommand CreateNestProjectCommand { get; }

    IDispatcherService DispatcherService { get; }

    ICommand ExitCommand { get; }

    ICommand LoadNestProjectCommand { get; }

    IMessageService MessageService { get; }

    INestMonitorViewModel NestMonitorViewModel { get; }

    ISvgNestConfigViewModel SvgNestConfigViewModel { get; }

    string Title { get; }

    Task ExportLayoutsAsync(System.Collections.Generic.IReadOnlyList<(ISheetPlacement Sheet, int Count, System.Collections.Generic.IReadOnlyCollection<DeepNestLib.IO.OffcutLine> Offcut)> layouts);

    void OnLoadNestProject(string filePath);

    /// <summary>Loads a project by path with the same save-your-result confirmation the Open menu uses.</summary>
    void LoadNestProjectInteractive(string filePath);

    Task OnLoadNestProjectAsync();

    /// <summary>Saves the document; returns false when the user cancels the Save As dialog.</summary>
    bool Save(IFileViewModel fileViewModel, bool v);
  }
}
