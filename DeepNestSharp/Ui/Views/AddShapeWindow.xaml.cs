namespace DeepNestSharp.Ui.Views
{
  using System;
  using System.Windows;
  using System.Windows.Controls;
  using DeepNestLib.IO;
  using Localization = DeepNestSharp.Ui.Localization;

  /// <summary>A rectangle, a circle or a washer, its size and how many: a part without a drawing.</summary>
  public partial class AddShapeWindow : Window
  {
    private const int StandardWasher = 0;
    private const int WideWasher = 1;
    private const int CustomWasher = 2;

    // The next shape usually looks like the last one: keep what was typed for the session.
    private static PartShape lastShape = PartShape.Rectangle;
    private static double lastWidth = 100;
    private static double lastHeight = 100;
    private static int lastQuantity = 1;
    private static int lastWasherKind = StandardWasher;
    private static int lastWasherSize;
    private static double lastWasherOuter = 30;
    private static double lastWasherHole = 10;

    private readonly bool millimetres;
    private bool ready;

    public AddShapeWindow(bool millimetres)
    {
      InitializeComponent();
      this.millimetres = millimetres;
      string units = Localization.Get(millimetres ? "Unit_Mm" : "Unit_In");

      this.widthUnits.Text = units;
      this.heightUnits.Text = units;
      this.qtyUpDown.Value = lastQuantity;
      this.washerSizeCombo.ItemsSource = PartShapes.Washers;
      this.washerSizeCombo.SelectedIndex = lastWasherSize;
      this.washerKindCombo.SelectedIndex = lastWasherKind;
      this.circleRadio.IsChecked = lastShape == PartShape.Circle;
      this.washerRadio.IsChecked = lastShape == PartShape.Washer;
      this.ready = true;
      this.ShowShape(restore: true);

      this.Loaded += (_, __) => this.Dispatcher.BeginInvoke(
        System.Windows.Threading.DispatcherPriority.Input,
        new Action(() =>
        {
          var first = this.widthUpDown.IsEnabled ? this.widthUpDown : (Control)this.qtyUpDown;
          first.Focus();
          if (first.Template?.FindName("PART_TextBox", first) is TextBox box)
          {
            box.Focus();
            box.SelectAll();
          }
        }));
    }

    public PartShape Shape { get; private set; }

    /// <summary>The width, the diameter of a circle, or the outside of a washer.</summary>
    public double ShapeWidth { get; private set; }

    /// <summary>The height, or the hole of a washer.</summary>
    public double ShapeHeight { get; private set; }

    public int Quantity { get; private set; }

    /// <summary>"M8" for a standard or wide washer; null for any other shape.</summary>
    public string WasherThread { get; private set; }

    public bool WideWasherChosen { get; private set; }

    private PartShape Chosen => this.washerRadio.IsChecked == true ? PartShape.Washer
      : this.circleRadio.IsChecked == true ? PartShape.Circle : PartShape.Rectangle;

    private void OnShapeChanged(object sender, RoutedEventArgs e) => this.ShowShape(restore: true);

    private void OnWasherChanged(object sender, SelectionChangedEventArgs e) => this.ShowShape(restore: false);

    /// <summary>Labels and fields for the chosen shape. A standard or wide washer fills in its sizes and locks
    /// them; restore puts back what was last typed for the shape.</summary>
    private void ShowShape(bool restore)
    {
      if (!this.ready)
      {
        return;
      }

      var shape = this.Chosen;
      bool washer = shape == PartShape.Washer;
      bool standard = washer && this.washerKindCombo.SelectedIndex != CustomWasher;

      var washerVisibility = washer ? Visibility.Visible : Visibility.Collapsed;
      this.washerKindLabel.Visibility = washerVisibility;
      this.washerKindPanel.Visibility = washerVisibility;
      this.washerSizeCombo.Visibility = standard ? Visibility.Visible : Visibility.Collapsed;

      this.widthLabel.Text = Localization.Get(shape switch
      {
        PartShape.Circle => "Shape_Diameter",
        PartShape.Washer => "Shape_OuterDiameter",
        _ => "Shape_Width",
      });
      this.heightLabel.Text = Localization.Get(washer ? "Shape_HoleDiameter" : "Shape_Height");
      var heightVisibility = shape == PartShape.Circle ? Visibility.Collapsed : Visibility.Visible;
      this.heightLabel.Visibility = heightVisibility;
      this.heightUpDown.Visibility = heightVisibility;
      this.heightUnits.Visibility = heightVisibility;

      this.widthUpDown.IsEnabled = !standard;
      this.heightUpDown.IsEnabled = !standard;
      if (standard && this.washerSizeCombo.SelectedItem is WasherSize size)
      {
        double scale = this.millimetres ? 1 : 1 / 25.4;
        this.widthUpDown.Value = Math.Round((this.washerKindCombo.SelectedIndex == WideWasher ? size.WideOuter : size.Outer) * scale, 3);
        this.heightUpDown.Value = Math.Round(size.Hole * scale, 3);
      }
      else if (restore || (washer && !standard))
      {
        this.widthUpDown.Value = washer ? lastWasherOuter : lastWidth;
        this.heightUpDown.Value = washer ? lastWasherHole : lastHeight;
      }
    }

    private void OnOk(object sender, RoutedEventArgs e)
    {
      // Typed text only reaches Value on focus loss, and Enter presses this button without one.
      this.widthUpDown.CommitInput();
      this.heightUpDown.CommitInput();
      this.qtyUpDown.CommitInput();

      this.Shape = this.Chosen;
      this.ShapeWidth = this.widthUpDown.Value ?? 0;
      this.ShapeHeight = this.Shape == PartShape.Circle ? this.ShapeWidth : this.heightUpDown.Value ?? 0;
      this.Quantity = this.qtyUpDown.Value ?? 1;
      if (!(this.ShapeWidth > 0) || !(this.ShapeHeight > 0))
      {
        return;
      }

      bool standard = this.Shape == PartShape.Washer && this.washerKindCombo.SelectedIndex != CustomWasher;
      if (this.Shape == PartShape.Washer && !(this.ShapeHeight < this.ShapeWidth))
      {
        this.heightUpDown.Focus();
        return;
      }

      this.WasherThread = standard ? (this.washerSizeCombo.SelectedItem as WasherSize)?.Thread : null;
      this.WideWasherChosen = standard && this.washerKindCombo.SelectedIndex == WideWasher;

      lastShape = this.Shape;
      lastQuantity = this.Quantity;
      if (this.Shape == PartShape.Washer)
      {
        lastWasherKind = this.washerKindCombo.SelectedIndex;
        lastWasherSize = Math.Max(0, this.washerSizeCombo.SelectedIndex);
        if (!standard)
        {
          lastWasherOuter = this.ShapeWidth;
          lastWasherHole = this.ShapeHeight;
        }
      }
      else
      {
        lastWidth = this.ShapeWidth;
        lastHeight = this.Shape == PartShape.Circle ? lastHeight : this.ShapeHeight;
      }

      this.DialogResult = true;
    }
  }
}
