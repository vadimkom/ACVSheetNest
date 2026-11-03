namespace DeepNestSharp.Ui.Views
{
  using System;
  using System.Windows;
  using System.Windows.Controls;
  using DeepNestLib.Placement;
  using DeepNestSharp.Ui.UserControls;
  using Localization = DeepNestSharp.Ui.Localization;

  /// <summary>
  /// What the stock ran short of: the parts left over, laid on virtual sheets of a standard size, to see how
  /// many sheets to buy. Shown only, never cut, exported or counted; "Save for later" writes the left-over
  /// parts as a job of their own, to nest when the material comes.
  /// </summary>
  public sealed class VirtualSheetsWindow : Window
  {
    public VirtualSheetsWindow(INestResult virtualNest, string header, Action<DxfViewer> setUp, Action saveForLater)
    {
      this.Title = Localization.Get("Title_VirtualSheets");
      this.Width = 1000;
      this.Height = 760;
      this.WindowStartupLocation = WindowStartupLocation.CenterOwner;
      this.ShowInTaskbar = false;

      var root = new DockPanel { Margin = new Thickness(10) };
      var text = new TextBlock { Text = header, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) };
      DockPanel.SetDock(text, Dock.Top);
      root.Children.Add(text);

      var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };
      var save = new Button { Content = Localization.Get("Btn_SaveForLater"), Margin = new Thickness(0, 0, 8, 0), Padding = new Thickness(12, 4, 12, 4) };
      save.SetResourceReference(StyleProperty, "PrimaryButton");
      save.Click += (_, __) => saveForLater();
      var close = new Button { Content = Localization.Get("Btn_Close"), IsCancel = true, MinWidth = 80, Padding = new Thickness(12, 4, 12, 4) };
      close.SetResourceReference(StyleProperty, "ModernButton");
      close.Click += (_, __) => this.Close();
      buttons.Children.Add(save);
      buttons.Children.Add(close);
      DockPanel.SetDock(buttons, Dock.Bottom);
      root.Children.Add(buttons);

      var viewer = new DxfViewer();
      setUp(viewer);
      viewer.Result = virtualNest;
      root.Children.Add(viewer);
      this.Content = root;
    }
  }
}
