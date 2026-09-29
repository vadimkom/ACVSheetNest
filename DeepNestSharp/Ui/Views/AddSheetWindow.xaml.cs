namespace DeepNestSharp.Ui.Views
{
  using System;
  using System.Linq;
  using System.Windows;
  using Localization = DeepNestSharp.Ui.Localization;

  /// <summary>Small dialog for a custom stock sheet size (the Add Sheet menu's "Custom size…").</summary>
  public partial class AddSheetWindow : Window
  {
    private readonly bool isUnitsMm;

    public AddSheetWindow()
      : this(true)
    {
    }

    /// <summary>Units drive labels, limits and defaults: metric shops need 3000x1500 sheets.</summary>
    public AddSheetWindow(bool unitsMm)
    {
      InitializeComponent();
      this.isUnitsMm = unitsMm;
      if (unitsMm)
      {
        this.widthUpDown.Maximum = 25000;
        this.heightUpDown.Maximum = 25000;
        this.widthUpDown.Value = 3000;
        this.heightUpDown.Value = 1500;
      }
      else
      {
        this.widthUpDown.Maximum = 1000;
        this.heightUpDown.Maximum = 1000;
        this.widthUpDown.Value = 120;
        this.heightUpDown.Value = 60;
      }

      this.priorityCombo.SelectedValue = 5;
      ApplyLocalization();

      // Typing a size is what the dialog is for: open in the width field with its number selected, so the
      // first keystroke replaces it. Queued at Input priority, after the window has taken focus itself.
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

    private void ApplyLocalization()
    {
      string unit = this.isUnitsMm ? "мм" : "in";
      this.Title = Localization.Get("Sheet_Dlg_Title");
      this.headerText.Text = string.Format(Localization.Get("Sheet_Dlg_Header"), this.isUnitsMm ? (Localization.CurrentLanguage == Localization.LangEnglish ? "millimeters" : "миллиметрах") : (Localization.CurrentLanguage == Localization.LangEnglish ? "inches" : "дюймах"));
      this.widthLabel.Text = string.Format(Localization.Get("Sheet_Dlg_Width"), unit);
      this.heightLabel.Text = string.Format(Localization.Get("Sheet_Dlg_Height"), unit);
      this.qtyLabel.Text = Localization.Get("Sheet_Dlg_Qty");
      this.priorityLabel.Text = Localization.Get("Sheet_Dlg_Priority");
      this.unlimitedCheck.Content = Localization.Get("Sheet_Dlg_Unlimited");
      this.rememberCheck.Content = Localization.Get("Sheet_Dlg_Remember");
      this.okButton.Content = Localization.Get("Sheet_Dlg_Add");
      this.cancelButton.Content = Localization.Get("Btn_Cancel");
    }

    /// <summary>Reuses the dialog to EDIT an existing stock row: pre-filled values, edit wording.</summary>
    public void PrefillForEdit(int width, int height, int quantity, bool unlimited = false, int priority = 5)
    {
      this.Title = Localization.Get("Sheet_Dlg_Title_Edit");
      this.okButton.Content = Localization.Get("Btn_OK");
      this.widthUpDown.Value = width;
      this.heightUpDown.Value = height;
      this.qtyUpDown.Value = quantity;
      this.unlimitedCheck.IsChecked = unlimited;
      this.priorityCombo.SelectedValue = priority;
    }

    public int SheetWidth { get; private set; } = 3000;

    public int SheetHeight { get; private set; } = 1500;

    public int SheetQuantity { get; private set; } = 1;

    public int SheetPriority { get; private set; } = 5;

    /// <summary>The job may take as many of this size as it needs.</summary>
    public bool SheetUnlimited { get; private set; }

    /// <summary>Save this size under "My sheets" in the Add Sheet menu.</summary>
    public bool RememberAsPreset => this.rememberCheck.IsChecked == true;

    /// <summary>Quantity means nothing while the size is unlimited, so it greys out. Its VALUE is left
    /// alone: unticking must give back the number that was there, not a default.</summary>
    private void OnUnlimitedChanged(object sender, RoutedEventArgs e)
      => this.qtyUpDown.IsEnabled = this.unlimitedCheck.IsChecked != true;

    private void OnOk(object sender, RoutedEventArgs e)
    {
      // Xceed up/downs only commit TYPED text on focus loss — commit before reading so pressing
      // Enter doesn't silently save the previous value.
      this.widthUpDown.CommitInput();
      this.heightUpDown.CommitInput();
      this.qtyUpDown.CommitInput();

      this.SheetWidth = this.widthUpDown.Value ?? (this.isUnitsMm ? 3000 : 120);
      this.SheetHeight = this.heightUpDown.Value ?? (this.isUnitsMm ? 1500 : 60);
      this.SheetQuantity = this.qtyUpDown.Value ?? 1;
      this.SheetUnlimited = this.unlimitedCheck.IsChecked == true;
      this.SheetPriority = (this.priorityCombo.SelectedValue as int?) ?? 5;
      this.DialogResult = true;
    }
  }
}
