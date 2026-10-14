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

    private readonly bool millimetres;
    private readonly string unit;
    private readonly ComboBox kindCombo = new ComboBox { MinWidth = 170 };
    private readonly ComboBox presetCombo = new ComboBox { MinWidth = 170 };
    private readonly IntegerUpDown qtyUpDown = new IntegerUpDown { Minimum = 1, Maximum = 100000, Value = 1 };
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
        Row(kind.Parameters[i].LabelKey, field, true);
      }

      Row("Shape_Quantity", this.qtyUpDown, false);

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

      var values = this.fields.Select(f => f.Value ?? 0).ToArray();
      string problem = this.CurrentKind.Problem(values);
      string summary = problem == null ? this.CurrentKind.Describe(values, Localization.Get, this.unit) : null;
      this.problemText.Text = problem == null ? string.Empty : Localization.Get(problem);
      this.problemText.Visibility = problem == null ? Visibility.Collapsed : Visibility.Visible;
      this.summaryText.Text = summary ?? string.Empty;
      this.summaryText.Visibility = summary == null ? Visibility.Collapsed : Visibility.Visible;
      this.addButton.IsEnabled = problem == null;
    }

    private void OnOk(object sender, RoutedEventArgs e)
    {
      // Typed text only reaches Value on focus loss, and Enter presses this button without one.
      var values = this.Read();
      this.qtyUpDown.CommitInput();
      var kind = this.CurrentKind;
      if (kind.Problem(values) != null)
      {
        this.ShowSummary();
        return;
      }

      this.Kind = kind;
      this.Values = values;
      this.Preset = this.CurrentPreset;
      this.Quantity = this.qtyUpDown.Value ?? 1;

      lastKind = kind;
      lastQuantity = this.Quantity;
      LastValues[kind] = values;
      if (kind.Presets.Count > 0)
      {
        LastPreset[kind] = this.presetCombo.SelectedIndex;
      }

      this.DialogResult = true;
    }
  }
}
