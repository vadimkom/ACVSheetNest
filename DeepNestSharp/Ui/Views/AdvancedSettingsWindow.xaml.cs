namespace DeepNestSharp.Ui.Views
{
  using System.Windows;
  using System.Windows.Controls;
  using DeepNestLib;
  using Localization = DeepNestSharp.Ui.Localization;

  /// <summary>
  /// Curated settings dialog (replaces the old auto-generated property grid): only the options the
  /// live raster engine, the DXF export and the app itself consume. OK applies and persists;
  /// Cancel changes nothing.
  /// </summary>
  public partial class AdvancedSettingsWindow : Window
  {
    private readonly ISvgNestConfig config;
    private double commonLineGapInches;
    private int commonLineMaxClusterParts;
    private bool displayedUnitsMm;
    private bool suppressUnitsChanged = true;

    internal AdvancedSettingsWindow(
      ISvgNestConfig config,
      bool autosaveEnabled,
      int autosaveMinutes,
      bool unitsMm = true,
      double commonLineGapInches = DeepNestLib.Placement.CommonLineDefaults.DefaultGapInches,
      DeepNestSharp.RasterNest.NestEffort effort = DeepNestSharp.RasterNest.NestEffortDefaults.Default,
      int commonLineMaxClusterParts = DeepNestLib.Placement.CommonLineDefaults.DefaultMaxClusterParts,
      int concurrencyLimit = 0)
    {
      this.config = config;
      this.commonLineGapInches = NormalizeCommonLineGapInches(commonLineGapInches);
      this.commonLineMaxClusterParts = NormalizeCommonLineMaxClusterParts(commonLineMaxClusterParts);
      InitializeComponent();

      this.unitsCombo.SelectedIndex = unitsMm ? 1 : 0;
      this.displayedUnitsMm = unitsMm;
      this.suppressUnitsChanged = false;
      this.UpdateUnitPresentation();

      SelectRotations(config.Rotations);

      // Auto first (tag 0), then every count a wave can use. Filled before the effort is selected, because
      // the Auto entry shows what Auto means for the selected effort.
      for (int n = 0; n <= DeepNestSharp.RasterNest.SparrowNestService.MaxConcurrencyLimit; n++)
      {
        this.concurrencyCombo.Items.Add(new ComboBoxItem { Tag = n, Content = n.ToString(System.Globalization.CultureInfo.InvariantCulture) });
      }

      this.concurrencyCombo.SelectedIndex = System.Math.Clamp(concurrencyLimit, 0, DeepNestSharp.RasterNest.SparrowNestService.MaxConcurrencyLimit);
      this.effortCombo.SelectedIndex = (int)effort;
      this.spacingUpDown.Value = System.Math.Max(0, config.Spacing);
      this.marginUpDown.Value = System.Math.Max(0, config.SheetSpacing);
      this.mergeLinesCheck.IsChecked = config.MergeLines;
      this.differentiateCheck.IsChecked = config.DifferentiateChildren;
      switch (config.DxfPartNameExport)
      {
        case DxfPartNameExport.None:
          this.partNameExportCombo.SelectedIndex = 0;
          break;
        case DxfPartNameExport.Layer9:
          this.partNameExportCombo.SelectedIndex = 1;
          break;
        case DxfPartNameExport.Background:
        default:
          this.partNameExportCombo.SelectedIndex = 2;
          break;
      }

      bool automaticClusterLimit = this.commonLineMaxClusterParts
        == DeepNestLib.Placement.CommonLineDefaults.AutoMaxClusterParts;
      this.commonLineMaxClusterPartsUpDown.Value = automaticClusterLimit
        ? DeepNestLib.Placement.CommonLineDefaults.MinimumMaxClusterParts
        : this.commonLineMaxClusterParts;
      this.commonLineClusterModeCombo.SelectedIndex = automaticClusterLimit ? 0 : 1;
      this.UpdateCommonLineClusterLimitPresentation();

      this.autosaveCheck.IsChecked = autosaveEnabled;
      this.minutesUpDown.Value = autosaveMinutes;
      this.minutesUpDown.IsEnabled = autosaveEnabled;

      this.ApplyLocalization();
    }

    public bool AutosaveEnabled => this.autosaveCheck.IsChecked == true;

    /// <summary>Drawing units are millimeters — persisted by the caller (SessionState).</summary>
    public bool UnitsMm => this.unitsCombo.SelectedIndex == 1;

    /// <summary>The physical common-line gap, in canonical inches, for the caller to persist.</summary>
    public double CommonLineGapInches => this.commonLineGapInches;

    /// <summary>Zero selects automatic common-line cluster sizing; manual limits are 2..500.</summary>
    public int CommonLineMaxClusterParts => this.commonLineMaxClusterParts;

    public int AutosaveMinutes => this.minutesUpDown.Value ?? 5;

    /// <summary>Converts canonical inches to the units selected for display.</summary>
    internal static double CommonLineGapForDisplay(double inches, bool unitsMm)
      => inches * (unitsMm ? DeepNestLib.Placement.CommonLineDefaults.InchesToMillimeters : 1.0);

    /// <summary>Converts an edited display value back to canonical inches.</summary>
    internal static double CommonLineGapFromDisplay(double value, bool unitsMm)
      => value / (unitsMm ? DeepNestLib.Placement.CommonLineDefaults.InchesToMillimeters : 1.0);

    /// <summary>
    /// Converts a linear distance between inches and millimeters (or vice versa), rounding to standard precision
    /// (2 decimal places for mm, 3 decimal places for inches).
    /// </summary>
    internal static double ConvertDistance(double value, bool fromMm, bool toMm)
    {
      if (fromMm == toMm)
      {
        return value;
      }

      return toMm
        ? System.Math.Round(value * DeepNestLib.Placement.CommonLineDefaults.InchesToMillimeters, 2)
        : System.Math.Round(value / DeepNestLib.Placement.CommonLineDefaults.InchesToMillimeters, 3);
    }

    /// <summary>How hard the nester searches each sheet. Persisted by the caller (SessionState), per user
    /// and not per project: it is a preference about this operator's time, so opening somebody else's job
    /// must not change it.</summary>
    internal DeepNestSharp.RasterNest.NestEffort Effort => this.effortCombo.SelectedItem is ComboBoxItem e
      ? (DeepNestSharp.RasterNest.NestEffort)int.Parse((string)e.Tag, System.Globalization.CultureInfo.InvariantCulture)
      : DeepNestSharp.RasterNest.NestEffortDefaults.Default;

    /// <summary>The most nesting searches run at once; 0 = automatic. Persisted by the caller (SessionState).</summary>
    internal int ConcurrencyLimit => this.concurrencyCombo.SelectedItem is ComboBoxItem c && c.Tag is int n ? n : 0;

    private void OnEffortChanged(object sender, SelectionChangedEventArgs e) => this.UpdateConcurrencyAutoText();

    /// <summary>Auto depends on the effort (Normal and Best run two threads per search, Fast one), so the
    /// entry says how many it will be on this computer for the effort now selected.</summary>
    private void UpdateConcurrencyAutoText()
    {
      if (this.concurrencyCombo == null || this.concurrencyCombo.Items.Count == 0)
      {
        return;
      }

      int auto = DeepNestSharp.RasterNest.SparrowNestService.AutoConcurrency(System.Environment.ProcessorCount, this.Effort);
      ((ComboBoxItem)this.concurrencyCombo.Items[0]).Content = string.Format(
        System.Globalization.CultureInfo.CurrentCulture, Localization.Get("Settings_Concurrency_Auto"), auto);
    }

    /// <summary>The chosen global rotation code (1/2/4/8/36) — persisted by the caller.</summary>
    public int Rotations => this.rotationsCombo.SelectedItem is ComboBoxItem item
      ? int.Parse((string)item.Tag, System.Globalization.CultureInfo.InvariantCulture)
      : 4;

    private void SelectRotations(int code)
    {
      foreach (ComboBoxItem item in this.rotationsCombo.Items)
      {
        if (int.Parse((string)item.Tag, System.Globalization.CultureInfo.InvariantCulture) == code)
        {
          this.rotationsCombo.SelectedItem = item;
          return;
        }
      }

      this.rotationsCombo.SelectedIndex = 2; // 90° steps — the engine default
    }

    private void OnAutosaveToggled(object sender, RoutedEventArgs e)
    {
      if (this.minutesUpDown != null)
      {
        this.minutesUpDown.IsEnabled = this.autosaveCheck.IsChecked == true;
      }
    }

    private void OnUnitsChanged(object sender, SelectionChangedEventArgs e)
    {
      if (this.suppressUnitsChanged || this.commonLineGapUpDown == null)
      {
        return;
      }

      // SelectionChanged runs after the ComboBox has moved to its new item. Commit using the units that
      // were on screen BEFORE the change, then redraw the same physical distance in the new units.
      this.CommitCommonLineGap(this.displayedUnitsMm);
      bool oldMm = this.displayedUnitsMm;
      bool newMm = this.UnitsMm;
      if (oldMm != newMm)
      {
        this.ConvertSpacingAndMargin(oldMm, newMm);
      }

      this.displayedUnitsMm = newMm;
      this.UpdateUnitPresentation();
    }

    private void ConvertSpacingAndMargin(bool fromMm, bool toMm)
    {
      this.spacingUpDown?.CommitInput();
      this.marginUpDown?.CommitInput();

      if (this.spacingUpDown?.Value is double spacing && spacing >= 0)
      {
        this.spacingUpDown.Value = ConvertDistance(spacing, fromMm, toMm);
      }

      if (this.marginUpDown?.Value is double margin && margin >= 0)
      {
        this.marginUpDown.Value = ConvertDistance(margin, fromMm, toMm);
      }
    }

    private void OnResetCommonLineGap(object sender, RoutedEventArgs e)
    {
      this.commonLineGapInches = DeepNestLib.Placement.CommonLineDefaults.DefaultGapInches;
      this.ShowCommonLineGap();
    }

    private void OnCommonLineClusterModeChanged(object sender, SelectionChangedEventArgs e)
    {
      this.UpdateCommonLineClusterLimitPresentation();
    }

    private void OnResetCommonLineMaxClusterParts(object sender, RoutedEventArgs e)
    {
      this.commonLineMaxClusterParts = DeepNestLib.Placement.CommonLineDefaults.DefaultMaxClusterParts;
      this.commonLineClusterModeCombo.SelectedIndex = 0;
      this.commonLineMaxClusterPartsUpDown.Value =
        DeepNestLib.Placement.CommonLineDefaults.MinimumMaxClusterParts;
      this.UpdateCommonLineClusterLimitPresentation();
    }

    private void UpdateCommonLineClusterLimitPresentation()
    {
      if (this.commonLineMaxClusterPartsUpDown != null)
      {
        this.commonLineMaxClusterPartsUpDown.IsEnabled = this.commonLineClusterModeCombo?.SelectedIndex == 1;
      }
    }

    private void ApplyLocalization()
    {
      this.Title = Localization.Get("Settings_Title");
      if (this.groupNesting != null) this.groupNesting.Header = Localization.Get("Settings_Group_Nesting");
      if (this.rotationsLabel != null) this.rotationsLabel.Text = Localization.Get("Settings_Rotations");
      if (this.rotItem1 != null) this.rotItem1.Content = Localization.Get("Settings_Rot_AsDrawn");
      if (this.rotItem2 != null) this.rotItem2.Content = Localization.Get("Settings_Rot_0_180");
      if (this.rotItem4 != null) this.rotItem4.Content = Localization.Get("Settings_Rot_90");
      if (this.rotItem8 != null) this.rotItem8.Content = Localization.Get("Settings_Rot_45");
      if (this.rotItem36 != null) this.rotItem36.Content = Localization.Get("Settings_Rot_Free");
      if (this.effortLabel != null) this.effortLabel.Text = Localization.Get("Settings_Effort");
      if (this.effortItem0 != null) this.effortItem0.Content = Localization.Get("Settings_Effort_Fast");
      if (this.effortItem1 != null) this.effortItem1.Content = Localization.Get("Settings_Effort_Normal");
      if (this.effortItem2 != null) this.effortItem2.Content = Localization.Get("Settings_Effort_Best");
      if (this.concurrencyLabel != null) this.concurrencyLabel.Text = Localization.Get("Settings_Concurrency");
      this.UpdateConcurrencyAutoText();
      if (this.groupCommonLine != null) this.groupCommonLine.Header = Localization.Get("Settings_Group_CommonLine");
      if (this.btnResetCommonLineGap != null) this.btnResetCommonLineGap.Content = Localization.Get("Settings_Reset");
      if (this.clusterPartsLabel != null) this.clusterPartsLabel.Text = Localization.Get("Settings_MaxCluster");
      if (this.clusterItemAuto != null) this.clusterItemAuto.Content = Localization.Get("Settings_Auto");
      if (this.clusterItemManual != null) this.clusterItemManual.Content = Localization.Get("Settings_Manual");
      if (this.btnResetClusterParts != null) this.btnResetClusterParts.Content = Localization.Get("Settings_Reset");
      if (this.groupDxfExport != null) this.groupDxfExport.Header = Localization.Get("Settings_Group_Dxf");
      if (this.mergeLinesCheck != null) this.mergeLinesCheck.Content = Localization.Get("Settings_MergeLines");
      if (this.differentiateCheck != null) this.differentiateCheck.Content = Localization.Get("Settings_ColorDiff");
      if (this.partNameExportLabel != null) this.partNameExportLabel.Text = Localization.Get("Settings_PartNames");
      if (this.partNameItemNone != null) this.partNameItemNone.Content = Localization.Get("Settings_PartNames_None");
      if (this.partNameItemLayer9 != null) this.partNameItemLayer9.Content = Localization.Get("Settings_PartNames_Layer9");
      if (this.partNameItemBackground != null) this.partNameItemBackground.Content = Localization.Get("Settings_PartNames_Background");
      if (this.groupApp != null) this.groupApp.Header = Localization.Get("Settings_Group_App");
      if (this.unitsLabel != null) this.unitsLabel.Text = Localization.Get("Settings_Units");
      if (this.unitsItemIn != null) this.unitsItemIn.Content = Localization.Get("Settings_Units_In");
      if (this.unitsItemMm != null) this.unitsItemMm.Content = Localization.Get("Settings_Units_Mm");
      if (this.autosaveCheck != null) this.autosaveCheck.Content = Localization.Get("Settings_Autosave");
      if (this.autosaveTimeLabel != null) this.autosaveTimeLabel.Text = Localization.Get("Settings_AutosaveTime");
      if (this.okButton != null) this.okButton.Content = Localization.Get("Btn_OK");
      if (this.cancelButton != null) this.cancelButton.Content = Localization.Get("Btn_Cancel");

      this.UpdateUnitPresentation();
    }

    private void UpdateUnitPresentation()
    {
      string unit = this.displayedUnitsMm ? (Localization.CurrentLanguage == Localization.LangEnglish ? "mm" : "мм") : "in";
      this.spacingLabel.Text = $"{Localization.Get("Settings_Spacing")} ({unit}):";
      this.commonLineGapLabel.Text = $"{Localization.Get("Settings_CommonLineGap")} ({unit}):";
      this.marginLabel.Text = $"{Localization.Get("Settings_Margin")} ({unit}):";
      if (this.spacingUpDown != null)
      {
        this.spacingUpDown.Increment = this.displayedUnitsMm ? 0.5 : 0.05;
        this.spacingUpDown.FormatString = this.displayedUnitsMm ? "0.##" : "0.###";
      }

      if (this.marginUpDown != null)
      {
        this.marginUpDown.Increment = this.displayedUnitsMm ? 0.5 : 0.05;
        this.marginUpDown.FormatString = this.displayedUnitsMm ? "0.##" : "0.###";
      }

      this.ShowCommonLineGap();
    }

    private void ShowCommonLineGap()
    {
      double unitScale = this.displayedUnitsMm
        ? DeepNestLib.Placement.CommonLineDefaults.InchesToMillimeters
        : 1.0;
      this.commonLineGapUpDown.FormatString = this.displayedUnitsMm ? "0.#####" : "0.000##";
      this.commonLineGapUpDown.Minimum =
        2 * DeepNestLib.Placement.CommonLineDefaults.DefaultGeometryToleranceInches * unitScale;
      this.commonLineGapUpDown.Increment = 0.001 * unitScale;
      this.commonLineGapUpDown.Value = CommonLineGapForDisplay(this.commonLineGapInches, this.displayedUnitsMm);
    }

    private void CommitCommonLineGap(bool unitsMm)
    {
      this.commonLineGapUpDown.CommitInput();
      double fallback = CommonLineGapForDisplay(this.commonLineGapInches, unitsMm);
      double displayed = System.Math.Max(0, this.commonLineGapUpDown.Value ?? fallback);
      this.commonLineGapInches = NormalizeCommonLineGapInches(CommonLineGapFromDisplay(displayed, unitsMm));
    }

    private static double NormalizeCommonLineGapInches(double value)
      => value > DeepNestLib.Placement.CommonLineDefaults.DefaultGeometryToleranceInches
        && !double.IsNaN(value)
        && !double.IsInfinity(value)
        ? value
        : DeepNestLib.Placement.CommonLineDefaults.DefaultGapInches;

    internal static int NormalizeCommonLineMaxClusterParts(int value)
      => DeepNestLib.Placement.CommonLineDefaults.NormalizeMaxClusterParts(value);

    private void CommitCommonLineMaxClusterParts()
    {
      if (this.commonLineClusterModeCombo.SelectedIndex != 1)
      {
        this.commonLineMaxClusterParts = DeepNestLib.Placement.CommonLineDefaults.AutoMaxClusterParts;
        return;
      }

      this.commonLineMaxClusterPartsUpDown.CommitInput();
      this.commonLineMaxClusterParts = NormalizeCommonLineMaxClusterParts(
        this.commonLineMaxClusterPartsUpDown.Value
          ?? DeepNestLib.Placement.CommonLineDefaults.MinimumMaxClusterParts);
    }

    private void OnOk(object sender, RoutedEventArgs e)
    {
      // Xceed up/downs commit typed text on focus loss; Enter (OK is IsDefault) would keep stale values.
      this.spacingUpDown.CommitInput();
      this.marginUpDown.CommitInput();
      this.minutesUpDown.CommitInput();
      this.CommitCommonLineGap(this.displayedUnitsMm);
      this.CommitCommonLineMaxClusterParts();

      // Settings-backed properties persist in their setters; Rotations and the autosave options are
      // persisted by the caller (SessionState).
      this.config.Rotations = Rotations;
      this.config.Spacing = System.Math.Max(0, this.spacingUpDown.Value ?? this.config.Spacing);
      this.config.SheetSpacing = System.Math.Max(0, this.marginUpDown.Value ?? this.config.SheetSpacing);
      this.config.MergeLines = this.mergeLinesCheck.IsChecked == true;
      this.config.DifferentiateChildren = this.differentiateCheck.IsChecked == true;
      this.config.DxfPartNameExport = this.partNameExportCombo.SelectedIndex switch
      {
        0 => DxfPartNameExport.None,
        1 => DxfPartNameExport.Layer9,
        2 => DxfPartNameExport.Background,
        _ => DxfPartNameExport.Background,
      };

      this.DialogResult = true;
    }
  }
}
