namespace DeepNestSharp.Ui.Views
{
  using System;
  using System.Collections.Generic;
  using System.Linq;
  using System.Windows;
  using System.Windows.Controls;
  using DeepNestLib.IO;
  using Xceed.Wpf.Toolkit;
  using Localization = DeepNestSharp.Ui.Localization;

  /// <summary>
  /// A part without a drawing: one of <see cref="PartShapes.Kinds"/>, a preset or custom sizes, and how many.
  /// The fields are built from the shape, so a new shape needs no change here.
  /// </summary>
  public partial class AddShapeWindow : Window
  {
    // The next shape usually looks like the last one: keep what was typed, per shape, for the session.
    private static readonly Dictionary<ShapeKind, double[]> LastValues = new Dictionary<ShapeKind, double[]>();
    private static readonly Dictionary<ShapeKind, int> LastPreset = new Dictionary<ShapeKind, int>();
    private static ShapeKind lastKind = PartShapes.Rectangle;
    private static int lastQuantity = 1;
    private static bool lastFillSheets;

    private readonly bool millimetres;
    private readonly string unit;
    private readonly ComboBox kindCombo = new ComboBox { MinWidth = 170 };
    private readonly ComboBox presetCombo = new ComboBox { MinWidth = 170 };
    private readonly IntegerUpDown qtyUpDown = new IntegerUpDown { Minimum = 1, Maximum = 100000, Value = 1 };
    private readonly CheckBox fillCheck = new CheckBox { VerticalAlignment = VerticalAlignment.Center };
    private readonly List<DoubleUpDown> fields = new List<DoubleUpDown>();
    private bool building;

    public AddShapeWindow(bool millimetres)
    {
      InitializeComponent();
      this.millimetres = millimetres;
      this.unit = Localization.Get(millimetres ? "Unit_Mm" : "Unit_In");

      foreach (var kind in PartShapes.Kinds)
      {
        this.kindCombo.Items.Add(Localization.Get(kind.NameKey));
      }

      this.kindCombo.SelectionChanged += (_, __) => this.Build();
      this.presetCombo.SelectionChanged += (_, __) => this.ApplyPreset();
      this.qtyUpDown.Value = lastQuantity;
      this.fillCheck.IsChecked = lastFillSheets;
      this.fillCheck.Checked += (_, __) => this.qtyUpDown.IsEnabled = false;
      this.fillCheck.Unchecked += (_, __) => this.qtyUpDown.IsEnabled = true;
      this.qtyUpDown.IsEnabled = !lastFillSheets;
      this.kindCombo.SelectedIndex = Math.Max(0, PartShapes.Kinds.ToList().IndexOf(lastKind));

      this.Loaded += (_, __) => this.Dispatcher.BeginInvoke(
        System.Windows.Threading.DispatcherPriority.Input,
        new Action(() =>
        {
          Control first = this.fields.FirstOrDefault(f => f.IsEnabled) ?? (Control)this.qtyUpDown;
          first.Focus();
          if (first.Template?.FindName("PART_TextBox", first) is TextBox box)
          {
            box.Focus();
            box.SelectAll();
          }
        }));
    }

    public ShapeKind Kind { get; private set; }

    /// <summary>The shape's parameters, in the drawing's units.</summary>
    public IReadOnlyList<double> Values { get; private set; }

    /// <summary>The preset chosen, or null for custom sizes.</summary>
    public ShapePreset Preset { get; private set; }

    public int Quantity { get; private set; }

    /// <summary>
    /// For a shape drawn to the metal it is cut from (tape): the length and width typed are the SHEET, and
    /// this turns a side of it into the room the shape gets on it (less the edge margins and a part gap).
    /// Null: the size typed is the shape's own.
    /// </summary>
    public Func<double, double> SheetRoom
    {
      get => this.sheetRoom;
      set
      {
        // Set after the constructor has drawn the fields: draw them again, with the sheet's labels.
        this.sheetRoom = value;
        this.Build();
      }
    }

    private Func<double, double> sheetRoom;

    /// <summary>The sheet the shape was drawn for (length, width), to go into the job with it; null when the
    /// size typed was the shape's own.</summary>
    public (double Length, double Width)? FitSheet { get; private set; }

    /// <summary>The values to draw: as typed, or, for a shape fitted to a sheet, with the room on it.</summary>
    private double[] Fitted(ShapeKind kind, double[] typed)
    {
      if (this.SheetRoom == null || kind.Fit is not (int length, int width))
      {
        return typed;
      }

      var values = (double[])typed.Clone();
      values[length] = this.SheetRoom(typed[length]);
      values[width] = this.SheetRoom(typed[width]);
      return values;
    }

    /// <summary>No count: as many as the job's sheets have room for once the counted parts are on.</summary>
    public bool FillSheets { get; private set; }

    private ShapeKind CurrentKind => PartShapes.Kinds[Math.Max(0, this.kindCombo.SelectedIndex)];

    private ShapePreset CurrentPreset
      => this.presetCombo.SelectedIndex >= 0 && this.presetCombo.SelectedIndex < this.CurrentKind.Presets.Count
        ? this.CurrentKind.Presets[this.presetCombo.SelectedIndex]
        : null;

    /// <summary>Millimetres to the drawing's units.</summary>
    private double FromMm(double mm) => this.millimetres ? mm : Math.Round(mm / 25.4, 4);

    /// <summary>Lays out the rows for the chosen shape: shape, preset (if it has any), its parameters, quantity.</summary>
    private void Build()
    {
      this.building = true;
      var kind = this.CurrentKind;
      var grid = this.fieldsGrid;
      grid.Children.Clear();
      grid.RowDefinitions.Clear();
      this.fields.Clear();
      int row = 0;

      void Row(string labelKey, FrameworkElement field, bool withUnit)
      {
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var label = new TextBlock { Text = Localization.Get(labelKey), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 6) };
        Grid.SetRow(label, row);
        grid.Children.Add(label);
        field.Margin = new Thickness(0, 0, 0, 6);
        Grid.SetRow(field, row);
        Grid.SetColumn(field, 1);
        if (field is ComboBox)
        {
          Grid.SetColumnSpan(field, 2);
        }

        grid.Children.Add(field);
        if (withUnit)
        {
          var units = new TextBlock { Text = this.unit, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 6), Foreground = (System.Windows.Media.Brush)this.FindResource("Brush.TextMuted") };
          Grid.SetRow(units, row);
          Grid.SetColumn(units, 2);
          grid.Children.Add(units);
        }

        row++;
      }

      Row("Shape_Kind", this.kindCombo, false);

      this.presetCombo.Items.Clear();
      if (kind.Presets.Count > 0)
      {
        foreach (var preset in kind.Presets)
        {
          this.presetCombo.Items.Add(Localization.Get(preset.LabelKey) + " " + preset.Detail);
        }

        this.presetCombo.Items.Add(Localization.Get("Shape_Custom"));
        Row("Shape_Preset", this.presetCombo, false);
      }

      double[] remembered = LastValues.TryGetValue(kind, out var v) ? v : kind.Parameters.Select(p => this.FromMm(p.DefaultMm)).ToArray();
      for (int i = 0; i < kind.Parameters.Count; i++)
      {
        var field = new DoubleUpDown { Minimum = 0, Maximum = 100000, FormatString = "0.###", Value = remembered[i] };
        field.ValueChanged += (_, __) => this.ShowSummary();
        this.fields.Add(field);
        // For a blank drawn to its sheet the length and width typed ARE the sheet; the labels say so.
        string label = this.SheetRoom != null && kind.Fit is (int fitLength, int fitWidth) && (i == fitLength || i == fitWidth)
          ? (i == fitLength ? "Shape_SheetLength" : "Shape_SheetWidth")
          : kind.Parameters[i].LabelKey;
        Row(label, field, true);
      }

      Row("Shape_Quantity", this.qtyUpDown, false);

      // ...or no count at all: the sheets chosen in the next step are filled with it (a sheet of washers).
      this.fillCheck.Content = Localization.Get("Part_FillSheets");
      this.fillCheck.ToolTip = Localization.Get("Tip_PartFill");
      this.fillCheck.Margin = new Thickness(10, 0, 0, 6);
      Grid.SetRow(this.fillCheck, row - 1);
      Grid.SetColumn(this.fillCheck, 2);
      grid.Children.Add(this.fillCheck);

      // A blank drawn to a sheet (tape, profile) already fills it: no box to tick, and one left ticked from the
      // washers must not turn it into a filler that adds no sheet and starts no nest.
      bool fitted = kind.Fit != null;
      this.fillCheck.Visibility = fitted ? Visibility.Collapsed : Visibility.Visible;
      this.qtyUpDown.IsEnabled = fitted || this.fillCheck.IsChecked != true;

      this.building = false;
      if (kind.Presets.Count > 0)
      {
        this.presetCombo.SelectedIndex = LastPreset.TryGetValue(kind, out int p) ? p : 0;
      }

      this.ApplyPreset();
    }

    /// <summary>A preset fills in and locks the values it sets; custom unlocks them.</summary>
    private void ApplyPreset()
    {
      if (this.building)
      {
        return;
      }

      var preset = this.CurrentPreset;
      for (int i = 0; i < this.fields.Count; i++)
      {
        bool set = preset != null && preset.ValuesMm.ContainsKey(i);
        if (set)
        {
          this.fields[i].Value = this.FromMm(preset.ValuesMm[i]);
        }

        this.fields[i].IsEnabled = !set;
      }

      this.ShowSummary();
    }

    private double[] Read()
    {
      foreach (var f in this.fields)
      {
        f.CommitInput();
      }

      return this.fields.Select(f => f.Value ?? 0).ToArray();
    }

    /// <summary>What the values will make, or what is wrong with them.</summary>
    private void ShowSummary()
    {
      if (this.building || this.fields.Count == 0)
      {
        return;
      }

      var typed = this.fields.Select(f => f.Value ?? 0).ToArray();
      var values = this.Fitted(this.CurrentKind, typed);
      string problem = this.CurrentKind.Problem(values);
      string summary = problem == null ? this.CurrentKind.Describe(values, Localization.Get, this.unit) : null;
      if (summary != null && !ReferenceEquals(values, typed) && this.CurrentKind.Fit is (int length, int width))
      {
        summary += Environment.NewLine + Localization.Format(
          "Shape_FitSheetNote",
          typed[length].ToString("0.##", System.Globalization.CultureInfo.CurrentCulture),
          typed[width].ToString("0.##", System.Globalization.CultureInfo.CurrentCulture),
          this.unit);
      }
      this.problemText.Text = problem == null ? string.Empty : Localization.Get(problem);
      this.problemText.Visibility = problem == null ? Visibility.Collapsed : Visibility.Visible;
      this.summaryText.Text = summary ?? string.Empty;
      this.summaryText.Visibility = summary == null ? Visibility.Collapsed : Visibility.Visible;
      this.addButton.IsEnabled = problem == null;
    }

    /// <summary>The parts library to save into; no button without one.</summary>
    public string LibraryFolder { get; set; }

    private static string lastLibraryGroup;

    /// <summary>
    /// Keeps the shape in the parts library. A shape drawn to the metal it is cut from (tape) goes in as a
    /// pattern without its length and width, so an offcut it fills draws it to its own size; any other shape
    /// goes in as the drawing of the size typed.
    /// </summary>
    private void OnSaveToLibrary(object sender, RoutedEventArgs e)
    {
      var values = this.Read();
      var kind = this.CurrentKind;
      if (kind.Problem(values) != null || string.IsNullOrWhiteSpace(this.LibraryFolder))
      {
        this.ShowSummary();
        return;
      }

      PartLibrary.CreateIfMissing(this.LibraryFolder, Localization.Get);
      var groups = PartLibrary.Groups(this.LibraryFolder, string.Empty).Select(g => g.Name).Where(g => g.Length > 0).ToList();
      bool pattern = kind.Fit != null;
      string group = lastLibraryGroup ?? (pattern ? Localization.Get(ReferenceEquals(kind, PartShapes.Profile) ? "Library_ProfileGroup" : "Library_TapeGroup") : groups.FirstOrDefault() ?? Localization.Get("Library_DefaultGroup"));
      var preset = this.CurrentPreset;
      string name = pattern ? kind.PatternName(values, Localization.Get) : kind.PartName(values, preset, Localization.Get);
      var dialog = new LibrarySaveWindow(groups, group, name, Localization.Get(pattern ? "Library_PatternNote" : "Library_DrawingNote")) { Owner = this };
      if (dialog.ShowDialog() != true)
      {
        return;
      }

      string folder = System.IO.Path.Combine(this.LibraryFolder, PartLibrary.SafeName(dialog.GroupName));
      try
      {
        if (pattern)
        {
          PartLibrary.SavePattern(folder, dialog.ItemName, ShapePattern.Of(kind, values, this.millimetres));
        }
        else
        {
          PartShapes.Write(folder, dialog.ItemName, kind, values, this.millimetres);
        }
      }
      catch (Exception ex)
      {
        System.Windows.MessageBox.Show(this, ex.GetBaseException().Message, "SheetNest", MessageBoxButton.OK, MessageBoxImage.Warning);
        return;
      }

      lastLibraryGroup = dialog.GroupName;
      this.summaryText.Text = Localization.Format("Library_Saved", dialog.GroupName, dialog.ItemName);
      this.summaryText.Visibility = Visibility.Visible;
    }

    private void OnOk(object sender, RoutedEventArgs e)
    {
      // Typed text only reaches Value on focus loss, and Enter presses this button without one.
      var typed = this.Read();
      this.qtyUpDown.CommitInput();
      var kind = this.CurrentKind;
      var values = this.Fitted(kind, typed);
      if (kind.Problem(values) != null)
      {
        this.ShowSummary();
        return;
      }

      this.Kind = kind;
      this.Values = values;
      this.FitSheet = !ReferenceEquals(values, typed) && kind.Fit is (int fitLength, int fitWidth)
        ? (typed[fitLength], typed[fitWidth])
        : null;
      this.Preset = this.CurrentPreset;
      this.Quantity = this.qtyUpDown.Value ?? 1;

      lastKind = kind;
      lastQuantity = this.Quantity;
      lastFillSheets = this.fillCheck.IsChecked == true;
      this.FillSheets = lastFillSheets && kind.Fit == null;
      LastValues[kind] = typed;
      if (kind.Presets.Count > 0)
      {
        LastPreset[kind] = this.presetCombo.SelectedIndex;
      }

      this.DialogResult = true;
    }
  }
}
