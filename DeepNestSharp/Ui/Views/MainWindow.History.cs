namespace DeepNestSharp.Ui.Views
{
  using System;
  using System.Collections.Generic;
  using System.Collections.Specialized;
  using System.ComponentModel;
  using System.Linq;
  using System.Windows;
  using System.Windows.Controls.Primitives;
  using System.Windows.Input;
  using System.Windows.Threading;
  using DeepNestLib.NestProject;
  using DeepNestSharp.Domain.ViewModels;
  using DeepNestSharp.Ui.Models;
  using Localization = DeepNestSharp.Ui.Localization;

  /// <summary>Ctrl+Z on the job, going back to the saved project, and saving it with its drawings (2026-11-17).</summary>
  public partial class MainWindow
  {
    private readonly List<INotifyCollectionChanged> historyLists = new List<INotifyCollectionChanged>();
    private readonly HashSet<INotifyPropertyChanged> historyItems = new HashSet<INotifyPropertyChanged>();
    private ProjectHistory history;
    private NestProjectViewModel historyDoc;
    private DispatcherTimer historyTimer;

    /// <summary>A project became active: its history starts empty once it has settled.</summary>
    private void StartHistory(NestProjectViewModel doc)
    {
      if (this.historyDoc?.ProjectInfo is INotifyPropertyChanged old)
      {
        old.PropertyChanged -= this.OnHistoryProjectChanged;
      }

      this.history = null;
      this.historyDoc = doc;
      this.UnhookHistoryLists();
      if (doc?.ProjectInfo is INotifyPropertyChanged info)
      {
        info.PropertyChanged += this.OnHistoryProjectChanged;
      }

      this.HookHistoryLists();

      // The load, a new job's material and the up-downs' first values all land before this runs.
      this.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
      {
        if (ReferenceEquals(this.historyDoc, doc) && doc != null)
        {
          this.history = new ProjectHistory(() => ProjectHistory.Rows(doc.ProjectInfo.ToJson()));
        }
      }));
    }

    private void OnHistoryProjectChanged(object sender, PropertyChangedEventArgs e)
    {
      if (e.PropertyName == nameof(IProjectInfo.DetailLoadInfos) || e.PropertyName == nameof(IProjectInfo.SheetLoadInfos))
      {
        this.UnhookHistoryLists();
        this.HookHistoryLists();
      }

      this.NoteSoon();
    }

    private void HookHistoryLists()
    {
      var info = this.historyDoc?.ProjectInfo;
      if (info == null)
      {
        return;
      }

      foreach (var list in new object[] { info.DetailLoadInfos, info.SheetLoadInfos }.OfType<INotifyCollectionChanged>())
      {
        list.CollectionChanged += this.OnHistoryListChanged;
        this.historyLists.Add(list);
        foreach (var item in ((System.Collections.IEnumerable)list).OfType<INotifyPropertyChanged>())
        {
          this.HookHistoryItem(item);
        }
      }
    }

    private void UnhookHistoryLists()
    {
      foreach (var list in this.historyLists)
      {
        list.CollectionChanged -= this.OnHistoryListChanged;
      }

      foreach (var item in this.historyItems)
      {
        item.PropertyChanged -= this.OnHistoryItemChanged;
      }

      this.historyLists.Clear();
      this.historyItems.Clear();
    }

    private void HookHistoryItem(INotifyPropertyChanged item)
    {
      if (this.historyItems.Add(item))
      {
        item.PropertyChanged += this.OnHistoryItemChanged;
      }
    }

    private void OnHistoryListChanged(object sender, NotifyCollectionChangedEventArgs e)
    {
      foreach (var item in (e.NewItems ?? Array.Empty<object>()).OfType<INotifyPropertyChanged>())
      {
        this.HookHistoryItem(item);
      }

      this.NoteSoon();
    }

    private void OnHistoryItemChanged(object sender, PropertyChangedEventArgs e) => this.NoteSoon();

    /// <summary>Changes come in bursts (a row added sets a dozen properties, a typed number one per key):
    /// one note once they stop.</summary>
    private void NoteSoon()
    {
      if (this.history == null)
      {
        return;
      }

      if (this.historyTimer == null)
      {
        this.historyTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(400) };
        this.historyTimer.Tick += (_, __) =>
        {
          this.historyTimer.Stop();
          this.history?.Note();
        };
      }

      this.historyTimer.Stop();
      this.historyTimer.Start();
    }

    private void OnUndo(object sender, RoutedEventArgs e) => this.UndoProjectChange();

    private bool UndoProjectChange()
    {
      if (!(ViewModel.ActiveDocument is NestProjectViewModel doc) || this.history == null || this.nestCts != null
        || !ReferenceEquals(doc, this.historyDoc) || !(doc.ProjectInfo is ObservableProjectInfo info))
      {
        return false;
      }

      this.historyTimer?.Stop();
      var config = ViewModel.SvgNestConfigViewModel.SvgNestConfig;
      bool undone = this.history.Undo(json =>
      {
        var source = ProjectInfo.FromJson(config, json);
        source.CommonLineGapInches = info.CommonLineGapInches;
        source.CommonLineMaxClusterParts = info.CommonLineMaxClusterParts;
        info.Restore(source);
      });

      if (!undone)
      {
        this.ShowStatus(Localization.Get("Msg_NothingToUndo"));
        return false;
      }

      this.ApplyPartColours(doc.ProjectInfo.DetailLoadInfos);
      this.ShowProjectMaterial();
      this.UpdateSummaryBadges();
      this.ShowStatus(Localization.Get("Msg_Undone"));
      return true;
    }

    /// <summary>Ctrl+Z on the Parts and Sheets steps, unless a text field has the focus (it undoes its own typing).</summary>
    private bool UndoKey(KeyEventArgs e)
    {
      if (e.Key != Key.Z || Keyboard.Modifiers != ModifierKeys.Control || e.OriginalSource is TextBoxBase
        || (this.currentMode != WorkflowMode.Parts && this.currentMode != WorkflowMode.Sheets))
      {
        return false;
      }

      this.UndoProjectChange();
      e.Handled = true;
      return true;
    }

    private void ShowStatus(string text)
    {
      this.statusBarNestPhaseText.Text = text;
      this.statusBarNestPhaseText.Visibility = Visibility.Visible;
    }

    /// <summary>File → Revert to saved: the project as it was last saved, every change since dropped.</summary>
    private void OnRevertToSaved(object sender, RoutedEventArgs e)
    {
      if (!(ViewModel.ActiveDocument is NestProjectViewModel doc) || this.nestCts != null)
      {
        return;
      }

      if (string.IsNullOrEmpty(doc.FilePath) || !System.IO.File.Exists(doc.FilePath))
      {
        ViewModel.MessageService.DisplayMessageBox(Localization.Get("Msg_RevertNoFile"), Localization.Get("Menu_Revert"), DeepNestLib.MessageBoxIcon.Information);
        return;
      }

      if (ViewModel.MessageService.DisplayOkCancel(
        Localization.Format("Msg_RevertConfirm", System.IO.Path.GetFileName(doc.FilePath)),
        Localization.Get("Menu_Revert"),
        DeepNestLib.MessageBoxIcon.Question) != DeepNestLib.MessageBoxResult.OK)
      {
        return;
      }

      ViewModel.OnLoadNestProject(doc.FilePath);
    }

    /// <summary>
    /// File → Save with drawings: the project and a folder "name files" beside it holding a copy of every
    /// drawing it reads, to carry the job to another PC. The open project keeps its own paths and file.
    /// </summary>
    private void OnSaveWithDrawings(object sender, RoutedEventArgs e)
    {
      if (!(ViewModel.ActiveDocument is NestProjectViewModel doc) || doc.ProjectInfo == null)
      {
        return;
      }

      string current = doc.FilePath;
      string folder = string.IsNullOrEmpty(current) ? null : System.IO.Path.GetDirectoryName(current);
      var dialog = new Microsoft.Win32.SaveFileDialog
      {
        Filter = ProjectInfo.FileDialogFilter,
        FileName = string.IsNullOrEmpty(current) ? "job.dnest" : System.IO.Path.GetFileName(current),
        InitialDirectory = DeepNestSharp.Ui.Services.FileIoService.UsableFolder(folder),
        Title = Localization.Get("Menu_SaveWithDrawings"),
      };
      if (!DeepNestSharp.Ui.Services.FileIoService.Show(dialog))
      {
        return;
      }

      try
      {
        string target = dialog.FileName;
        string files = System.IO.Path.Combine(
          System.IO.Path.GetDirectoryName(target),
          System.IO.Path.GetFileNameWithoutExtension(target) + Localization.Get("Pack_FolderSuffix"));
        var config = ViewModel.SvgNestConfigViewModel.SvgNestConfig;
        string json = doc.ProjectInfo.ToJson();
        var copy = ProjectInfo.FromJson(config, json);
        copy.CommonLineGapInches = doc.ProjectInfo.CommonLineGapInches;
        copy.CommonLineMaxClusterParts = doc.ProjectInfo.CommonLineMaxClusterParts;
        var (copied, missing) = ProjectPaths.Pack(copy, files);
        System.IO.File.WriteAllText(target, copy.ToJson());

        string message = Localization.Format("Msg_SavedWithDrawings", target, copied, files);
        if (missing.Count > 0)
        {
          message += "\n\n" + Localization.Format("Msg_PackMissing", missing.Count) + "\n" + string.Join("\n", missing.Take(10));
        }

        ViewModel.MessageService.DisplayMessageBox(
          message,
          Localization.Get("Menu_SaveWithDrawings"),
          missing.Count > 0 ? DeepNestLib.MessageBoxIcon.Warning : DeepNestLib.MessageBoxIcon.Information);
      }
      catch (Exception ex)
      {
        ViewModel.MessageService.DisplayMessageBox(ex.GetBaseException().Message, Localization.Get("Menu_SaveWithDrawings"), DeepNestLib.MessageBoxIcon.Warning);
      }
    }
  }
}
