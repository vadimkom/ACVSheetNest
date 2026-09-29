namespace DeepNestSharp.Ui.Views
{
  using System;
  using System.Collections.Generic;
  using System.Linq;
  using System.Threading.Tasks;
  using System.Windows;
  using System.Windows.Controls;
  using System.Windows.Input;
  using DeepNestLib.NestProject;
  using DeepNestLib.Placement;
  using DeepNestSharp.Domain;
  using DeepNestSharp.Domain.Models;
  using DeepNestSharp.Domain.ViewModels;
  using DeepNestSharp.RasterNest;
  using Localization = DeepNestSharp.Ui.Localization;

  public partial class MainWindow : Window
  {
    public enum WorkflowMode
    {
      Parts = 0,
      Sheets = 1,
      Nesting = 2,
      Layout = 3
    }

    public class NestRunHistoryItem
    {
      public int RunNumber { get; set; }

      /// <summary>The number, with a star on the best finished nest in the list.</summary>
      public string RunLabel => this.IsBest ? $"★ {this.RunNumber}" : this.RunNumber.ToString(System.Globalization.CultureInfo.InvariantCulture);

      public bool IsBest { get; set; }

      public INestResult Result { get; set; }
      public string UtilizationText { get; set; }
      public string PlacedText { get; set; }
      public string SheetsText { get; set; }
      public string TimeText { get; set; }
    }

    public class ReportSheetRow
    {
      public int SheetNumber { get; set; }
      public string SizeText { get; set; }
      public int RepeatCount { get; set; }
      public int PlacedCount { get; set; }
      public string UtilizationText { get; set; }
    }

    public class ReportPartRow
    {
      public int PositionNumber { get; set; }
      public System.Windows.Media.Brush ColorBrush { get; set; }
      public string Name { get; set; }
      public string SizeText { get; set; }
      public int QtyTotal { get; set; }
      public string AreaText { get; set; }
    }

    private WorkflowMode currentMode = WorkflowMode.Parts;
    private bool settingMode; // SetCurrentMode is checking the step buttons itself
    private readonly System.Collections.ObjectModel.ObservableCollection<NestRunHistoryItem> nestRuns = new();
    private int runSeedOffset = 0;

    // Sheets the LAST nest deducted from the stock, by size — Clear Result gives them back.
    private Dictionary<(int W, int H), int>? lastNestConsumed;

    // Set once the window is closed, so an async op that finishes afterwards
    // doesn't touch a dead window (would throw "set Owner on a closed Window").
    private bool isClosed;
    private string updateUrl;
    private System.Windows.Threading.DispatcherTimer autosaveTimer;
    private System.Threading.CancellationTokenSource nestCts;
    private System.Diagnostics.Stopwatch nestStopwatch;   // elapsed time while a nest runs (drives the live clock)
    private System.Windows.Threading.DispatcherTimer nestTimer; // ticks the elapsed clock ~2×/s during a nest
    private bool unitsMm = true; // drawing units are millimeters (SessionState.UnitsMm); millimeters by default in Europe
    private List<(int W, int H)> userSheetPresets = new List<(int W, int H)>(); // "My sheets" (SessionState.SheetPresets)
    private bool autosaveEnabled = true;
    private int autosaveMinutes = 5;
    // Per-user defaults for the NEXT job. The process-wide config is temporarily overwritten whenever
    // a document becomes active, so it cannot also be the source of truth for session preferences.
    private double sessionCommonLineGapInches = CommonLineDefaults.DefaultGapInches;
    private int sessionCommonLineMaxClusterParts = CommonLineDefaults.DefaultMaxClusterParts;

    private DeepNestSharp.RasterNest.NestEffort nestEffort = DeepNestSharp.RasterNest.NestEffortDefaults.Default; // how hard the nester searches (SessionState)
    private bool preferRectOffcut; // cut the last sheet's leftover off as a rectangular offcut (SessionState)
    private int offcutDirection; // 0 = end, 1 = side, 2 = both (SessionState.OffcutDirection)
    private double offcutSpacing = -1; // gap parts→offcut cut line; -1 = default to part spacing (SessionState)
    private double offcutMinWidth = -1; // narrowest strip worth cutting; <=0 = automatic 5% rule (SessionState)
    private double offcutMinX = -1; // the remnant size window, the remnant cutoff (SessionState)
    private double offcutMinY = -1;
    private double offcutMaxX = -1;
    private double offcutMaxY = -1;
    private double offcutEdgeOverlap = -1; // how far the cut runs past the sheet edge; <=0 = flush

    public MainWindow(IMainViewModel viewModel)
    {
      InitializeComponent();
      this.DataContext = viewModel;
      this.runsListView.ItemsSource = this.nestRuns;
      viewModel.AboutDialogService = new AboutDialogService(() => new AboutDialog());
      this.Loaded += MainWindow_Loaded;
      this.Closing += MainWindow_Closing;
      this.Closed += (s, e) => this.isClosed = true;
      viewModel.ActiveDocumentChanged += MainWindow_ActiveDocumentChanged;

      // Autosave (auto-save workspace): snapshot the dirty project periodically; a crash then
      // offers recovery on the next start (clean saves/closes clear the snapshot). Enable/interval
      // live in Settings > Application Settings and the session restores them on load.
      this.autosaveTimer = new System.Windows.Threading.DispatcherTimer { Interval = System.TimeSpan.FromMinutes(5) };
      this.autosaveTimer.Tick += this.OnAutosaveTick;
      this.autosaveTimer.Start();
    }

    public IMainViewModel ViewModel => (IMainViewModel)DataContext;

    // US stock sheet sizes most used in sheet-metal fabrication (mill/service-center standards),
    // width = the LONG side (matches the machine bed orientation; the nester grows the pack along
    // the long axis so the remnant is a full short-dimension strip). Grouped by the SHORT side
    // (36 / 48 / 60 / 72), then by length — the menu draws a separator between groups.
    private static readonly SheetPreset[] SheetPresets =
    {
      new SheetPreset("96 × 36 in   (8 × 3 ft)", 96, 36),
      new SheetPreset("120 × 36 in   (10 × 3 ft)", 120, 36),
      new SheetPreset("96 × 48 in   (8 × 4 ft)", 96, 48),
      new SheetPreset("120 × 48 in   (10 × 4 ft)", 120, 48),
      new SheetPreset("144 × 48 in   (12 × 4 ft)", 144, 48),
      new SheetPreset("96 × 60 in   (8 × 5 ft)", 96, 60),
      new SheetPreset("120 × 60 in   (10 × 5 ft)", 120, 60),
      new SheetPreset("144 × 60 in   (12 × 5 ft)", 144, 60),
      new SheetPreset("120 × 72 in   (10 × 6 ft)", 120, 72),
      new SheetPreset("144 × 72 in   (12 × 6 ft)", 144, 72),
    };

    // The metric sizes the shop actually stocks (mm), width = the LONG side. Anything else is typed in under
    // Custom size, and can be remembered there as one of "My sheets".
    private static readonly SheetPreset[] SheetPresetsMm =
    {
      new SheetPreset("2000 × 1000 mm", 2000, 1000),
      new SheetPreset("2500 × 1250 mm", 2500, 1250),
      new SheetPreset("1250 × 500 mm", 1250, 500),
    };

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
      // A part clicked on either drawing says what and where it is in the status line.
      void ShowPart(string text)
      {
        this.statusPartInfo.Text = text ?? string.Empty;
        this.statusPartInfo.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
      }

      this.dxfViewer.PartInspected += ShowPart;
      this.nestDxfViewer.PartInspected += ShowPart;

      // Sensible defaults: always use all cores; drop the removed "Squeeze" arrangement.
      var cfg = ViewModel.SvgNestConfigViewModel.SvgNestConfig;
      cfg.UseParallel = true;
      if (cfg.PlacementType == PlacementTypeEnum.Squeeze)
      {
        cfg.PlacementType = PlacementTypeEnum.BoundingBox;
      }

      // CRITICAL for inch parts: the engine's curve/simplify tolerances are in the drawing's units.
      // The 0.72 default assumes the old SVG ~72-units/inch scale; on 1-unit/inch DXFs it's ~72× too
      // big, so the Douglas-Peucker pass (4×tol = 2.88") erases tabs/detail and the nest then ignores
      // real spacing/edge gaps. 0.01" keeps the geometry true so spacing and sheet margin are honoured.
      cfg.CurveTolerance = 0.01;

      // Fire-and-forget update check: lights up the status-bar link if a newer release exists.
      // Silent on every failure (offline shop PCs) and sends nothing — a single anonymous GET.
      _ = this.CheckForUpdatesOnStartupAsync();

      // Part spacing default is 10 (legacy SVG-scale units) — that's TEN INCHES between parts on inch DXFs,
      // which blows up the raster spacing-halo so large it dissolves interlockable concavities (triangles
      // Restore the previous session's sheet edge margin. Sheets are NOT restored: the app starts
      // with an empty Sheets tab because each launch is usually a different job — sheet stock
      // belongs to (and travels with) each saved .dnest project.
      var session = SessionState.Load();
      this.unitsMm = session?.UnitsMm ?? true;
      Localization.CurrentLanguage = Localization.NormalizeLanguage(session?.Language);
      Localization.LanguageChanged -= this.ApplyLocalization;
      Localization.LanguageChanged += this.ApplyLocalization;

      // stop rotating) and leaves huge gaps in every nest. Replace only the EXACT legacy default with a
      // sane 0.25"; any other persisted value is a deliberate user setting and must not be clobbered.
      // A persisted 0 is ALSO normalized to 0.25" (the shop rule: spacing ≥ 2× material thickness, so
      // 0.25 covers up to 1/8" plate): a global default of "every part touches" is never what a laser
      // job wants — true common-line cutting is a deliberate per-part choice in Edit Part.
      if (cfg.Spacing == 10.0 || cfg.Spacing <= 0.0)
      {
        cfg.Spacing = this.unitsMm ? 6.35 : 0.25;
      }
      else if (this.unitsMm && cfg.Spacing == 0.25)
      {
        cfg.Spacing = 6.35;
      }

      (this.sessionCommonLineGapInches, this.sessionCommonLineMaxClusterParts) =
        CommonLinePreferencesFrom(session);
      cfg.CommonLineGapInches = this.sessionCommonLineGapInches;
      cfg.CommonLineMaxClusterParts = this.sessionCommonLineMaxClusterParts;
      if (session != null)
      {
        if (session.SheetEdgeMargin >= 0)
        {
          cfg.SheetSpacing = session.SheetEdgeMargin;
        }
        else if (this.unitsMm && cfg.SheetSpacing == 0.25)
        {
          cfg.SheetSpacing = 6.35;
        }

        if (session.UnitsMm.HasValue)
        {
          this.unitsMm = session.UnitsMm.Value;
        }

        if (session.SheetPresets != null)
        {
          this.userSheetPresets = session.SheetPresets
            .Where(sp => sp.Width > 0 && sp.Height > 0)
            .Select(sp => (sp.Width, sp.Height))
            .ToList();
        }

        this.ApplyUnits();

        // Autosave settings (Settings > Advanced Settings).
        this.ApplyAutosaveSettings(
          session.AutosaveEnabled ?? true,
          session.AutosaveMinutes >= 1 && session.AutosaveMinutes <= 60 ? session.AutosaveMinutes : 5);

        // A Fast stored by a build that overwrote the "never chosen" marker on close says nothing about
        // what the operator wanted, so it is forgotten once and the current default applies. Saving here
        // is what makes it once: the write stamps the settings version, and a Fast picked deliberately
        // after this point is kept.
        if (DeepNestSharp.RasterNest.NestEffortDefaults.NeedsEffortReset(session.NestEffort, session.SettingsVersion))
        {
          session.NestEffort = DeepNestSharp.RasterNest.NestEffortDefaults.Unset;
          session.Save();
        }

        this.nestEffort = DeepNestSharp.RasterNest.NestEffortDefaults.FromPersisted(session.NestEffort);
        this.RefreshNestEffortMenu();
        SparrowNestService.ConcurrencyLimit = System.Math.Clamp(session.NestConcurrency, 0, SparrowNestService.MaxConcurrencyLimit);

        // Global default rotations (plain in-memory config property — resets to 4 without this).
        if (session.DefaultRotations > 0)
        {
          cfg.Rotations = session.DefaultRotations;
        }

        if (session.PreferRectangularOffcut.HasValue)
        {
          this.preferRectOffcut = session.PreferRectangularOffcut.Value;
        }

        if (session.OffcutDirection >= 0 && session.OffcutDirection <= 3)
        {
          this.offcutDirection = session.OffcutDirection;
        }

        if (session.OffcutSpacing >= 0)
        {
          this.offcutSpacing = session.OffcutSpacing;
        }

        if (session.OffcutMinWidth >= 0)
        {
          this.offcutMinWidth = session.OffcutMinWidth;
        }

        // The single minimum came first and applied to both axes. Seed the pair from it so a session that
        // already had one does not silently lose it the first time the new dialog is opened.
        this.offcutMinX = session.OffcutMinX >= 0 ? session.OffcutMinX : this.offcutMinWidth;
        this.offcutMinY = session.OffcutMinY >= 0 ? session.OffcutMinY : this.offcutMinWidth;
        this.offcutMaxX = session.OffcutMaxX;
        this.offcutMaxY = session.OffcutMaxY;
        this.offcutEdgeOverlap = session.OffcutEdgeOverlap;
      }

      // Create the initial document only AFTER restoring the session. ProjectInfo snapshots the
      // geometry-affecting common-line gap at construction time; creating it earlier made a session
      // value such as 0.015" drive the first nest while Save/Autosave still wrote the old 0.010".
      if (ViewModel.ActiveDocument == null && ViewModel.CreateNestProjectCommand.CanExecute(null))
      {
        ViewModel.CreateNestProjectCommand.Execute(null);
      }

      // The config normalization above — and the initial WPF binding pass, which runs AFTER Loaded
      // (Xceed up-downs push their first values) — ping the project's IsDirty. A fresh EMPTY project
      // has no user work, and the title asterisk must mean "YOUR changes are unsaved", so reset the
      // flag once the binding churn settles. The content guard keeps real work (recovered projects,
      // a part added in the first second) dirty.
      this.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ApplicationIdle, new System.Action(() =>
      {
        if (!this.isClosed
            && ViewModel.ActiveDocument is NestProjectViewModel fresh
            && string.IsNullOrEmpty(fresh.FilePath)
            && fresh.ProjectInfo.DetailLoadInfos.Count == 0
            && fresh.ProjectInfo.SheetLoadInfos.Count == 0)
        {
          fresh.IsDirty = false;
        }
      }));

      // Initialize workflow mode to Models/Parts and update counts
      this.SetCurrentMode(WorkflowMode.Parts);
      this.ApplyUnits();
      this.ApplyLocalization();
      this.UpdateSummaryBadges();

      // Crash recovery LAST (after the config/session churn, so the recovered doc's deliberate
      // dirty flag survives): an autosave snapshot only exists after an unclean exit.
      this.OfferAutosaveRecovery();
    }

    /// <summary>Offers to restore the autosaved project left behind by a crashed session.</summary>
    private void OfferAutosaveRecovery()
    {
      try
      {
        if (!Autosave.TryGetPending(out string originalPath, out string savedAt))
        {
          return;
        }

        string name = string.IsNullOrWhiteSpace(originalPath) ? Localization.Get("Msg_RecoverUnsaved") : System.IO.Path.GetFileName(originalPath);
        string when = string.IsNullOrWhiteSpace(savedAt) ? string.Empty : Localization.Format("Msg_RecoverWhen", savedAt);
        var answer = ViewModel.MessageService.DisplayOkCancel(
          Localization.Format("Msg_Recover", name, when),
          Localization.Get("Title_Recovery"),
          DeepNestLib.MessageBoxIcon.Information);
        if (answer == DeepNestLib.MessageBoxResult.OK)
        {
          ViewModel.OnLoadNestProject(Autosave.RecoveryPath);
          if (ViewModel.ActiveDocument is NestProjectViewModel recovered)
          {
            // Point the document back at the REAL file (empty = never saved -> Save As) and keep it
            // dirty: recovered work is unsaved work until the user saves it.
            recovered.FilePath = originalPath ?? string.Empty;
            recovered.IsDirty = true;
          }
        }

        Autosave.Clear();
      }
      catch (Exception ex)
      {
        // Recovery is best-effort and must never block startup — but a failure is worth a local log.
        CrashReporter.Save(ex, "autosave-recovery");
        Autosave.Clear(); // a snapshot that cannot be recovered must not re-prompt forever
      }
    }

    /// <summary>Startup check: light up the status-bar link if a newer release exists; silent otherwise.</summary>
    private async Task CheckForUpdatesOnStartupAsync()
    {
      try
      {
        var result = await UpdateChecker.CheckAsync();
        if (this.isClosed || result == null || !UpdateChecker.IsNewer(result.Value.Latest, UpdateChecker.CurrentVersion))
        {
          return;
        }

        this.ShowUpdateNotice(result.Value.Latest, result.Value.Url);
      }
      catch
      {
        // never disturb startup
      }
    }

    private void ShowUpdateNotice(Version latest, string url)
    {
      this.updateUrl = url;
      this.updateNotice.Text = $"Update available: {latest.ToString(3)}";
      this.updateNotice.Visibility = Visibility.Visible;
    }

    /// <summary>Help menu: check GitHub on demand and report the outcome.</summary>
    private void OnSendFeedback(object sender, RoutedEventArgs e)
    {
      try
      {
        // Feedback lands in the project's GitHub issues — the same single inbox the crash
        // reporter uses; version and OS are pre-filled so users don't have to know them.
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
          FileName = CrashReporter.BuildFeedbackUrl(),
          UseShellExecute = true,
        });
      }
      catch
      {
        ViewModel.MessageService.DisplayMessageBox(
          Localization.Get("Msg_NoBrowserFeedback"),
          Localization.Get("Title_Feedback"),
          DeepNestLib.MessageBoxIcon.Information);
      }
    }

    private async void OnCheckUpdates(object sender, RoutedEventArgs e)
    {
      try
      {
        Mouse.OverrideCursor = System.Windows.Input.Cursors.Wait;
        var result = await UpdateChecker.CheckAsync();
        Mouse.OverrideCursor = null;
        if (this.isClosed)
        {
          return;
        }

        var current = UpdateChecker.CurrentVersion;
        if (result == null)
        {
          ViewModel.MessageService.DisplayMessageBox(
            Localization.Get("Msg_UpdateFailed"),
            Localization.Get("Title_Updates"),
            DeepNestLib.MessageBoxIcon.Information);
          return;
        }

        if (!UpdateChecker.IsNewer(result.Value.Latest, current))
        {
          ViewModel.MessageService.DisplayMessageBox(
            Localization.Format("Msg_UpToDate", current.ToString(3)),
            Localization.Get("Title_Updates"),
            DeepNestLib.MessageBoxIcon.Information);
          return;
        }

        this.ShowUpdateNotice(result.Value.Latest, result.Value.Url);
        var answer = ViewModel.MessageService.DisplayOkCancel(
          Localization.Format("Msg_UpdateAvailable", result.Value.Latest.ToString(3), current.ToString(3)),
          Localization.Get("Title_Updates"),
          DeepNestLib.MessageBoxIcon.Information);
        if (answer == DeepNestLib.MessageBoxResult.OK)
        {
          UpdateChecker.OpenDownloadPage(result.Value.Url);
        }
      }
      catch (Exception ex)
      {
        Mouse.OverrideCursor = null;
        ViewModel.MessageService.DisplayMessageBox(ex.Message, Localization.Get("Title_Updates"), DeepNestLib.MessageBoxIcon.Stop);
      }
    }

    private void OnUpdateNoticeClick(object sender, MouseButtonEventArgs e)
    {
      if (!string.IsNullOrEmpty(this.updateUrl))
      {
        UpdateChecker.OpenDownloadPage(this.updateUrl);
      }
    }

    private void Document_PropertyChangedForMru(object sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
      if (e.PropertyName == nameof(NestProjectViewModel.FilePath)
          && sender is NestProjectViewModel doc
          && !string.IsNullOrWhiteSpace(doc.FilePath))
      {
        SessionState.PushRecentProject(doc.FilePath);
      }

      // A save (IsDirty -> false) means there is nothing left to recover.
      if (e.PropertyName == nameof(NestProjectViewModel.IsDirty)
          && sender is NestProjectViewModel saved
          && !saved.IsDirty)
      {
        Autosave.Clear();
      }
    }

    /// <summary>Every 5 minutes: snapshot the dirty project (with its on-screen nest result) for recovery.</summary>
    private void OnAutosaveTick(object sender, System.EventArgs e)
    {
      try
      {
        if (ViewModel?.ActiveDocument is NestProjectViewModel doc && doc.IsDirty)
        {
          // Same embed Save performs, so a recovery brings the on-screen nest result back too.
          doc.ProjectInfo.LastNestResultJson =
            (ViewModel.NestMonitorViewModel.SelectedItem as NestResult)?.ToJson(false) ?? string.Empty;
          Autosave.Write(doc.TextContent, doc.FilePath);
        }
      }
      catch
      {
        // autosave must never disturb the user
      }
    }

    /// <summary>Rebuilds File > Recent Projects from the session on every open (prunes dead paths).</summary>
    private void OnRecentProjectsOpened(object sender, RoutedEventArgs e)
    {
      this.recentProjectsMenu.Items.Clear();
      var recents = (SessionState.Load()?.RecentProjects ?? new List<string>())
        .Where(System.IO.File.Exists)
        .ToList();
      if (recents.Count == 0)
      {
        this.recentProjectsMenu.Items.Add(new MenuItem { Header = "(empty)", IsEnabled = false });
        return;
      }

      foreach (var path in recents)
      {
        var item = new MenuItem
        {
          Header = System.IO.Path.GetFileName(path),
          ToolTip = path,
        };
        string captured = path;
        item.Click += (_, __) => ViewModel.LoadNestProjectInteractive(captured);
        this.recentProjectsMenu.Items.Add(item);
      }
    }

    /// <summary>Applies the autosave settings to the running timer and remembers them for the session save.</summary>
    private void ApplyAutosaveSettings(bool enabled, int minutes)
    {
      this.autosaveEnabled = enabled;
      this.autosaveMinutes = minutes;
      if (this.autosaveTimer != null)
      {
        this.autosaveTimer.Interval = System.TimeSpan.FromMinutes(minutes);
        this.autosaveTimer.IsEnabled = enabled;
      }
    }

    private void OnAdvancedSettings(object sender, RoutedEventArgs e)
    {
      var cfg = ViewModel.SvgNestConfigViewModel.SvgNestConfig;
      var session = SessionState.Load() ?? new SessionState();
      double previousCommonLineGapInches = cfg.CommonLineGapInches;
      int previousCommonLineMaxClusterParts = cfg.CommonLineMaxClusterParts;
      var dialog = new AdvancedSettingsWindow(
        cfg,
        this.autosaveEnabled,
        this.autosaveMinutes,
        this.unitsMm,
        commonLineGapInches: cfg.CommonLineGapInches,
        effort: this.nestEffort,
        commonLineMaxClusterParts: cfg.CommonLineMaxClusterParts,
        concurrencyLimit: SparrowNestService.ConcurrencyLimit)
      {
        Owner = this,
      };
      if (dialog.ShowDialog() != true)
      {
        return;
      }

      this.ApplyAutosaveSettings(dialog.AutosaveEnabled, dialog.AutosaveMinutes);
      cfg.CommonLineGapInches = dialog.CommonLineGapInches;
      cfg.CommonLineMaxClusterParts = dialog.CommonLineMaxClusterParts;
      // Confirming Advanced Settings is an explicit operator choice: apply it to this document and also
      // remember it for future new jobs. Merely activating/opening a document never writes these fields.
      this.sessionCommonLineGapInches = cfg.CommonLineGapInches;
      this.sessionCommonLineMaxClusterParts = cfg.CommonLineMaxClusterParts;
      if ((System.Math.Abs(previousCommonLineGapInches - cfg.CommonLineGapInches) > 1e-12
            || previousCommonLineMaxClusterParts != cfg.CommonLineMaxClusterParts)
          && ViewModel.ActiveDocument is NestProjectViewModel project)
      {
        // Unlike machine/search preferences, this value changes placed geometry and therefore belongs
        // in the .dnest. Mark the job so Save/Autosave actually writes the new value.
        project.ProjectInfo.CommonLineGapInches = cfg.CommonLineGapInches;
        project.ProjectInfo.CommonLineMaxClusterParts = cfg.CommonLineMaxClusterParts;
        project.IsDirty = true;
      }

      this.unitsMm = dialog.UnitsMm;
      this.ApplyUnits();

      // The settings-backed config values persisted in their setters; the rest goes to the session
      // immediately (load-modify-save keeps the MRU and the other session fields intact).
      session.AutosaveEnabled = this.autosaveEnabled;
      session.AutosaveMinutes = this.autosaveMinutes;
      session.DefaultRotations = cfg.Rotations;
      session.SheetEdgeMargin = System.Math.Max(0, cfg.SheetSpacing);
      session.UnitsMm = this.unitsMm;
      session.CommonLineGapInches = cfg.CommonLineGapInches;
      session.CommonLineMaxClusterParts = cfg.CommonLineMaxClusterParts;
      this.nestEffort = dialog.Effort;
      session.NestEffort = (int)this.nestEffort;
      SparrowNestService.ConcurrencyLimit = dialog.ConcurrencyLimit;
      session.NestConcurrency = dialog.ConcurrencyLimit;
      session.Save();

      // Two places set the same preference, so the toolbar is rebuilt from the field rather than left
      // showing whatever it showed before the dialog was opened.
      this.RefreshNestEffortMenu();
    }

    /// <summary>The arrow half of the NEST split button. Anchored under the button so the menu reads as
    /// belonging to it rather than appearing at the pointer.</summary>
    private void OnNestEffortMenu(object sender, RoutedEventArgs e)
    {
      if (this.nestEffortButton.ContextMenu is System.Windows.Controls.ContextMenu menu)
      {
        menu.PlacementTarget = this.nestEffortButton;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        menu.IsOpen = true;
      }
    }

    private void OnNestEffortChosen(object sender, RoutedEventArgs e)
    {
      if (sender is System.Windows.Controls.MenuItem item
          && item.Tag is string tag
          && int.TryParse(tag, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int chosen))
      {
        this.nestEffort = DeepNestSharp.RasterNest.NestEffortDefaults.FromPersisted(chosen);
        var session = SessionState.Load() ?? new SessionState();
        session.NestEffort = (int)this.nestEffort;
        session.Save();
      }

      // Unconditionally: a checkable MenuItem toggles itself on click, so even the item that was already
      // ticked has to be put back the way the field says, or clicking the current effort unticks it.
      this.RefreshNestEffortMenu();
    }

    /// <summary>
    /// Rebuilds the ticks and the tooltip from <see cref="nestEffort"/>, never from whichever control was
    /// clicked, so the toolbar and Advanced Settings cannot drift apart.
    /// </summary>
    private void RefreshNestEffortMenu()
    {
      if (this.effortFastItem == null)
      {
        return; // called before the toolbar exists
      }

      this.effortFastItem.Header = Localization.Get("Effort_Fast");
      this.effortNormalItem.Header = Localization.Get("Effort_Normal");
      this.effortBestItem.Header = Localization.Get("Effort_Best");

      this.effortFastItem.IsChecked = this.nestEffort == DeepNestSharp.RasterNest.NestEffort.Fast;
      this.effortNormalItem.IsChecked = this.nestEffort == DeepNestSharp.RasterNest.NestEffort.Normal;
      this.effortBestItem.IsChecked = this.nestEffort == DeepNestSharp.RasterNest.NestEffort.Best;

      string name = this.nestEffort switch
      {
        DeepNestSharp.RasterNest.NestEffort.Normal => "Normal",
        DeepNestSharp.RasterNest.NestEffort.Best => "Best",
        _ => "Fast",
      };

      // Says what it actually buys. It reads as "how long you wait", but what it changes is how TIGHTLY
      // the parts end up packed — and on a common-line job that tightness is what decides whether the
      // last part of a row gets to share its cut or sits on its own at the normal gap.
      this.nestEffortButton.ToolTip =
        $"Nesting effort: {name}. It buys a TIGHTER pack, not more parts: the parts that fit are found "
        + "almost at once and the rest of the search squeezes them together. On a common-line job that is "
        + "what lets the last part of a row share its cut instead of standing alone.";
    }

    private void OnOffcutSettings(object sender, RoutedEventArgs e)
    {
      var (limits, spacing, edgeOverlap) = this.EffectiveOffcutLimits();
      var dialog = new OffcutSettingsWindow(this.preferRectOffcut, this.offcutDirection, spacing, limits, edgeOverlap, this.unitsMm) { Owner = this };
      if (dialog.ShowDialog() != true)
      {
        return;
      }

      this.preferRectOffcut = dialog.OffcutEnabled;
      this.offcutDirection = dialog.Direction;
      this.offcutSpacing = dialog.Spacing;
      this.offcutMinX = dialog.Limits.MinX;
      this.offcutMinY = dialog.Limits.MinY;
      this.offcutMaxX = dialog.Limits.MaxX;
      this.offcutMaxY = dialog.Limits.MaxY;
      this.offcutEdgeOverlap = dialog.EdgeOverlap;

      var session = SessionState.Load() ?? new SessionState();
      session.PreferRectangularOffcut = this.preferRectOffcut;
      session.OffcutDirection = this.offcutDirection;
      session.OffcutSpacing = this.offcutSpacing;
      session.OffcutMinX = this.offcutMinX;
      session.OffcutMinY = this.offcutMinY;
      session.OffcutMaxX = this.offcutMaxX;
      session.OffcutMaxY = this.offcutMaxY;
      session.OffcutEdgeOverlap = this.offcutEdgeOverlap;
      session.Save();

      // Reflect the new settings on the on-screen result immediately (overlay only — the packing
      // itself only changes on the next NEST).
      if (this.dxfViewer != null)
      {
        this.dxfViewer.OffcutOptions = this.CurrentOffcutOptions();
        this.dxfViewer.RefreshRender();
      }
    }

    /// <summary>Discards the displayed nest result and returns the sheets it consumed to the stock.</summary>
    private void ClearNestResult()
    {
      bool hadResult = ViewModel.NestMonitorViewModel.SelectedItem != null || !ViewModel.NestMonitorViewModel.TopNestResults.IsEmpty;
      ViewModel.NestMonitorViewModel.Reset();

      // The project no longer carries this result (a save from here on writes it without one, and
      // the session's close-time stock logic must treat any FRESH nest after this as unsaved).
      if (ViewModel.ActiveDocument is NestProjectViewModel clearedDoc)
      {
        clearedDoc.ProjectInfo.LastNestResultJson = string.Empty;
      }

      this.UpdateNestedInfo(null);

      if (hadResult && lastNestConsumed != null && ViewModel.ActiveDocument is NestProjectViewModel doc)
      {
        ReturnStock(doc.ProjectInfo.SheetLoadInfos, lastNestConsumed);
        this.sheetsListView?.Items.Refresh();
      }

      lastNestConsumed = null; // a result can only be given back once
    }

    /// <summary>
    /// A project became active (opened or created): restore the nest result saved inside it, or
    /// clear the previous project's result — the on-screen nest always belongs to the active project.
    /// </summary>
    private void MainWindow_ActiveDocumentChanged(object sender, System.EventArgs e)
    {
      if (!(ViewModel.ActiveDocument is NestProjectViewModel doc))
      {
        return;
      }

      this.nestRuns.Clear();
      this.runSeedOffset = 0;
      this.UpdateSummaryBadges();

      if (IsFreshUnsavedProject(doc))
      {
        ApplyCommonLinePreferencesToNewProject(
          doc.ProjectInfo,
          this.sessionCommonLineGapInches,
          this.sessionCommonLineMaxClusterParts);
      }

      // ProjectInfo keeps this geometry-affecting value per document while the runtime config is shared.
      // Reapply the active tab's snapshot before nesting/export/UI consumers can observe another tab's gap.
      ViewModel.SvgNestConfigViewModel.SvgNestConfig.CommonLineGapInches =
        doc.ProjectInfo.CommonLineGapInches;
      ViewModel.SvgNestConfigViewModel.SvgNestConfig.CommonLineMaxClusterParts =
        doc.ProjectInfo.CommonLineMaxClusterParts;
      this.ApplyUnits();

      // Recent Projects: record opened files now, and saved-for-the-first-time files when their
      // FilePath appears (Save As raises PropertyChanged). Subscription is made idempotent.
      if (!string.IsNullOrWhiteSpace(doc.FilePath))
      {
        SessionState.PushRecentProject(doc.FilePath);
      }

      doc.PropertyChanged -= this.Document_PropertyChangedForMru;
      doc.PropertyChanged += this.Document_PropertyChangedForMru;

      var monitor = ViewModel.NestMonitorViewModel;
      string json = doc.ProjectInfo.LastNestResultJson;
      if (string.IsNullOrWhiteSpace(json))
      {
        monitor.Reset();
        lastNestConsumed = null;
        if (this.nestDxfViewer != null) this.nestDxfViewer.Result = null;
        this.UpdateNestedInfo(null);
        return;
      }

      try
      {
        var result = NestResult.FromJson(json);
        if (result == null || result.UsedSheets.Count == 0)
        {
          monitor.Reset();
          lastNestConsumed = null;
          if (this.nestDxfViewer != null) this.nestDxfViewer.Result = null;
          this.UpdateNestedInfo(null);
          return;
        }

        // Same clearance setup the nest itself hands the viewer, so manual editing of the restored
        // result enforces the same rules.
        if (this.dxfViewer != null)
        {
          this.dxfViewer.DefaultPartSpacing = System.Math.Max(0, ViewModel.SvgNestConfigViewModel.SvgNestConfig.Spacing);
          this.dxfViewer.SheetEdgeMargin = System.Math.Max(0, ViewModel.SvgNestConfigViewModel.SvgNestConfig.SheetSpacing);
          this.dxfViewer.PartSpacings = doc.ProjectInfo.DetailLoadInfos
            .Where(o => !string.IsNullOrWhiteSpace(o.Path))
            .GroupBy(o => o.Path, System.StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
              g => g.Key,
              g => g.Min(o => o.Spacing >= 0 ? o.Spacing : System.Math.Max(0, ViewModel.SvgNestConfigViewModel.SvgNestConfig.Spacing)),
              System.StringComparer.OrdinalIgnoreCase);
        }

        this.ApplyPartColours(doc.ProjectInfo.DetailLoadInfos);

        monitor.TopNestResults.SetSingleResult(result);
        monitor.SelectedItem = result;

        if (this.nestDxfViewer != null)
        {
          this.nestDxfViewer.DefaultPartSpacing = this.dxfViewer?.DefaultPartSpacing ?? 0;
          this.nestDxfViewer.SheetEdgeMargin = this.dxfViewer?.SheetEdgeMargin ?? 0;
          this.nestDxfViewer.PartSpacings = this.dxfViewer?.PartSpacings;
          this.nestDxfViewer.Result = result;
        }

        var runItem = new NestRunHistoryItem
        {
          RunNumber = 1,
          Result = result,
          UtilizationText = FormattableString.Invariant($"{result.MaterialUtilization * 100:0.#}%"),
          PlacedText = FormattableString.Invariant($"{result.TotalPlacedCount} / {result.TotalPartsCount}"),
          SheetsText = FormattableString.Invariant($"{result.UsedSheets.Count}"),
          TimeText = Localization.Get("Run_Saved")
        };
        this.nestRuns.Add(runItem);
        this.runsListView.SelectedItem = runItem;

        // The project file stores the stock AS DEDUCTED by this nest, so Clear Result must be able
        // to give those sheets back — rebuild the consumed-by-size record from the result itself.
        var restoredConsumed = new Dictionary<(int W, int H), int>();
        foreach (var sp in result.UsedSheets)
        {
          var key = ((int)System.Math.Round(sp.Sheet.WidthCalculated), (int)System.Math.Round(sp.Sheet.HeightCalculated));
          restoredConsumed[key] = restoredConsumed.TryGetValue(key, out int n) ? n + 1 : 1;
        }

        lastNestConsumed = restoredConsumed;
        this.UpdateNestedInfo(result);
      }
      catch (System.Exception)
      {
        // A corrupt/incompatible embedded result must never block opening the project itself.
        monitor.Reset();
        lastNestConsumed = null;
        if (this.nestDxfViewer != null) this.nestDxfViewer.Result = null;
        this.UpdateNestedInfo(null);
      }
      finally
      {
        this.UpdateSummaryBadges();
      }
    }

    /// <summary>
    /// Feeds the per-row "Available X/Y" badges in the Parts and Sheets panels: sets each row's
    /// NestedCount (sheets consumed / parts placed by the on-screen result; 0 = no result) and the
    /// wrappers render availability from it. The badges are ALWAYS visible — with no result every
    /// row simply reads N/N.
    /// </summary>
    private void UpdateNestedInfo(INestResult result)
    {
      if (!(ViewModel.ActiveDocument is NestProjectViewModel doc))
      {
        return;
      }

      var sheetRows = doc.ProjectInfo.SheetLoadInfos.OfType<ObservableSheetLoadInfo>().ToList();
      var partRows = doc.ProjectInfo.DetailLoadInfos.OfType<ObservableDetailLoadInfo>().ToList();

      if (result == null)
      {
        sheetRows.ForEach(s => s.NestedCount = 0);
        partRows.ForEach(p => p.NestedCount = 0);
        return;
      }

      // Sheets: sheets of this size the nest consumed (recorded at deduction time); the wrapper
      // renders availability as Quantity / (Quantity + NestedCount).
      foreach (var s in sheetRows)
      {
        int used = 0;
        if (lastNestConsumed != null)
        {
          lastNestConsumed.TryGetValue((s.Width, s.Height), out used);
        }

        s.NestedCount = used;
      }

      // Parts: placements carry the name the parser stored (full path for DXF, file name for SVG).
      // Hand each name's count out to matching rows in listed order, capped at each row's own
      // demand — the same matching the mixed-stock consumption uses, so a DXF listed twice (e.g.
      // once common-line, once spaced) splits sensibly instead of double-counting.
      static string SafeFileName(string path)
      {
        try
        {
          return System.IO.Path.GetFileName(path);
        }
        catch (System.ArgumentException)
        {
          return path;
        }
      }

      var assigned = new Dictionary<ObservableDetailLoadInfo, int>();
      partRows.ForEach(p => assigned[p] = 0);
      var placedByName = result.UsedSheets
        .SelectMany(sp => sp.PartPlacements)
        .GroupBy(p => p.Part.Name ?? string.Empty, System.StringComparer.OrdinalIgnoreCase);
      foreach (var group in placedByName)
      {
        int left = group.Count();
        foreach (int pass in new[] { 0, 1 })
        {
          foreach (var row in partRows)
          {
            if (left == 0)
            {
              break;
            }

            bool exact = string.Equals(row.Path, group.Key, System.StringComparison.OrdinalIgnoreCase);
            bool match = pass == 0 ? exact : !exact && string.Equals(SafeFileName(row.Path), group.Key, System.StringComparison.OrdinalIgnoreCase);
            if (!match)
            {
              continue;
            }

            int take = System.Math.Min(System.Math.Max(0, row.Quantity + row.Extra + row.MirrorQuantity - assigned[row]), left);
            assigned[row] += take;
            left -= take;
          }

          if (left == 0)
          {
            break;
          }
        }
      }

      partRows.ForEach(p => p.NestedCount = assigned[p]);
    }

    private void MainWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
    {
      // Closing with a nest result on screen: offer to save the project first. Yes runs the same
      // Save as the File menu (Save As dialog when the project has no file yet — declining THAT
      // dialog aborts the close so no work is silently lost); No closes without saving; Cancel stays.
      if (ViewModel.NestMonitorViewModel.SelectedItem != null && ViewModel.ActiveDocument != null)
      {
        var answer = MessageBox.Show(
          this,
          Localization.Get("Msg_SaveBeforeClosing"),
          "SheetNest",
          MessageBoxButton.YesNoCancel,
          MessageBoxImage.Question);
        if (answer == MessageBoxResult.Cancel)
        {
          e.Cancel = true;
          return;
        }

        if (answer == MessageBoxResult.Yes)
        {
          // Always via the Save As dialog (pre-filled with the project's own name/folder for saved
          // projects — plain Enter overwrites, typing renames). Cancelling the dialog aborts the
          // close so no work is silently lost.
          if (!ViewModel.Save(ViewModel.ActiveDocument, true))
          {
            e.Cancel = true;
            return;
          }
        }
      }

      // A close that reaches this point is CLEAN (saved, discarded by choice, or nothing dirty) —
      // the autosave snapshot must not trigger a bogus recovery prompt on the next start.
      Autosave.Clear();

      // Sheet stock is deliberately NOT part of the session anymore — the app starts empty and each
      // saved .dnest project carries its own stock. Update the existing session instead of replacing it:
      // replacing it used to erase every preference not named here, including NestEffort.
      var session = SessionState.Load() ?? new SessionState();
      session.CaptureApplicationClose(
        System.Math.Max(0, ViewModel.SvgNestConfigViewModel.SvgNestConfig.SheetSpacing),
        this.unitsMm,
        this.userSheetPresets.Select(p => new SessionSheet { Width = p.W, Height = p.H }),
        this.autosaveEnabled,
        this.autosaveMinutes,
        ViewModel.SvgNestConfigViewModel.SvgNestConfig.Rotations,
        this.sessionCommonLineGapInches,
        this.sessionCommonLineMaxClusterParts);
      session.Save();
    }

    internal static (double GapInches, int MaxClusterParts) CommonLinePreferencesFrom(SessionState? session)
      => session == null
        ? (CommonLineDefaults.DefaultGapInches, CommonLineDefaults.DefaultMaxClusterParts)
        : (session.EffectiveCommonLineGapInches, session.EffectiveCommonLineMaxClusterParts);

    internal static void ApplyCommonLinePreferencesToNewProject(
      IProjectInfo project,
      double gapInches,
      int maxClusterParts)
    {
      if (project == null)
      {
        return;
      }

      // ProjectInfo owns normalization, keeping this handoff identical to deserialization and the dialog.
      project.CommonLineGapInches = gapInches;
      project.CommonLineMaxClusterParts = maxClusterParts;
    }

    private static bool IsFreshUnsavedProject(NestProjectViewModel document)
      => document != null
        && string.IsNullOrWhiteSpace(document.FilePath)
        && document.ProjectInfo.DetailLoadInfos.Count == 0
        && document.ProjectInfo.SheetLoadInfos.Count == 0
        && string.IsNullOrWhiteSpace(document.ProjectInfo.LastNestResultJson);

    /// <summary>Reflects the drawing units (in/mm) in every unit-labelled UI element.</summary>
    private void ApplyUnits()
    {
      string u = this.unitsMm ? (Localization.CurrentLanguage == Localization.LangEnglish ? "mm" : "мм") : "in";
      string uArea = this.unitsMm ? (Localization.CurrentLanguage == Localization.LangEnglish ? "mm²" : "мм²") : "in²";
      if (this.marginUnitsLabel != null)
      {
        this.marginUnitsLabel.Text = u;
      }

      if (this.sheetEdgeMarginBox != null)
      {
        this.sheetEdgeMarginBox.Increment = this.unitsMm ? 0.5 : 0.05;
        this.sheetEdgeMarginBox.FormatString = this.unitsMm ? "0.##" : "0.###";
      }

      if (this.colHeaderDimensions != null)
      {
        this.colHeaderDimensions.Header = $"{Localization.Get("Col_Dimensions")}, {u}";
      }

      if (this.colHeaderArea != null)
      {
        this.colHeaderArea.Header = $"{Localization.Get("Col_NetArea")}, {uArea}";
      }

      if (this.colHeaderSheetDimensions != null)
      {
        this.colHeaderSheetDimensions.Header = $"{Localization.Get("Col_SheetSize")}, {u}";
      }

      if (this.dxfViewer != null)
      {
        this.dxfViewer.UnitsMm = this.unitsMm;
        this.dxfViewer.CommonLineGap = ViewModel.SvgNestConfigViewModel.SvgNestConfig.CommonLineGapInches
          * (this.unitsMm ? CommonLineDefaults.InchesToMillimeters : 1.0);
      }

      if (this.nestDxfViewer != null)
      {
        this.nestDxfViewer.UnitsMm = this.unitsMm;
        this.nestDxfViewer.CommonLineGap = ViewModel.SvgNestConfigViewModel.SvgNestConfig.CommonLineGapInches
          * (this.unitsMm ? CommonLineDefaults.InchesToMillimeters : 1.0);
      }

      if (this.reportPreviewGrid?.Visibility == Visibility.Visible)
      {
        this.UpdateReportPreview();
      }
    }

    /// <summary>Applies current localization (ru/bg/en) to all UI texts in the window.</summary>
    private void ApplyLocalization()
    {
      // Menu items
      if (this.menuFile != null) this.menuFile.Header = Localization.Get("Menu_File");
      if (this.menuNewProject != null) this.menuNewProject.Header = Localization.Get("Menu_NewProject");
      if (this.menuOpenProject != null) this.menuOpenProject.Header = Localization.Get("Menu_OpenProject");
      if (this.recentProjectsMenu != null) this.recentProjectsMenu.Header = Localization.Get("Menu_RecentProjects");
      if (this.recentEmptyItem != null) this.recentEmptyItem.Header = Localization.Get("Menu_RecentEmpty");
      if (this.menuSave != null) this.menuSave.Header = Localization.Get("Menu_Save");
      if (this.menuSaveAs != null) this.menuSaveAs.Header = Localization.Get("Menu_SaveAs");
      if (this.menuExit != null) this.menuExit.Header = Localization.Get("Menu_Exit");
      if (this.menuSettings != null) this.menuSettings.Header = Localization.Get("Menu_Settings");
      if (this.menuOffcut != null) this.menuOffcut.Header = Localization.Get("Menu_Offcut");
      if (this.menuAdvancedSettings != null) this.menuAdvancedSettings.Header = Localization.Get("Menu_AdvancedSettings");
      if (this.menuLanguage != null) this.menuLanguage.Header = Localization.Get("Menu_Language");
      if (this.menuHelp != null) this.menuHelp.Header = Localization.Get("Menu_Help");
      if (this.menuCheckUpdates != null) this.menuCheckUpdates.Header = Localization.Get("Menu_CheckUpdates");
      if (this.menuSendFeedback != null) this.menuSendFeedback.Header = Localization.Get("Menu_SendFeedback");
      if (this.menuAbout != null) this.menuAbout.Header = Localization.Get("Menu_About");

      // Language checkmarks
      if (this.langRuItem != null) this.langRuItem.IsChecked = Localization.CurrentLanguage == Localization.LangRussian;
      if (this.langBgItem != null) this.langBgItem.IsChecked = Localization.CurrentLanguage == Localization.LangBulgarian;
      if (this.langEnItem != null) this.langEnItem.IsChecked = Localization.CurrentLanguage == Localization.LangEnglish;

      // Toolbar Buttons
      if (this.btnNew != null) this.btnNew.Content = Localization.Get("Btn_New");
      if (this.btnOpen != null) this.btnOpen.Content = Localization.Get("Btn_Open");
      if (this.btnSave != null) this.btnSave.Content = Localization.Get("Btn_Save");

      // Workflow Mode Tabs
      if (this.navPartsBtn != null) this.navPartsBtn.Content = Localization.Get("Tab_Parts");
      if (this.navSheetsBtn != null) this.navSheetsBtn.Content = Localization.Get("Tab_Sheets");
      if (this.navNestingBtn != null) this.navNestingBtn.Content = Localization.Get("Tab_Nesting");
      if (this.navLayoutBtn != null) this.navLayoutBtn.Content = Localization.Get("Tab_Layout");

      // Screen 1: Parts
      if (this.btnAddPart != null) this.btnAddPart.Content = Localization.Get("Btn_AddDxf");
      if (this.btnDeletePart != null) this.btnDeletePart.Content = Localization.Get("Btn_DeletePart");
      if (this.btnClearAllParts != null) this.btnClearAllParts.Content = Localization.Get("Btn_ClearAll");
      if (this.btnGoToSheets != null) this.btnGoToSheets.Content = Localization.Get("Btn_NextSheets");
      if (this.partsHintText != null) this.partsHintText.Text = Localization.Get("Hint_DragDrop");
      if (this.partsEmptyTitle != null) this.partsEmptyTitle.Text = Localization.CurrentLanguage == Localization.LangBulgarian
        ? "В проекта все още няма детайли"
        : (Localization.CurrentLanguage == Localization.LangEnglish ? "No parts in project yet" : "В проекте пока нет ни одной детали");
      if (this.partsEmptySub != null) this.partsEmptySub.Text = Localization.Get("Hint_DragDrop");
      if (this.btnAddPartEmpty != null) this.btnAddPartEmpty.Content = Localization.Get("Btn_AddDxf");
      if (this.colHeaderPartPreview != null) this.colHeaderPartPreview.Header = Localization.Get("Col_Preview");
      if (this.colHeaderPartName != null) this.colHeaderPartName.Header = Localization.Get("Col_PartName");
      if (this.colHeaderPartQty != null) this.colHeaderPartQty.Header = Localization.Get("Col_Quantity");
      if (this.colHeaderColor != null) this.colHeaderColor.Header = Localization.Get("Col_Color");
      if (this.colHeaderPriority != null) this.colHeaderPriority.Header = Localization.Get("Col_Priority");
      if (this.colHeaderPartStatus != null) this.colHeaderPartStatus.Header = Localization.Get("Col_Status");
      if (this.colHeaderPartActions != null) this.colHeaderPartActions.Header = Localization.Get("Col_Actions");
      if (this.lblMultiplier != null) this.lblMultiplier.Text = Localization.Get("Lbl_Multiplier");
      if (this.btnApplyMultiplier != null)
      {
        this.btnApplyMultiplier.Content = Localization.Get("Btn_Multiply");
        this.btnApplyMultiplier.ToolTip = Localization.Get("Tip_Multiply");
      }
      if (this.partsMultiplierUpDown != null) this.partsMultiplierUpDown.ToolTip = Localization.Get("Tip_Multiplier");
      if (this.btnClearRuns != null)
      {
        this.btnClearRuns.Content = Localization.Get("Btn_ClearRuns");
        this.btnClearRuns.ToolTip = Localization.Get("Tip_ClearRuns");
      }

      // Screen 2: Sheets
      if (this.btnBackToParts != null) this.btnBackToParts.Content = Localization.Get("Btn_BackToParts");
      if (this.btnGoToNesting != null) this.btnGoToNesting.Content = Localization.Get("Btn_NextNesting");
      if (this.btnAddSheet != null) this.btnAddSheet.Content = Localization.Get("Btn_AddSheet");
      if (this.btnDeleteSheet != null) this.btnDeleteSheet.Content = Localization.Get("Btn_DeleteSheet");
      if (this.lblSheetEdgeMargin != null) this.lblSheetEdgeMargin.Text = Localization.Get("Lbl_SheetEdgeMargin");
      if (this.sheetsEmptyTitle != null) this.sheetsEmptyTitle.Text = Localization.CurrentLanguage == Localization.LangBulgarian
        ? "Няма добавени листове"
        : (Localization.CurrentLanguage == Localization.LangEnglish ? "No sheets added" : "Листы материала не добавлены");
      if (this.btnAddSheetEmpty != null) this.btnAddSheetEmpty.Content = Localization.Get("Btn_AddSheet");
      if (this.colHeaderSheetQty != null) this.colHeaderSheetQty.Header = Localization.Get("Col_Quantity");
      if (this.colHeaderSheetUnlimited != null) this.colHeaderSheetUnlimited.Header = Localization.Get("Col_Unlimited");
      if (this.colHeaderSheetPriority != null) this.colHeaderSheetPriority.Header = Localization.Get("Col_SheetPriority");
      if (this.colHeaderSheetStatus != null) this.colHeaderSheetStatus.Header = Localization.Get("Col_AvailableUsed");
      if (this.colHeaderSheetActions != null) this.colHeaderSheetActions.Header = Localization.Get("Col_Actions");

      // Screen 3: Nesting
      if (this.btnStartNest != null) this.btnStartNest.Content = Localization.Get("Btn_StartNest");
      if (this.cancelNestButton != null) this.cancelNestButton.Content = Localization.Get("Btn_StopNest");
      if (this.btnOpenLayoutFromNesting != null) this.btnOpenLayoutFromNesting.Content = Localization.Get("Btn_GoToLayout");
      if (this.runsListTitle != null) this.runsListTitle.Text = Localization.Get("Lbl_RunsList");
      if (this.runsListHint != null) this.runsListHint.Text = Localization.Get("Hint_RunsList");
      if (this.colHeaderRunNum != null) this.colHeaderRunNum.Header = Localization.Get("Col_RunNum");
      if (this.colHeaderRunUtil != null) this.colHeaderRunUtil.Header = Localization.Get("Col_RunUtil");
      if (this.colHeaderRunPlaced != null) this.colHeaderRunPlaced.Header = Localization.Get("Col_RunPlaced");
      if (this.colHeaderRunSheets != null) this.colHeaderRunSheets.Header = Localization.Get("Col_RunSheets");
      if (this.colHeaderRunTime != null) this.colHeaderRunTime.Header = Localization.Get("Col_RunTime");
      this.RefreshNestEffortMenu();

      // Screen 4: Layout & Report
      if (this.btnBackToNesting != null) this.btnBackToNesting.Content = Localization.Get("Btn_BackToNesting");
      if (this.viewTypeDxfRadio != null) this.viewTypeDxfRadio.Content = Localization.Get("Radio_DxfView");
      if (this.viewTypeReportRadio != null) this.viewTypeReportRadio.Content = Localization.Get("Radio_ReportView");
      if (this.btnExportDxf != null) this.btnExportDxf.Content = Localization.Get("Btn_ExportDxf");
      if (this.btnExportPdf != null) this.btnExportPdf.Content = Localization.Get("Btn_ExportPdf");
      if (this.btnPrintReport != null) this.btnPrintReport.Content = Localization.Get("Btn_PrintReport");
      if (this.btnReportZoomFit != null) this.btnReportZoomFit.Content = Localization.Get("Report_PreviewToolbar_ZoomFit");
      if (this.reportEmptyTitle != null) this.reportEmptyTitle.Text = Localization.Get("Report_EmptyTitle");
      if (this.reportEmptySub != null) this.reportEmptySub.Text = Localization.Get("Report_EmptySub");
      this.UpdateReportPageIndicator();

      // Status Bar
      if (this.statusLabelUtilization != null) this.statusLabelUtilization.Text = Localization.Get("Status_Utilization");
      if (this.statusLabelSheets != null) this.statusLabelSheets.Text = Localization.Get("Status_Sheets");
      if (this.statusLabelPlaced != null) this.statusLabelPlaced.Text = Localization.Get("Status_Placed");
      if (this.statusLabelUnplaced != null) this.statusLabelUnplaced.Text = Localization.Get("Status_Unplaced");

      // Update unit labels on columns and summaries
      this.ApplyUnits();
      this.UpdateSummaryBadges();
    }

    private void OnLanguageRuClicked(object sender, RoutedEventArgs e)
    {
      this.SetLanguage(Localization.LangRussian);
    }

    private void OnLanguageBgClicked(object sender, RoutedEventArgs e)
    {
      this.SetLanguage(Localization.LangBulgarian);
    }

    private void OnLanguageEnClicked(object sender, RoutedEventArgs e)
    {
      this.SetLanguage(Localization.LangEnglish);
    }

    private void SetLanguage(string lang)
    {
      Localization.CurrentLanguage = lang;
      var session = SessionState.Load() ?? new SessionState();
      session.Language = lang;
      session.Save();
      this.ApplyLocalization();
    }

    /// <summary>Persists the user's sheet presets right away (load-modify-save, like the MRU).</summary>
    private void SaveUserSheetPresets()
    {
      var session = SessionState.Load() ?? new SessionState();
      session.SheetPresets = this.userSheetPresets.Select(p => new SessionSheet { Width = p.W, Height = p.H }).ToList();
      session.Save();
    }

    /// <summary>Add Sheet opens a menu: "My sheets" (user presets) + standard stock sizes + "Custom size…".</summary>
    private void OnAddSheetMenu(object sender, RoutedEventArgs e)
    {
      var menu = new ContextMenu();
      string unit = this.unitsMm ? "mm" : "in";

      if (this.userSheetPresets.Count > 0)
      {
        menu.Items.Add(new MenuItem { Header = Localization.Get("Menu_MySheets"), IsEnabled = false, FontWeight = FontWeights.Bold });
        foreach (var preset in this.userSheetPresets.OrderBy(p => p.H).ThenBy(p => p.W))
        {
          var size = preset;
          var item = new MenuItem { Header = $"{size.W} × {size.H} {unit}" };
          item.Click += (_, __) => this.AddSheetOfSize(size.W, size.H, 1);
          menu.Items.Add(item);
        }

        var removeMenu = new MenuItem { Header = Localization.Get("Menu_RemoveMySheets") };
        foreach (var preset in this.userSheetPresets.OrderBy(p => p.H).ThenBy(p => p.W))
        {
          var size = preset;
          var item = new MenuItem { Header = $"{size.W} × {size.H} {unit}" };
          item.Click += (_, __) =>
          {
            this.userSheetPresets.Remove(size);
            this.SaveUserSheetPresets();
          };
          removeMenu.Items.Add(item);
        }

        menu.Items.Add(removeMenu);
        menu.Items.Add(new Separator());
      }

      int lastShortSide = -1;
      foreach (var preset in this.unitsMm ? SheetPresetsMm : SheetPresets)
      {
        if (!this.unitsMm && lastShortSide >= 0 && preset.Height != lastShortSide)
        {
          menu.Items.Add(new Separator()); // visual break between the 48-wide and 60-wide groups
        }

        lastShortSide = preset.Height;
        var size = preset;
        var item = new MenuItem { Header = size.Name };
        item.Click += (_, __) => this.AddSheetOfSize(size.Width, size.Height, 1);
        menu.Items.Add(item);
      }

      menu.Items.Add(new Separator());
      var custom = new MenuItem { Header = Localization.Get("Menu_CustomSize") };
      custom.Click += (_, __) =>
      {
        var dialog = new AddSheetWindow(this.unitsMm) { Owner = this };
        if (dialog.ShowDialog() == true)
        {
          this.AddSheetOfSize(dialog.SheetWidth, dialog.SheetHeight, dialog.SheetQuantity, dialog.SheetUnlimited, dialog.SheetPriority);
          if (dialog.RememberAsPreset && !this.userSheetPresets.Contains((dialog.SheetWidth, dialog.SheetHeight)))
          {
            this.userSheetPresets.Add((dialog.SheetWidth, dialog.SheetHeight));
            this.SaveUserSheetPresets();
          }
        }
      };
      menu.Items.Add(custom);

      menu.PlacementTarget = sender as UIElement;
      menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
      menu.IsOpen = true;
    }

    /// <summary>Edits a stock row through the sheet dialog — the panel shows no quantity at all.</summary>
    private void OnEditSheet(object sender, RoutedEventArgs e)
    {
      if ((sender as Button)?.Tag is ISheetLoadInfo row)
      {
        OpenEditSheet(row);
      }
    }

    /// <summary>Double-click on a stock row opens the same Edit sheet dialog as the ✎ button.</summary>
    private void OnSheetsListDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
      // Same guard as the parts list: MouseDoubleClick fires for double-clicks on the W/H spinners'
      // repeat arrows or the row buttons too, and those don't move the selection — walk up from the
      // real click target and edit the row that was actually hit.
      var d = e.OriginalSource as System.Windows.DependencyObject;
      while (d != null && !(d is ListViewItem))
      {
        if (d is System.Windows.Controls.Primitives.ButtonBase || d is Xceed.Wpf.Toolkit.IntegerUpDown)
        {
          return;
        }

        d = System.Windows.Media.VisualTreeHelper.GetParent(d);
      }

      if (d is ListViewItem item && item.DataContext is ISheetLoadInfo row)
      {
        OpenEditSheet(row);
      }
    }

    private void OpenEditSheet(ISheetLoadInfo row)
    {
      if (lastNestConsumed != null)
      {
        ClearNestResult();
      }

      var dialog = new AddSheetWindow(this.unitsMm) { Owner = this };
      dialog.PrefillForEdit(row.Width, row.Height, row.Quantity, row.Unlimited, row.Priority);
      if (dialog.ShowDialog() == true)
      {
        row.Width = dialog.SheetWidth;
        row.Height = dialog.SheetHeight;
        row.Quantity = dialog.SheetQuantity;
        row.Unlimited = dialog.SheetUnlimited;
        row.Priority = dialog.SheetPriority;
        this.sheetsListView?.Items.Refresh();
      }
    }

    private void AddSheetOfSize(int width, int height, int quantity, bool unlimited = false, int priority = 5)
    {
      if (lastNestConsumed != null)
      {
        ClearNestResult();
      }

      if (ViewModel.ActiveDocument is NestProjectViewModel doc && doc.AddSheetCommand.CanExecute(null))
      {
        doc.AddSheetCommand.Execute(null);
        var sheet = doc.ProjectInfo.SheetLoadInfos.LastOrDefault();
        if (sheet != null)
        {
          sheet.Width = width;
          sheet.Height = height;
          sheet.Quantity = quantity;
          sheet.Unlimited = unlimited;
          sheet.Priority = priority;
        }

        this.sheetsListView.Items.Refresh();
      }
    }

    private sealed class SheetPreset
    {
      public SheetPreset(string name, int width, int height)
      {
        this.Name = name;
        this.Width = width;
        this.Height = height;
      }

      public string Name { get; }

      public int Width { get; }

      public int Height { get; }
    }

    private async void OnAddPartClicked(object sender, RoutedEventArgs e)
    {
      await AddPartsToActiveProject(d => d.AddPartCommand);
      UpdateSummaryBadges();
    }

    /// <summary>
    /// Hands the job's part colours to everything that draws a part: the nest viewer, and the thumbnails in
    /// the parts list — which ARE the legend, one row per colour.
    /// <para>Built from EVERY row that has a path, included or not. Keyed off only the included ones,
    /// ticking a part's checkbox would silently shift all the other colours, and that checkbox is bound
    /// straight to the view model with no hook here to catch it. An excluded part just holds a colour
    /// nobody uses.</para>
    /// </summary>
    private void ApplyPartColours(System.Collections.Generic.IEnumerable<DeepNestLib.NestProject.IDetailLoadInfo> parts)
    {
      var map = PartColors.Build(parts?.Select(o => (o.Path, o.ColorRgb)));
      if (this.dxfViewer != null)
      {
        this.dxfViewer.PartColours = map;
      }

      DeepNestSharp.Ui.Converters.PartPreviewConverter.SetColours(map);

      // Tell each row the colour it ENDED UP with, so its swatch shows the real thing even when the user
      // never picked one. Only the user's own choice is saved with the project; this is display only.
      foreach (var row in parts ?? System.Linq.Enumerable.Empty<DeepNestLib.NestProject.IDetailLoadInfo>())
      {
        if (row is DeepNestSharp.Domain.Models.ObservableDetailLoadInfo observable)
        {
          observable.EffectiveColorRgb = PartColors.ToRgb(PartColors.For(map, row.Path));
        }
      }

      this.partsListView?.Items.Refresh();
    }

    /// <summary>One palette entry as the popup's swatch grid shows it.</summary>
    private sealed class PaletteSwatch
    {
      public int Rgb { get; set; }

      public string Label { get; set; }

      public System.Windows.Media.Brush Brush { get; set; }
    }

    /// <summary>The row whose colour the popup is currently editing.</summary>
    private DeepNestLib.NestProject.IDetailLoadInfo partBeingColoured;

    /// <summary>True while the canvas is being SET to the part's current colour — that is not the user
    /// choosing anything, and acting on it would recolour a part just by opening the picker.</summary>
    private bool suppressPartColorCanvas;

    /// <summary>Opens the colour picker over the swatch that was clicked.</summary>
    private void OnPartColorClick(object sender, RoutedEventArgs e)
    {
      if (!(sender is FrameworkElement swatch) || !(swatch.Tag is DeepNestLib.NestProject.IDetailLoadInfo part))
      {
        return;
      }

      this.partBeingColoured = part;

      if (this.partColorSwatches.ItemsSource == null)
      {
        var palette = new System.Collections.Generic.List<PaletteSwatch>();
        for (int i = 0; i < PartColors.PaletteLength; i++)
        {
          var (r, g, b) = PartColors.PaletteAt(i);
          var brush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(r, g, b));
          brush.Freeze();
          palette.Add(new PaletteSwatch { Rgb = PartColors.ToRgb((r, g, b)), Label = $"Part {i + 1}", Brush = brush });
        }

        this.partColorSwatches.ItemsSource = palette;
      }

      // Start the canvas on the colour the part has now, without that assignment counting as a change.
      var (cr, cg, cb) = PartColors.FromRgb(Math.Max(0, part.ColorRgb >= 0 ? part.ColorRgb : this.EffectiveColorOf(part)));
      this.partColorPopup.PlacementTarget = swatch;
      this.suppressPartColorCanvas = true;
      this.partColorCanvas.SelectedColor = System.Windows.Media.Color.FromRgb(cr, cg, cb);
      this.suppressPartColorCanvas = false;
      this.partColorPopup.IsOpen = true;
    }

    private void OnPartColorSwatchClick(object sender, RoutedEventArgs e)
    {
      if (sender is FrameworkElement swatch && swatch.Tag is int rgb)
      {
        this.ApplyChosenPartColour(rgb);
        this.partColorPopup.IsOpen = false;
      }
    }

    private void OnPartColorCanvasChanged(object sender, RoutedPropertyChangedEventArgs<System.Windows.Media.Color?> e)
    {
      if (!this.suppressPartColorCanvas && e.NewValue.HasValue)
      {
        this.ApplyChosenPartColour(PartColors.ToRgb((e.NewValue.Value.R, e.NewValue.Value.G, e.NewValue.Value.B)));
      }
    }

    /// <summary>Remembers the colour on the part, then repaints everything that draws it — the row's
    /// swatch, its thumbnail and the sheet.</summary>
    private void ApplyChosenPartColour(int rgb)
    {
      var part = this.partBeingColoured;
      if (part == null || part.ColorRgb == rgb)
      {
        return;
      }

      part.ColorRgb = rgb;
      var doc = ViewModel.ActiveDocument as NestProjectViewModel;
      this.ApplyPartColours(doc?.ProjectInfo?.DetailLoadInfos);
      this.dxfViewer?.RefreshRender();
    }

    private int EffectiveColorOf(DeepNestLib.NestProject.IDetailLoadInfo part)
      => PartColors.ToRgb(PartColors.For(this.dxfViewer?.PartColours, part.Path));

    private async System.Threading.Tasks.Task AddPartsToActiveProject(
      System.Func<NestProjectViewModel, Microsoft.Toolkit.Mvvm.Input.IAsyncRelayCommand> commandSelector)
    {
      var doc = ViewModel.ActiveDocument as NestProjectViewModel;
      if (doc == null)
      {
        return;
      }

      int before = doc.ProjectInfo.DetailLoadInfos.Count;
      await commandSelector(doc).ExecuteAsync(null);

      // The user may have closed the window while the parts loaded — don't touch a dead window.
      if (this.isClosed || !this.IsLoaded)
      {
        return;
      }

      // A new part changes the set of colours, and the thumbnails have to follow.
      this.ApplyPartColours(doc.ProjectInfo.DetailLoadInfos);

      // Insert flow: adding a single part opens Edit Part right away.
      //
      // A drawing that held several parts adds several rows, so this deliberately does not fire: there
      // is no single part to edit, and picking one of them would be a guess. The operator sees the rows
      // and opens whichever they meant.
      var infos = doc.ProjectInfo.DetailLoadInfos;
      if (infos.Count == before + 1 && infos[infos.Count - 1] is IDetailLoadInfo added)
      {
        OpenEditPart(added);
      }
    }

    private void OnEditPart(object sender, RoutedEventArgs e)
    {
      if ((sender as FrameworkElement)?.Tag is IDetailLoadInfo part)
      {
        OpenEditPart(part);
      }
    }

    private void OnPartsListDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
      // MouseDoubleClick fires even for double-clicks on the qty spinner's repeat arrows or the ✕
      // button inside a row (WPF class handler with handledEventsToo) — and those clicks don't move
      // the selection, so acting on SelectedItem could edit the WRONG part. Walk up from the actual
      // click target: ignore clicks on interactive controls, and edit the row that was hit.
      var d = e.OriginalSource as System.Windows.DependencyObject;
      while (d != null && !(d is ListViewItem))
      {
        if (d is System.Windows.Controls.Primitives.ButtonBase || d is Xceed.Wpf.Toolkit.IntegerUpDown)
        {
          return;
        }

        d = System.Windows.Media.VisualTreeHelper.GetParent(d);
      }

      if (d is ListViewItem item && item.DataContext is IDetailLoadInfo part)
      {
        OpenEditPart(part);
      }
    }

    private void OpenEditPart(IDetailLoadInfo part)
    {
      if (this.isClosed || !this.IsLoaded)
      {
        return;
      }

      var cfg = ViewModel.SvgNestConfigViewModel.SvgNestConfig;
      var dialog = new EditPartWindow(part, cfg.Rotations, System.Math.Max(0, cfg.Spacing))
      {
        Owner = this,
      };
      if (dialog.ShowDialog() != true)
      {
        return;
      }

      this.partsListView.Items.Refresh(); // plain (non-observable) fields may have changed
    }

    private async void OnNest(object sender, RoutedEventArgs e)
    {
      if (this.nestCts != null)
      {
        return;
      }

      // If there is already a nest result, discard it and return the sheets it used to the stock
      // before recalculating from scratch.
      if (!ViewModel.NestMonitorViewModel.TopNestResults.IsEmpty || lastNestConsumed != null)
      {
        ClearNestResult();
      }

      // SheetNest has ONE engine: sparrow, run as a bundled native subprocess.
      var project = (ViewModel.ActiveDocument as NestProjectViewModel)?.ProjectInfo;
      if (project == null || project.DetailLoadInfos.Count == 0)
      {
        ViewModel.MessageService.DisplayMessageBox(Localization.Get("Msg_AddPartsFirst"), Localization.Get("Title_Nest"), DeepNestLib.MessageBoxIcon.Information);
        return;
      }

      // Read the UI-bound project data on the UI thread, then do the heavy nest off-thread so the app
      // stays responsive (it was freezing because the whole nest ran on the UI thread).

      var parts = project.DetailLoadInfos
        .Where(o => o.IsIncluded && !string.IsNullOrWhiteSpace(o.Path))
        .SelectMany(o =>
        {
          var cc = o.EffectiveCommonCutting;
          string commonCuttingDomain = CommonCuttingDomainOf(o);
          bool directCommonCutting = RequiresMergedLinesForDirectCommonCutting(o);
          var populations = new List<RasterPartInfo>
          {
            new RasterPartInfo
            {
              Path = o.Path,
              Quantity = o.Quantity + o.Extra,               // required + spares
              Rotations = o.Rotations,                       // -1 = engine default
              Priority = o.Priority,                         // higher nests first
              Spacing = o.Spacing,                           // kept to whoever it cannot share a cut with; -1 = job default
              Cc = cc,
              CommonCuttingDomain = commonCuttingDomain,
              RequiresMergedLines = directCommonCutting,
            },
          };

          if (o.MirrorQuantity > 0)
          {
            // Second population of the SAME part, nested X-flipped (left/right-hand pairs).
            populations.Add(new RasterPartInfo
            {
              Path = o.Path,
              Quantity = o.MirrorQuantity,
              Rotations = o.Rotations,
              Priority = o.Priority,
              Spacing = o.Spacing,
              Cc = cc,
              CommonCuttingDomain = commonCuttingDomain,
              RequiresMergedLines = directCommonCutting,
              Mirrored = true,
            });
          }

          return populations;
        })
        .ToList();
      // EVERY sheet entry participates. For each sheet the engine packs the pending work onto every size
      // still in stock and cuts whichever filled most of its own sheet, so list order decides nothing; a
      // size marked Unlimited never runs down, and its Quantity is carried along only to be ignored.
      var sheetStock = project.SheetLoadInfos
        .Where(s => s.Width > 0 && s.Height > 0 && (s.Unlimited || s.Quantity > 0))
        .Select(s => (s.Width, s.Height, s.Quantity, s.Unlimited, s.Priority))
        .ToList();
      if (sheetStock.Count == 0)
      {
        ViewModel.MessageService.DisplayMessageBox(Localization.Get("Msg_AddSheetsFirst"), Localization.Get("Title_Nest"), DeepNestLib.MessageBoxIcon.Information);
        return;
      }

      // Total sheets the job may use — only meaningful when every size is counted, which is why the
      // warning that quotes it is skipped once a size is unlimited.
      int sheetQty = sheetStock.Sum(s => s.Quantity);
      var config = ViewModel.SvgNestConfigViewModel.SvgNestConfig;
      int rotations = config.Rotations;
      double spacing = config.Spacing;          // part spacing (drawing units = inches)
      double margin = config.SheetSpacing;      // sheet edge margin

      // The nester is the external sparrow engine (a bundled native binary). It must be present.
      string sparrowExe = ResolveSparrowExe();
      if (sparrowExe == null)
      {
        ViewModel.MessageService.DisplayMessageBox(
          Localization.Get("Msg_NoEngine"),
          "Nest",
          DeepNestLib.MessageBoxIcon.Warning);
        return;
      }

      var button = sender as Button;
      if (button != null)
      {
        button.IsEnabled = false;
      }

      nestCts = new System.Threading.CancellationTokenSource();
      this.cancelNestButton.Content = "■  Cancel";
      this.cancelNestButton.IsEnabled = true;
      this.cancelNestButton.Visibility = Visibility.Visible;
      this.nestPhaseText.Text = Localization.Format("Nest_Running", "0:00");
      this.nestPhaseText.Visibility = Visibility.Visible;

      // A stopwatch + timer keep the elapsed clock ticking between engine reports (the engine only reports
      // on sheet/density events, which can be seconds apart).
      this.nestStopwatch = System.Diagnostics.Stopwatch.StartNew();
      this.nestTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
      this.nestTimer.Tick += (_, __) => this.RenderNestProgress();
      this.nestTimer.Start();

      // The status just shows the elapsed clock (the DispatcherTimer keeps it ticking); the engine's per-sheet
      // reports aren't needed for that, but we still refresh on each so the clock updates promptly.
      var nestProgress = new System.Progress<(int Placed, int Total, int Sheet, double Density)>(_ =>
      {
        this.RenderNestProgress();
      });

      bool nestSucceeded = false;
      try
      {
        // Hidden end-to-end test hook for the crash reporter (set SHEETNEST_CRASH_TEST=1).
        if (System.Environment.GetEnvironmentVariable("SHEETNEST_CRASH_TEST") == "1")
        {
          throw new System.InvalidOperationException("Synthetic crash for testing the problem-report dialog.");
        }

        var token = nestCts.Token;

        // Hand the per-part spacings to the viewer BEFORE showing the result, so manual nesting
        // and intermediate candidates enforce the same clearances and part colors.
        this.ApplyPartColours(project.DetailLoadInfos);

        if (this.dxfViewer != null)
        {
          this.dxfViewer.DefaultPartSpacing = System.Math.Max(0, spacing);
          this.dxfViewer.SheetEdgeMargin = System.Math.Max(0, margin);
          this.dxfViewer.OffcutOptions = this.CurrentOffcutOptions();
          this.dxfViewer.PartSpacings = parts
            .GroupBy(p => p.Path, System.StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
              g => g.Key,
              g => g.Min(p => p.Spacing >= 0 ? p.Spacing : System.Math.Max(0, spacing)),
              System.StringComparer.OrdinalIgnoreCase);
        }

        if (this.nestDxfViewer != null)
        {
          this.nestDxfViewer.DefaultPartSpacing = System.Math.Max(0, spacing);
          this.nestDxfViewer.SheetEdgeMargin = System.Math.Max(0, margin);
          this.nestDxfViewer.OffcutOptions = this.CurrentOffcutOptions();
          this.nestDxfViewer.PartSpacings = this.dxfViewer?.PartSpacings;
        }

        // The search's work in progress is only DRAWN, sheet by sheet as it goes. It used to be added to the
        // variants list too, one row per try per sheet, so the list mixed a first sheet on its own (7 of 24
        // parts, "68%") with finished nests, and a partial nest looked better than the finished one.
        Action<INestResult> candidateCallback = candidateResult =>
        {
          this.Dispatcher.BeginInvoke(new Action(() =>
          {
            if (candidateResult == null || this.nestDxfViewer == null || this.nestCts == null) return;
            this.nestDxfViewer.Result = candidateResult;
            this.nestPreviewMetricsText.Text = Localization.Format(
              "Nest_LivePreview",
              candidateResult.TotalPlacedCount,
              candidateResult.TotalPartsCount,
              candidateResult.UsedSheets.Count);
          }));
        };

        var (result, error) = await Task.Run(() =>
        {
          // The sparrow engine: dense collision-separation nesting, per-sheet, corner-compacted, with
          // pattern replication across identical sheets. Works in real drawing units (no raster grid).
          // perSheetBudgetSec no longer bounds the search: how hard a sheet is searched is the operator's
          // effort setting, in iterations, so two machines agree on the nest and only the clock differs. What
          // survives here is the scale for the watchdog that kills a hung engine process.
          // Common cutting rides along on each part's own mode; there is no job-wide flag to keep in step.
          double unitScale = this.unitsMm ? CommonLineDefaults.InchesToMillimeters : 1.0;
          var r = SparrowNestService.Nest(
            parts,
            sheetStock,
            rotations,
            spacing,
            margin,
            5,
            sparrowExe,
            out string err,
            token,
            nestProgress,
            geometryTolerance: CommonLineDefaults.DefaultGeometryToleranceInches * unitScale,
            commonLineGap: config.CommonLineGapInches * unitScale,
            effort: this.nestEffort,
            commonLineMaxClusterParts: config.CommonLineMaxClusterParts,
            candidateCallback: candidateCallback,
            seedOffset: this.runSeedOffset);
          return (r, err);
        });

        this.runSeedOffset += 50;

        if (result == null)
        {
          ViewModel.MessageService.DisplayMessageBox(error ?? Localization.Get("Msg_NoResult"), Localization.Get("Title_Nest"), DeepNestLib.MessageBoxIcon.Information);
          return;
        }

        nestSucceeded = true;

        // Show it in the results list + viewer + status bar so utilization / placed / sheets / fitness /
        // time all appear. The result must be IN TopNestResults first, otherwise the list's two-way
        // SelectedItem binding resets the selection (and the stats) back to null.
        var vm = ViewModel.NestMonitorViewModel;
        vm.TopNestResults.SetSingleResult(result);
        vm.SelectedItem = result;

        if (this.nestDxfViewer != null)
        {
          this.nestDxfViewer.Result = result;
        }

        this.AddFinishedNest(result);

        // Consume the stock this nest used, right in the Sheets tab — the count visibly drops
        // (35 in stock, nest used 31 → the tab now shows 4) and closing the app persists what
        // physically remains.
        var used = new Dictionary<(int W, int H), int>();
        foreach (var sp in result.UsedSheets)
        {
          var key = ((int)System.Math.Round(sp.Sheet.WidthCalculated), (int)System.Math.Round(sp.Sheet.HeightCalculated));
          used[key] = used.TryGetValue(key, out int n) ? n + 1 : 1;
        }

        lastNestConsumed = ConsumeStock(project.SheetLoadInfos, used);
        this.sheetsListView.Items.Refresh();
        this.UpdateNestedInfo(result);

        // Not enough sheets for the whole order? Say so clearly — don't let a partial nest pass as done.
        // With an unlimited size in the stock the job never ran OUT of sheets, so telling the operator to
        // raise a quantity would send them to change a number that is already ignored: the only way a part
        // is left over then is that no sheet size is big enough to hold it.
        int unplacedCount = result.UnplacedParts?.Count ?? 0;
        if (unplacedCount > 0)
        {
          bool anyUnlimited = project.SheetLoadInfos.Any(s => s.Unlimited);
          ViewModel.MessageService.DisplayMessageBox(
            anyUnlimited
              ? Localization.Format("Msg_PartsDoNotFit", unplacedCount)
              : Localization.Format("Msg_NotEnoughSheets", unplacedCount, sheetQty),
            anyUnlimited ? Localization.Get("Title_PartsDoNotFit") : Localization.Get("Title_NotEnoughSheets"),
            DeepNestLib.MessageBoxIcon.Warning);
        }
      }
      catch (System.Exception cancelEx) when (IsCancellation(cancelEx))
      {
        // User pressed Cancel: not an error — discard everything and return to idle. Cancellations
        // can also arrive WRAPPED: a Parallel.For without the token in its options bundles the
        // workers' OperationCanceledExceptions into an AggregateException (user-reported crash
        // dialog on Cancel during the tail sweep).
        this.nestPhaseText.Text = Localization.Get("Nest_Cancelled");
        if (this.nestRuns.Count > 0 && this.runsListView.SelectedItem == null)
        {
          this.runsListView.SelectedItem = this.nestRuns[0];
        }
      }
      catch (System.Exception ex)
      {
        // A third-party DXF the importer chokes on (or an engine bug) must never kill the app —
        // this async void handler would otherwise crash the process (real-world user report).
        // Log it, offer the consent-based GitHub report, and stay alive.
        CrashReporter.Show(ex, "nest", this);
      }
      finally
      {
        string elapsed = NestElapsed();
        this.nestTimer?.Stop();
        this.nestTimer = null;
        this.nestStopwatch?.Stop();
        this.nestStopwatch = null;

        this.cancelNestButton.Visibility = Visibility.Collapsed;

        // Keep the final time on screen after a successful nest; keep "Nest cancelled" if cancelled;
        // otherwise (error) clear the status text.
        if (nestSucceeded)
        {
          CommonLineFallbackNotice[] commonLineFallbacks;
          lock (SparrowNestService.DiagCommonLineFallbacks)
          {
            commonLineFallbacks = SparrowNestService.DiagCommonLineFallbacks.ToArray();
          }

          // Say what common cutting actually managed. Giving a group up used to be completely silent,
          // so a job could come out with no shared cuts on it and look exactly like a job that never
          // had any to make.
          string seams = SparrowNestService.DiagSeamsSnapped > 0
              || SparrowNestService.DiagSeamsGivenUp > 0
              || commonLineFallbacks.Length > 0
            ? Localization.Format("Nest_SharedCuts", SparrowNestService.DiagSeamsSnapped)
              + (SparrowNestService.DiagSeamsGivenUp > 0
                ? Localization.Format("Nest_GivenUp", SparrowNestService.DiagSeamsGivenUp)
                : string.Empty)
              + (commonLineFallbacks.Length > 0
                ? Localization.Format("Nest_KeptNormal", commonLineFallbacks.Select(notice => notice.Drawing).Distinct(StringComparer.OrdinalIgnoreCase).Count())
                : string.Empty)
            : string.Empty;

          this.nestPhaseText.Text = Localization.Format("Nest_Done", elapsed, seams);

          if (commonLineFallbacks.Length > 0)
          {
            ViewModel.MessageService.DisplayMessageBox(
              CommonLineFallbackMessage(commonLineFallbacks),
              Localization.Get("Title_CommonLine"),
              DeepNestLib.MessageBoxIcon.Warning);
          }

        }
        else if (this.nestPhaseText.Text != Localization.Get("Nest_Cancelled"))
        {
          this.nestPhaseText.Visibility = Visibility.Collapsed;
        }

        nestCts.Dispose();
        nestCts = null;
        if (button != null)
        {
          button.IsEnabled = true;
        }
      }
    }

    /// <summary>Renders the live nest status: just the elapsed clock (no progress bar — a global % can't be
    /// known accurately). The final time is kept on screen when the nest ends.</summary>
    private void RenderNestProgress()
    {
      this.nestPhaseText.Text = Localization.Format("Nest_Running", NestElapsed());
    }

    /// <summary>Elapsed nest time as m:ss (0:00 if the stopwatch isn't running).</summary>
    private string NestElapsed()
    {
      return this.nestStopwatch is { } sw
        ? FormattableString.Invariant($"{(int)sw.Elapsed.TotalMinutes}:{sw.Elapsed.Seconds:00}")
        : "0:00";
    }

    /// <summary>Locates the external quality engine: SHEETNEST_SPARROW env var, else sparrow.exe beside the app.</summary>
    private static string ResolveSparrowExe()
    {
      var env = System.Environment.GetEnvironmentVariable("SHEETNEST_SPARROW");
      if (!string.IsNullOrWhiteSpace(env) && System.IO.File.Exists(env))
      {
        return env;
      }

      var beside = System.IO.Path.Combine(System.AppContext.BaseDirectory, "sparrow.exe");
      return System.IO.File.Exists(beside) ? beside : null;
    }

    private void OnCancelNest(object sender, RoutedEventArgs e)
    {
      // Cooperative: the engine checks the token at its phase boundaries (sub-second on real jobs).
      this.cancelNestButton.Content = "Cancelling…";
      this.cancelNestButton.IsEnabled = false;
      nestCts?.Cancel();
    }

    private static bool IsCancellation(System.Exception ex)
    {
      return ex is System.OperationCanceledException
        || (ex is System.AggregateException agg
            && agg.Flatten().InnerExceptions.Count > 0
            && agg.Flatten().InnerExceptions.All(e => e is System.OperationCanceledException));
    }

    /// <summary>
    /// Manual editing may leave a part on top of a neighbour or off the sheet — that is what makes
    /// rearranging possible — so the check that used to live in the editor lives here instead: nothing
    /// leaves the app while any layout is still wrong. Checks EVERY layout, not the one on screen.
    /// </summary>
    private bool RefuseWhileAnythingOverlaps(string title)
    {
      var unfit = this.dxfViewer?.FindUnfitLayouts();
      if (unfit == null || unfit.Count == 0)
      {
        return false;
      }

      // Say WHICH fault, per layout. The two are not the same job: an overlap is moved apart by hand, while
      // a part inside the edge margin usually means the margin wants lowering and the nest re-running.
      int parts = unfit.Sum(u => u.Parts);
      var where = string.Join("\n", unfit.Select(u => Localization.Format("Msg_UnfitRow", u.Layout, u.Parts, Why(u))));
      ViewModel.MessageService.DisplayMessageBox(
        Localization.Format("Msg_NotReadyToCut", parts, where),
        title,
        DeepNestLib.MessageBoxIcon.Stop);
      return true;

      static string Why((string Layout, int Parts, int Overlapping, int InMargin) u)
      {
        if (u.Overlapping > 0 && u.InMargin > 0)
        {
          return Localization.Format("Why_Both", u.Overlapping, u.InMargin);
        }

        return u.InMargin > 0 ? Localization.Get("Why_Margin") : Localization.Get("Why_Overlap");
      }
    }

    private void OnNestReportPdf(object sender, RoutedEventArgs e)
    {
      var selected = ViewModel.NestMonitorViewModel?.SelectedItem;
      if (selected == null || selected.UsedSheets == null || selected.UsedSheets.Count == 0)
      {
        ViewModel.MessageService.DisplayMessageBox(
          Localization.Get("Msg_NestFirstReport"),
          Localization.Get("Title_Report"),
          DeepNestLib.MessageBoxIcon.Information);
        return;
      }

      if (this.RefuseWhileAnythingOverlaps(Localization.Get("Title_Report")))
      {
        return;
      }

      // Report exactly what the viewer shows: one entry per distinct layout with its cut count.
      var plan = this.dxfViewer.GetProductionPlan();
      if (plan == null || plan.Count == 0)
      {
        plan = selected.UsedSheets.Select(s => ((DeepNestLib.Placement.ISheetPlacement)s, 1, "Sheet")).ToList();
      }

      var dialog = new Microsoft.Win32.SaveFileDialog
      {
        Filter = "PDF files (*.pdf)|*.pdf",
        FileName = "nest report.pdf",
        Title = "Save nest report",
      };
      if (dialog.ShowDialog(this) != true)
      {
        return;
      }

      try
      {
        Reports.NestReportPdf.Write(
          dialog.FileName,
          plan,
          selected.UnplacedParts?.Count ?? 0,
          this.unitsMm ? "mm" : "in",
          this.dxfViewer?.PartColours);
      }
      catch (System.Exception ex)
      {
        ViewModel.MessageService.DisplayMessageBox(
          Localization.Format("Msg_ReportWriteFailed", ex.Message),
          Localization.Get("Title_Report"),
          DeepNestLib.MessageBoxIcon.Error);
      }
    }

    private async void OnExportDxf(object sender, RoutedEventArgs e)
    {
      var selected = ViewModel.NestMonitorViewModel?.SelectedItem;
      if (selected == null || selected.UsedSheets == null || selected.UsedSheets.Count == 0)
      {
        ViewModel.MessageService.DisplayMessageBox(
          Localization.Get("Msg_NestFirstExport"),
          Localization.Get("Title_ExportDxf"),
          DeepNestLib.MessageBoxIcon.Information);
        return;
      }

      if (this.RefuseWhileAnythingOverlaps(Localization.Get("Title_ExportDxf")))
      {
        return;
      }

      // One DXF per DISTINCT layout, named with its number, sheet size and how many times it is cut, so two
      // identical sheets don't each produce a duplicate DXF. Fall back to every sheet if no grouping.
      var export = new List<(DeepNestLib.Placement.ISheetPlacement Sheet, int Count, IReadOnlyCollection<DeepNestLib.IO.OffcutLine> Offcut)>();
      var plan = this.dxfViewer.GetProductionPlan();
      if (plan != null && plan.Count > 0)
      {
        var offcutSheet = this.dxfViewer.OffcutSheet;
        foreach (var (sheet, count, _) in plan)
        {
          // The offcut separation cut(s) travel only with the genuine remainder sheet the engine
          // shaped (same rule as the viewer overlay) — never a replicated pattern or a synthetic
          // production-plan representative — so the cut matches the packed remainder exactly.
          var offcut = ReferenceEquals(sheet, offcutSheet) ? OffcutGeometry.BuildLines(sheet, this.CurrentOffcutOptions()) : null;
          export.Add((sheet, count, offcut));
        }
      }
      else
      {
        var used = selected.UsedSheets.ToList();
        for (int si = 0; si < used.Count; si++)
        {
          var offcut = si == used.Count - 1 ? OffcutGeometry.BuildLines(used[si], this.CurrentOffcutOptions()) : null;
          export.Add((used[si], 1, offcut));
        }
      }

      await ViewModel.ExportLayoutsAsync(export);
    }

    /// <summary>
    /// The contract a part shares a cut under. Only parts naming the same non-empty contract may share
    /// one, so a part that has not confirmed direct common cutting names none and shares with nobody.
    /// </summary>
    internal static string CommonCuttingDomainOf(IDetailLoadInfo part)
    {
      if (part == null || part.EffectiveCommonCutting == CommonCuttingMode.None)
      {
        return string.Empty;
      }

      return part.DirectCommonCuttingEnabled ? "direct" : string.Empty;
    }

    /// <summary>
    /// A direct common-cutting source makes its layout authoritative for DXF line merging. Unsupported
    /// formats must never force an exporter that cannot represent the shared edge once.
    /// </summary>
    internal static bool RequiresMergedLinesForDirectCommonCutting(IDetailLoadInfo part)
      => part != null
        && part.SupportsCommonCutting
        && part.DirectCommonCuttingEnabled
        && part.EffectiveCommonCutting != CommonCuttingMode.None;

    /// <summary>
    /// Charge a finished nest to the stock: counted sizes lose what it used, and the record of what each
    /// size gave up comes back for the Available badge and for <see cref="ReturnStock"/> to undo.
    /// <para>An unlimited size is REPORTED and not charged. Those are two different jobs and conflating
    /// them is what put "0 used" on the badge: leaving the size out of the record to stop Clear Result
    /// crediting it also took away the only number the badge had to show.</para>
    /// <para>Lives here, out of the click handler, so it can be tested at all. The whole reason it exists
    /// as a method is that it failed twice in one afternoon inside <c>OnNest</c>, where the suite cannot
    /// reach it without instantiating a WPF Window.</para>
    /// </summary>
    /// <param name="rows">The stock rows, whose Quantity this mutates.</param>
    /// <param name="usedBySize">How many sheets of each size the nest consumed. Not modified.</param>
    internal static Dictionary<(int W, int H), int> ConsumeStock(
      IEnumerable<ISheetLoadInfo> rows,
      IReadOnlyDictionary<(int W, int H), int> usedBySize)
    {
      var consumed = new Dictionary<(int W, int H), int>();
      if (rows == null || usedBySize == null)
      {
        return consumed;
      }

      // A copy, because the remaining count is walked down as rows claim it — two rows of the same size
      // must share what was used rather than both taking all of it. Doing that to the caller's dictionary
      // would leave it emptied behind their back.
      var remaining = usedBySize.ToDictionary(kv => kv.Key, kv => kv.Value);

      foreach (var row in rows)
      {
        var key = (row.Width, row.Height);
        if (!remaining.TryGetValue(key, out int n) || n <= 0)
        {
          continue;
        }

        int take = row.Unlimited ? n : Math.Min(row.Quantity, n);
        if (!row.Unlimited)
        {
          row.Quantity -= take;
        }

        remaining[key] = n - take;
        if (take > 0)
        {
          consumed[key] = consumed.TryGetValue(key, out int c) ? c + take : take;
        }
      }

      return consumed;
    }

    /// <summary>
    /// Undo <see cref="ConsumeStock"/>: the sheets a discarded result was holding go back on the rack.
    /// <para>An unlimited size gets nothing, because nothing was taken from it. That guard has to live
    /// here rather than at the deduction: reopening a saved project rebuilds the consumed record from the
    /// result itself, so this is the only place BOTH routes into a give-back pass through.</para>
    /// </summary>
    internal static void ReturnStock(
      System.Collections.Generic.IList<ISheetLoadInfo> rows,
      IReadOnlyDictionary<(int W, int H), int> consumed)
    {
      if (rows == null || consumed == null)
      {
        return;
      }

      foreach (var kv in consumed)
      {
        var row = rows.FirstOrDefault(s => s.Width == kv.Key.W && s.Height == kv.Key.H);
        if (row == null)
        {
          // The user removed the row after nesting — bring the size back so no stock is lost.
          rows.Add(new SheetLoadInfo(kv.Key.W, kv.Key.H, kv.Value));
        }
        else if (!row.Unlimited)
        {
          row.Quantity += kv.Value;
        }
      }
    }

    /// <summary>Formats the per-drawing reason a requested direct common line retained normal spacing.</summary>
    internal static string CommonLineFallbackMessage(IEnumerable<CommonLineFallbackNotice> notices)
    {
      var rows = (notices ?? Enumerable.Empty<CommonLineFallbackNotice>())
        .Where(notice => notice != null)
        .GroupBy(notice => notice.Drawing, StringComparer.OrdinalIgnoreCase)
        .Select(group => $"    {group.Key} — {string.Join("; ", group.Select(notice => notice.Reason).Distinct(StringComparer.Ordinal))}")
        .ToArray();
      if (rows.Length == 0)
      {
        return string.Empty;
      }

      return Localization.Format("Msg_CommonLineSkipped", string.Join("\n", rows));
    }

    /// <summary>
    /// The size window and edge overlap in force: what was saved, or the recommended figure for the
    /// drawing's units. Resolved HERE, where the geometry is built, and handed to the dialog already
    /// resolved — a default that lived only in the window would be a lie, showing 12 on screen while the
    /// cut was still computed the automatic way.
    /// <para>-1 means never saved and takes the default; an explicit 0 is the user asking for automatic
    /// (the 5% rule on a minimum, no limit on a maximum) and is left alone.</para>
    /// </summary>
    private (OffcutLimits Limits, double Spacing, double EdgeOverlap) EffectiveOffcutLimits()
    {
      var (d, defaultSpacing, defaultOverlap) = OffcutOptions.Defaults(this.unitsMm);
      return (
        new OffcutLimits(
          this.offcutMinX >= 0 ? this.offcutMinX : d.MinX,
          this.offcutMinY >= 0 ? this.offcutMinY : d.MinY,
          this.offcutMaxX >= 0 ? this.offcutMaxX : d.MaxX,
          this.offcutMaxY >= 0 ? this.offcutMaxY : d.MaxY),
        this.offcutSpacing >= 0 ? this.offcutSpacing : defaultSpacing,
        this.offcutEdgeOverlap >= 0 ? this.offcutEdgeOverlap : defaultOverlap);
    }

    /// <summary>The active offcut settings, or null when the option is off.</summary>
    private OffcutOptions CurrentOffcutOptions()
    {
      if (!this.preferRectOffcut)
      {
        return null;
      }

      var (limits, spacing, edgeOverlap) = this.EffectiveOffcutLimits();
      return new OffcutOptions
      {
        Direction = (OffcutDirection)System.Math.Max(0, System.Math.Min(3, this.offcutDirection)),
        Spacing = spacing,
        MinX = limits.MinX > 0 ? limits.MinX : -1,
        MinY = limits.MinY > 0 ? limits.MinY : -1,
        MaxX = limits.MaxX > 0 ? limits.MaxX : -1,
        MaxY = limits.MaxY > 0 ? limits.MaxY : -1,
        EdgeOverlap = System.Math.Max(0, edgeOverlap),
      };
    }

    #region Workflow Modes & Navigation

    private void SetCurrentMode(WorkflowMode mode)
    {
      this.currentMode = mode;

      if (this.gridModeParts != null)
      {
        this.gridModeParts.Visibility = mode == WorkflowMode.Parts ? Visibility.Visible : Visibility.Collapsed;
      }
      if (this.gridModeSheets != null)
      {
        this.gridModeSheets.Visibility = mode == WorkflowMode.Sheets ? Visibility.Visible : Visibility.Collapsed;
      }
      if (this.gridModeNesting != null)
      {
        this.gridModeNesting.Visibility = mode == WorkflowMode.Nesting ? Visibility.Visible : Visibility.Collapsed;
      }
      if (this.gridModeLayout != null)
      {
        this.gridModeLayout.Visibility = mode == WorkflowMode.Layout ? Visibility.Visible : Visibility.Collapsed;
      }

      this.settingMode = true;
      try
      {
        if (this.navPartsBtn != null) this.navPartsBtn.IsChecked = mode == WorkflowMode.Parts;
        if (this.navSheetsBtn != null) this.navSheetsBtn.IsChecked = mode == WorkflowMode.Sheets;
        if (this.navNestingBtn != null) this.navNestingBtn.IsChecked = mode == WorkflowMode.Nesting;
        if (this.navLayoutBtn != null) this.navLayoutBtn.IsChecked = mode == WorkflowMode.Layout;
      }
      finally
      {
        this.settingMode = false;
      }

      this.UpdateSummaryBadges();

      if (mode == WorkflowMode.Layout && this.viewTypeReportRadio?.IsChecked == true)
      {
        this.UpdateReportPreview();
      }
    }

    /// <summary>
    /// On Checked rather than Click, so a step chosen from the keyboard (arrows within the group) or by a
    /// screen reader switches the screen too, not just the highlight. SetCurrentMode checks the buttons
    /// itself, and the XAML checks step 1 while the window is still being built; both are ignored here.
    /// </summary>
    private void OnNavModeChecked(object sender, RoutedEventArgs e)
    {
      if (!this.IsLoaded || this.settingMode || !(sender is FrameworkElement fe) || fe.Tag == null)
      {
        return;
      }

      SetCurrentMode((WorkflowMode)Convert.ToInt32(fe.Tag));
    }

    private void OnGoToParts(object sender, RoutedEventArgs e) => SetCurrentMode(WorkflowMode.Parts);
    private void OnGoToSheets(object sender, RoutedEventArgs e) => SetCurrentMode(WorkflowMode.Sheets);
    private void OnGoToNesting(object sender, RoutedEventArgs e) => SetCurrentMode(WorkflowMode.Nesting);
    private void OnGoToLayout(object sender, RoutedEventArgs e) => SetCurrentMode(WorkflowMode.Layout);

    private void MainWindow_KeyDown(object sender, KeyEventArgs e)
    {
      if (Keyboard.Modifiers == ModifierKeys.Control)
      {
        if (e.Key == Key.D1 || e.Key == Key.NumPad1)
        {
          SetCurrentMode(WorkflowMode.Parts);
          e.Handled = true;
        }
        else if (e.Key == Key.D2 || e.Key == Key.NumPad2)
        {
          SetCurrentMode(WorkflowMode.Sheets);
          e.Handled = true;
        }
        else if (e.Key == Key.D3 || e.Key == Key.NumPad3)
        {
          SetCurrentMode(WorkflowMode.Nesting);
          e.Handled = true;
        }
        else if (e.Key == Key.D4 || e.Key == Key.NumPad4)
        {
          SetCurrentMode(WorkflowMode.Layout);
          e.Handled = true;
        }
      }
    }

    private void UpdateSummaryBadges()
    {
      if (this.partsCountSummary == null || this.sheetsCountSummary == null)
      {
        return;
      }

      if (ViewModel?.ActiveDocument is NestProjectViewModel doc && doc.ProjectInfo != null)
      {
        var parts = doc.ProjectInfo.DetailLoadInfos;
        int partTypes = parts.Count;
        int totalPartQty = parts.Sum(p => p.Quantity + p.Extra + p.MirrorQuantity);
        this.partsCountSummary.Text = string.Format(
          Localization.Get("Parts_CountSummary"),
          partTypes,
          totalPartQty);

        var sheets = doc.ProjectInfo.SheetLoadInfos;
        int sheetTypes = sheets.Count;
        int totalSheetQty = sheets.Where(s => !s.Unlimited).Sum(s => s.Quantity);
        bool hasUnlimited = sheets.Any(s => s.Unlimited);
        string sheetQtyStr = hasUnlimited
          ? $"{totalSheetQty}+ ({Localization.Get("Sheets_UnlimitedNote")})"
          : $"{totalSheetQty} {Localization.Get("Unit_Pcs")}";
        this.sheetsCountSummary.Text = string.Format(
          Localization.Get("Sheets_CountSummary"),
          sheetTypes,
          sheetQtyStr);
      }
      else
      {
        this.partsCountSummary.Text = string.Format(Localization.Get("Parts_CountSummary"), 0, 0);
        this.sheetsCountSummary.Text = string.Format(Localization.Get("Sheets_CountSummary"), 0, "0");
      }
    }

    #endregion

    #region Part & Sheet Table Actions

    private void OnRemoveSelectedPart(object sender, RoutedEventArgs e)
    {
      if (ViewModel?.ActiveDocument is NestProjectViewModel doc && this.partsListView.SelectedItem is IDetailLoadInfo selected)
      {
        doc.RemovePartCommand.Execute(selected);
        this.ApplyPartColours(doc.ProjectInfo.DetailLoadInfos);
        UpdateSummaryBadges();
      }
    }

    private void OnClearAllParts(object sender, RoutedEventArgs e)
    {
      if (ViewModel?.ActiveDocument is NestProjectViewModel doc && doc.ProjectInfo.DetailLoadInfos.Count > 0)
      {
        if (MessageBox.Show(Localization.Get("Msg_ClearPartsConfirm"), "SheetNest", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
        {
          doc.ClearPartsCommand.Execute(null);
          this.ApplyPartColours(doc.ProjectInfo.DetailLoadInfos);
          UpdateSummaryBadges();
        }
      }
    }

    private void OnRemoveSelectedSheet(object sender, RoutedEventArgs e)
    {
      if (ViewModel?.ActiveDocument is NestProjectViewModel doc && this.sheetsListView.SelectedItem is ISheetLoadInfo selected)
      {
        doc.RemoveSheetCommand.Execute(selected);
        UpdateSummaryBadges();
      }
    }

    private void OnPartQuantityChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
      if (ViewModel?.ActiveDocument is NestProjectViewModel doc)
      {
        doc.IsDirty = true;
        UpdateSummaryBadges();
      }
    }

    private void OnApplyPartsMultiplier(object sender, RoutedEventArgs e)
    {
      if (ViewModel?.ActiveDocument is not NestProjectViewModel doc)
      {
        return;
      }

      int factor = this.partsMultiplierUpDown?.Value ?? 1;
      if (factor <= 1)
      {
        return;
      }

      var selectedList = this.partsListView.SelectedItems.OfType<ObservableDetailLoadInfo>().ToList();
      var targetParts = (selectedList.Count > 0 && selectedList.Count < doc.ProjectInfo.DetailLoadInfos.Count)
        ? selectedList
        : doc.ProjectInfo.DetailLoadInfos.OfType<ObservableDetailLoadInfo>().ToList();

      if (targetParts.Count == 0)
      {
        return;
      }

      foreach (var part in targetParts)
      {
        part.Quantity = Math.Max(1, part.Quantity * factor);
        if (part.MirrorQuantity > 0)
        {
          part.MirrorQuantity = Math.Max(1, part.MirrorQuantity * factor);
        }
      }

      doc.IsDirty = true;
      UpdateSummaryBadges();
      this.partsListView?.Items.Refresh();
    }

    private void OnClearRunsClicked(object sender, RoutedEventArgs e)
    {
      this.nestRuns.Clear();
      this.finishedNests = 0;
      if (this.nestDxfViewer != null)
      {
        this.nestDxfViewer.Result = null;
      }

      if (this.nestPreviewMetricsText != null)
      {
        this.nestPreviewMetricsText.Text = Localization.CurrentLanguage == Localization.LangBulgarian
          ? "Преглед на разкроя: изберете вариант от списъка с варианти"
          : (Localization.CurrentLanguage == Localization.LangEnglish
            ? "Nesting preview: select a variant from the list of variants"
            : "Предпросмотр раскладки: выберите вариант в списке вариантов");
      }
    }

    #endregion

    #region Drag & Drop

    private void OnWindowDragOver(object sender, DragEventArgs e)
    {
      if (e.Data.GetDataPresent(DataFormats.FileDrop))
      {
        e.Effects = DragDropEffects.Copy;
        e.Handled = true;
      }
      else
      {
        e.Effects = DragDropEffects.None;
      }
    }

    private void OnPartsDragOver(object sender, DragEventArgs e)
    {
      OnWindowDragOver(sender, e);
    }

    private async void OnWindowDrop(object sender, DragEventArgs e)
    {
      if (!(e.Data.GetData(DataFormats.FileDrop) is string[] files) || files.Length == 0)
      {
        return;
      }

      // The parts grid routes here too; without this the Drop bubbles on to the window and every
      // file is added twice.
      e.Handled = true;
      try
      {
        await ProcessDroppedFilesAsync(files);
      }
      catch (Exception ex)
      {
        CrashReporter.Show(ex, "drop-files", this);
      }
    }

    private void OnPartsDrop(object sender, DragEventArgs e) => OnWindowDrop(sender, e);

    /// <summary>Files named on the command line: the same handling as files dropped on the window.</summary>
    internal async void OpenFiles(string[] files)
    {
      try
      {
        await ProcessDroppedFilesAsync(files);
      }
      catch (Exception ex)
      {
        CrashReporter.Show(ex, "open-files", this);
      }
    }

    private async Task ProcessDroppedFilesAsync(string[] files)
    {
      if (files == null || files.Length == 0)
      {
        return;
      }

      // If user dropped a project file (.dnest), open it
      var projectFile = files.FirstOrDefault(f => string.Equals(System.IO.Path.GetExtension(f), ".dnest", StringComparison.OrdinalIgnoreCase));
      if (projectFile != null)
      {
        ViewModel.LoadNestProjectInteractive(projectFile);
        return;
      }

      // Ensure active document exists
      if (ViewModel.ActiveDocument == null && ViewModel.CreateNestProjectCommand.CanExecute(null))
      {
        ViewModel.CreateNestProjectCommand.Execute(null);
      }

      var doc = ViewModel.ActiveDocument as NestProjectViewModel;
      if (doc == null)
      {
        return;
      }

      int before = doc.ProjectInfo.DetailLoadInfos.Count;

      await doc.AddPartFilesAsync(files.Where(DeepNestLib.IO.DxfFileExtensions.IsDxf));

      if (doc.ProjectInfo.DetailLoadInfos.Count > before)
      {
        this.ApplyPartColours(doc.ProjectInfo.DetailLoadInfos);
        UpdateSummaryBadges();

        SetCurrentMode(WorkflowMode.Parts);

        var infos = doc.ProjectInfo.DetailLoadInfos;
        if (infos.Count == before + 1 && infos[infos.Count - 1] is IDetailLoadInfo added)
        {
          OpenEditPart(added);
        }
      }
    }

    #endregion

    #region Narrow and portrait windows

    // At the laser the monitor stands on end (1080 x 1920). The part columns were sized for a landscape screen and
    // ran off its right edge; the name now takes what the others leave, and a narrow window drops the area
    // column, which the size column beside it already tells.
    private void OnPartsListSizeChanged(object sender, SizeChangedEventArgs e)
    {
      if (!e.WidthChanged)
      {
        return;
      }

      double available = this.partsListView.ActualWidth - SystemParameters.VerticalScrollBarWidth - 10;
      this.colHeaderArea.Width = available < 1150 ? 0 : 105;
      double others = this.colHeaderPartPreview.Width + this.colHeaderPartQty.Width + this.colHeaderDimensions.Width
        + this.colHeaderArea.Width + this.colHeaderColor.Width + this.colHeaderPriority.Width
        + this.colHeaderPartStatus.Width + this.colHeaderPartActions.Width;
      this.colHeaderPartName.Width = Math.Max(220, available - others);
    }

    private bool? nestAreaStacked;

    // Side by side on a landscape screen; on one standing on end the variants go above the preview, which then
    // gets the whole width for the sheet.
    private void OnNestWorkAreaSizeChanged(object sender, SizeChangedEventArgs e)
    {
      bool stacked = e.NewSize.Width < 1000 || e.NewSize.Height > e.NewSize.Width;
      if (this.nestAreaStacked == stacked)
      {
        return;
      }

      this.nestAreaStacked = stacked;
      var grid = this.nestWorkArea;
      grid.ColumnDefinitions.Clear();
      grid.RowDefinitions.Clear();
      if (stacked)
      {
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(260), MinHeight = 120 });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star), MinHeight = 300 });
      }
      else
      {
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(380), MinWidth = 260 });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 360 });
      }

      for (int i = 0; i < 3; i++)
      {
        UIElement pane = i == 0 ? this.runsPane : (i == 1 ? this.runsSplitter : this.previewPane);
        Grid.SetRow(pane, stacked ? i : 0);
        Grid.SetColumn(pane, stacked ? 0 : i);
      }

      this.runsPane.BorderThickness = stacked ? new Thickness(0, 0, 0, 1) : new Thickness(0, 0, 1, 0);
      this.runsSplitter.Width = stacked ? double.NaN : 5;
      this.runsSplitter.Height = stacked ? 5 : double.NaN;
      this.runsSplitter.HorizontalAlignment = stacked ? HorizontalAlignment.Stretch : HorizontalAlignment.Center;
      this.runsSplitter.VerticalAlignment = stacked ? VerticalAlignment.Center : VerticalAlignment.Stretch;
      this.runsSplitter.ResizeDirection = stacked ? GridResizeDirection.Rows : GridResizeDirection.Columns;
    }

    #endregion

    #region Runs History & Layout Selection

    private int finishedNests;

    /// <summary>
    /// Adds a finished nest to the variants list, the way Solid Edge Nesting keeps its solutions: one row per
    /// complete nest, best first (every part placed, then fewest sheets, then most material used), the best
    /// starred. A nest that comes out the same as one already listed is not listed twice; that row is selected.
    /// </summary>
    private void AddFinishedNest(INestResult result)
    {
      string util = FormattableString.Invariant($"{result.MaterialUtilization * 100:0.#}%");
      string placed = FormattableString.Invariant($"{result.TotalPlacedCount} / {result.TotalPartsCount}");
      string sheets = FormattableString.Invariant($"{result.UsedSheets.Count}");
      var same = this.nestRuns.FirstOrDefault(r => r.UtilizationText == util && r.PlacedText == placed && r.SheetsText == sheets);
      if (same != null)
      {
        this.runsListView.SelectedItem = same;
        return;
      }

      var item = new NestRunHistoryItem
      {
        RunNumber = ++this.finishedNests,
        Result = result,
        UtilizationText = util,
        PlacedText = placed,
        SheetsText = sheets,
        TimeText = NestElapsed(),
      };

      static (int Placed, int Sheets, double Util) Rank(INestResult r)
        => (r.TotalPlacedCount, -r.UsedSheets.Count, r.MaterialUtilization);

      int at = 0;
      while (at < this.nestRuns.Count && Rank(this.nestRuns[at].Result).CompareTo(Rank(result)) >= 0)
      {
        at++;
      }

      this.nestRuns.Insert(at, item);
      for (int i = 0; i < this.nestRuns.Count; i++)
      {
        this.nestRuns[i].IsBest = i == 0;
      }

      this.runsListView.Items.Refresh();
      this.runsListView.SelectedItem = item;
    }

    private void OnRunsListSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
      if (this.runsListView.SelectedItem is NestRunHistoryItem item && item.Result != null)
      {
        if (this.nestDxfViewer != null)
        {
          this.nestDxfViewer.Result = item.Result;
        }
        if (this.nestPreviewMetricsText != null)
        {
          string variantWord = Localization.CurrentLanguage == Localization.LangBulgarian
            ? "Вариант"
            : (Localization.CurrentLanguage == Localization.LangEnglish ? "Variant" : "Вариант");
          string utilWord = Localization.CurrentLanguage == Localization.LangBulgarian
            ? "Утилизация"
            : (Localization.CurrentLanguage == Localization.LangEnglish ? "Utilization" : "Утилизация");
          string placedWord = Localization.CurrentLanguage == Localization.LangBulgarian
            ? "Поставени"
            : (Localization.CurrentLanguage == Localization.LangEnglish ? "Placed" : "Размещено");
          string sheetsWord = Localization.CurrentLanguage == Localization.LangBulgarian
            ? "Листа"
            : (Localization.CurrentLanguage == Localization.LangEnglish ? "Sheets" : "Листов");
          string timeWord = Localization.CurrentLanguage == Localization.LangBulgarian
            ? "време"
            : (Localization.CurrentLanguage == Localization.LangEnglish ? "time" : "время");

          this.nestPreviewMetricsText.Text = $"{variantWord} #{item.RunNumber}: {utilWord} {item.UtilizationText} | {placedWord} {item.PlacedText} | {sheetsWord}: {item.SheetsText} ({timeWord}: {item.TimeText})";
        }
        ViewModel.NestMonitorViewModel.SelectedItem = item.Result;
        this.UpdateNestedInfo(item.Result);
      }
    }

    private void OnRunsListDoubleClick(object sender, MouseButtonEventArgs e)
    {
      if (this.runsListView.SelectedItem is NestRunHistoryItem item && item.Result != null)
      {
        ViewModel.NestMonitorViewModel.SelectedItem = item.Result;
        SetCurrentMode(WorkflowMode.Layout);
      }
    }

    #endregion

    #region Screen 4: Layout & Report Preview

    private double reportZoom = 1.0;
    private int currentReportPageIndex = 0;

    private void OnLayoutViewTypeChanged(object sender, RoutedEventArgs e)
    {
      bool showReport = this.viewTypeReportRadio?.IsChecked == true;
      if (this.dxfViewerBorder != null)
      {
        this.dxfViewerBorder.Visibility = showReport ? Visibility.Collapsed : Visibility.Visible;
      }
      if (this.reportPreviewGrid != null)
      {
        this.reportPreviewGrid.Visibility = showReport ? Visibility.Visible : Visibility.Collapsed;
      }
      if (showReport)
      {
        UpdateReportPreview();
      }
    }

    private void UpdateReportPreview()
    {
      var selected = ViewModel.NestMonitorViewModel?.SelectedItem;
      if (selected == null || selected.UsedSheets == null || selected.UsedSheets.Count == 0)
      {
        if (this.reportEmptyNotice != null) this.reportEmptyNotice.Visibility = Visibility.Visible;
        if (this.reportPagesStackPanel != null) this.reportPagesStackPanel.Children.Clear();
        this.currentReportPageIndex = 0;
        UpdateReportPageIndicator();
        return;
      }

      if (this.reportEmptyNotice != null) this.reportEmptyNotice.Visibility = Visibility.Collapsed;

      var plan = this.dxfViewer?.GetProductionPlan();
      if (plan == null || plan.Count == 0)
      {
        plan = selected.UsedSheets.Select(s => ((DeepNestLib.Placement.ISheetPlacement)s, 1, "Sheet")).ToList();
      }

      string units = this.unitsMm ? "mm" : "in"; // the report translates the word itself; it computes with this one
      var colours = this.dxfViewer?.PartColours ?? PartColors.Build(plan.SelectMany(l => l.Sheet.PartPlacements).Select(p => p.Part?.Name));
      int unplacedCount = selected.UnplacedParts?.Count ?? 0;

      var pages = Reports.NestReportPdf.BuildWpfPages(plan, unplacedCount, units, colours);
      if (this.reportPagesStackPanel != null)
      {
        this.reportPagesStackPanel.Children.Clear();
        foreach (var page in pages)
        {
          this.reportPagesStackPanel.Children.Add(page);
        }
      }

      this.currentReportPageIndex = 0;
      UpdateReportPageIndicator();

      int totalSheets = plan.Sum(l => l.Count);
      int totalParts = plan.Sum(l => l.Count * l.Sheet.PartPlacements.Count);
      double placedArea = plan.Sum(l => l.Count * l.Sheet.PartPlacements.Sum(p => System.Math.Abs(p.Part.NetArea)));
      double stockArea = plan.Sum(l => l.Count * l.Sheet.Sheet.WidthCalculated * l.Sheet.Sheet.HeightCalculated);
      double overallUtil = stockArea <= 0 ? 0 : placedArea / stockArea * 100.0;
      if (this.reportSummaryText != null)
      {
        this.reportSummaryText.Text = FormattableString.Invariant(
          $"{Localization.Get("Kpi_Utilization")} {overallUtil:0.#}% | {Localization.Get("Kpi_SheetsUsed")} {totalSheets} | {Localization.Get("Kpi_PartsPlaced")} {totalParts}");
      }

      if (this.reportPagesScroll != null && this.reportPagesScroll.ActualWidth > 300)
      {
        double avail = this.reportPagesScroll.ActualWidth - 60;
        if (avail > 300 && avail < 842)
        {
          SetReportZoom(avail / 842.0);
        }
      }
    }

    private void UpdateReportPageIndicator()
    {
      int total = this.reportPagesStackPanel?.Children.Count ?? 0;
      if (total == 0)
      {
        if (this.reportPageIndicatorText != null) this.reportPageIndicatorText.Text = "—";
        return;
      }
      int cur = Math.Clamp(this.currentReportPageIndex + 1, 1, total);
      if (this.reportPageIndicatorText != null)
      {
        string fmt = Localization.Get("Report_PageXofY");
        this.reportPageIndicatorText.Text = string.Format(fmt, cur, total);
      }
    }

    private void SetReportZoom(double zoom)
    {
      this.reportZoom = Math.Clamp(zoom, 0.35, 2.5);
      if (this.reportScaleTransform != null)
      {
        this.reportScaleTransform.ScaleX = this.reportZoom;
        this.reportScaleTransform.ScaleY = this.reportZoom;
      }
      if (this.reportZoomText != null)
      {
        this.reportZoomText.Text = $"{Math.Round(this.reportZoom * 100)}%";
      }
    }

    private void OnReportZoomIn(object sender, RoutedEventArgs e)
    {
      SetReportZoom(this.reportZoom + 0.15);
    }

    private void OnReportZoomOut(object sender, RoutedEventArgs e)
    {
      SetReportZoom(this.reportZoom - 0.15);
    }

    private void OnReportZoom100(object sender, RoutedEventArgs e)
    {
      SetReportZoom(1.0);
    }

    private void OnReportZoomFit(object sender, RoutedEventArgs e)
    {
      double availableWidth = (this.reportPagesScroll?.ActualWidth ?? 0) - 60;
      if (availableWidth > 200)
      {
        SetReportZoom(availableWidth / 842.0);
      }
      else
      {
        SetReportZoom(1.0);
      }
    }

    private void OnReportScrollPreviewMouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
    {
      if ((System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Control) == System.Windows.Input.ModifierKeys.Control)
      {
        e.Handled = true;
        double delta = e.Delta > 0 ? 0.1 : -0.1;
        SetReportZoom(this.reportZoom + delta);
      }
    }

    private void OnReportPagesScrollChanged(object sender, ScrollChangedEventArgs e)
    {
      if (this.reportPagesStackPanel == null || this.reportPagesStackPanel.Children.Count == 0) return;
      double scrollY = this.reportPagesScroll.VerticalOffset;
      double pageHeightWithMargin = (595.0 + 28.0) * this.reportZoom;
      if (pageHeightWithMargin > 0)
      {
        int page = (int)Math.Round(scrollY / pageHeightWithMargin);
        page = Math.Clamp(page, 0, this.reportPagesStackPanel.Children.Count - 1);
        if (page != this.currentReportPageIndex)
        {
          this.currentReportPageIndex = page;
          UpdateReportPageIndicator();
        }
      }
    }

    private void OnReportPrevPage(object sender, RoutedEventArgs e)
    {
      int total = this.reportPagesStackPanel?.Children.Count ?? 0;
      if (total <= 1) return;
      if (this.currentReportPageIndex > 0)
      {
        this.currentReportPageIndex--;
        ScrollToReportPage(this.currentReportPageIndex);
      }
    }

    private void OnReportNextPage(object sender, RoutedEventArgs e)
    {
      int total = this.reportPagesStackPanel?.Children.Count ?? 0;
      if (total <= 1) return;
      if (this.currentReportPageIndex < total - 1)
      {
        this.currentReportPageIndex++;
        ScrollToReportPage(this.currentReportPageIndex);
      }
    }

    private void ScrollToReportPage(int index)
    {
      if (this.reportPagesStackPanel != null && index >= 0 && index < this.reportPagesStackPanel.Children.Count)
      {
        var el = this.reportPagesStackPanel.Children[index] as FrameworkElement;
        el?.BringIntoView();
        UpdateReportPageIndicator();
      }
    }

    private void OnPrintReport(object sender, RoutedEventArgs e)
    {
      var selected = ViewModel.NestMonitorViewModel?.SelectedItem;
      if (selected == null || selected.UsedSheets == null || selected.UsedSheets.Count == 0)
      {
        ViewModel.MessageService.DisplayMessageBox(
          Localization.Get("Msg_NestFirstToPrint"),
          Localization.Get("Btn_PrintReport"),
          DeepNestLib.MessageBoxIcon.Information);
        return;
      }

      if (this.RefuseWhileAnythingOverlaps(Localization.Get("Title_PrintReport")))
      {
        return;
      }

      var plan = this.dxfViewer?.GetProductionPlan();
      if (plan == null || plan.Count == 0)
      {
        plan = selected.UsedSheets.Select(s => ((DeepNestLib.Placement.ISheetPlacement)s, 1, "Sheet")).ToList();
      }

      string units = this.unitsMm ? "mm" : "in"; // the report translates the word itself; it computes with this one
      var colours = this.dxfViewer?.PartColours ?? PartColors.Build(plan.SelectMany(l => l.Sheet.PartPlacements).Select(p => p.Part?.Name));
      int unplacedCount = selected.UnplacedParts?.Count ?? 0;

      var doc = Reports.NestReportPdf.BuildFixedDocument(plan, unplacedCount, units, colours);

      var printDlg = new System.Windows.Controls.PrintDialog();
      if (printDlg.ShowDialog() == true)
      {
        try
        {
          if (printDlg.PrintTicket != null)
          {
            printDlg.PrintTicket.PageOrientation = System.Printing.PageOrientation.Landscape;
          }
          printDlg.PrintDocument(((System.Windows.Documents.IDocumentPaginatorSource)doc).DocumentPaginator, "SheetNest Production Report");
        }
        catch (System.Exception ex)
        {
          ViewModel.MessageService.DisplayMessageBox(
            $"{Localization.Get("Msg_PrintError")}: {ex.Message}",
            Localization.Get("Btn_PrintReport"),
            DeepNestLib.MessageBoxIcon.Error);
        }
      }
    }

    #endregion
  }
}
