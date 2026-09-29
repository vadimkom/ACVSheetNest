namespace DeepNestSharp.Ui.Views
{
  using System;
  using System.Windows;
  using DeepNestLib.IO;
  using Localization = DeepNestSharp.Ui.Localization;

  /// <summary>A rectangle or a circle, its size and how many: a part without a drawing.</summary>
  public partial class AddShapeWindow : Window
  {
    // The next shape usually looks like the last one: keep what was typed for the session.
    private static PartShape lastShape = PartShape.Rectangle;
    private static double lastWidth = 100;
    private static double lastHeight = 100;
    private static int lastQuantity = 1;

    public AddShapeWindow(bool millimetres)
    {
      InitializeComponent();
      string units = millimetres ? "mm" : "in";
      if (Localization.CurrentLanguage != Localization.LangEnglish)
      {
        units = millimetres ? "мм" : "in";
      }

      this.widthUnits.Text = units;
      this.heightUnits.Text = units;
      this.widthUpDown.Value = lastWidth;
      this.heightUpDown.Value = lastHeight;
      this.qtyUpDown.Value = lastQuantity;
      this.circleRadio.IsChecked = lastShape == PartShape.Circle;
      this.OnShapeChanged(this, null);

      this.Loaded += (_, __) => this.Dispatcher.BeginInvoke(
        System.Windows.Threading.DispatcherPriority.Input,
        new Action(() =>
        {
          this.widthUpDown.Focus();
          if (this.widthUpDown.Template?.FindName("PART_TextBox", this.widthUpDown) is System.Windows.Controls.TextBox box)
          {
            box.Focus();
            box.SelectAll();
          }
        }));
    }

    public PartShape Shape { get; private set; }

    /// <summary>The width, or the diameter of a circle.</summary>
    public double ShapeWidth { get; private set; }

    public double ShapeHeight { get; private set; }

    public int Quantity { get; private set; }

    private void OnShapeChanged(object sender, RoutedEventArgs e)
    {
      if (this.heightUpDown == null)
      {
        return;
      }

      bool circle = this.circleRadio.IsChecked == true;
      this.widthLabel.Text = Localization.Get(circle ? "Shape_Diameter" : "Shape_Width");
      var visibility = circle ? Visibility.Collapsed : Visibility.Visible;
      this.heightLabel.Visibility = visibility;
      this.heightUpDown.Visibility = visibility;
      this.heightUnits.Visibility = visibility;
    }

    private void OnOk(object sender, RoutedEventArgs e)
    {
      // Typed text only reaches Value on focus loss, and Enter presses this button without one.
      this.widthUpDown.CommitInput();
      this.heightUpDown.CommitInput();
      this.qtyUpDown.CommitInput();

      this.Shape = this.circleRadio.IsChecked == true ? PartShape.Circle : PartShape.Rectangle;
      this.ShapeWidth = this.widthUpDown.Value ?? 0;
      this.ShapeHeight = this.Shape == PartShape.Circle ? this.ShapeWidth : this.heightUpDown.Value ?? 0;
      this.Quantity = this.qtyUpDown.Value ?? 1;
      if (!(this.ShapeWidth > 0) || !(this.ShapeHeight > 0))
      {
        return;
      }

      lastShape = this.Shape;
      lastWidth = this.ShapeWidth;
      lastHeight = this.Shape == PartShape.Circle ? lastHeight : this.ShapeHeight;
      lastQuantity = this.Quantity;
      this.DialogResult = true;
    }
  }
}
