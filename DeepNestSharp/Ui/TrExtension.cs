namespace DeepNestSharp.Ui
{
  using System;
  using System.ComponentModel;
  using System.Windows.Data;
  using System.Windows.Markup;

  /// <summary>
  /// <c>{ui:Tr Key}</c> in XAML: the text for <paramref name="Key"/> in the current language, bound so that
  /// switching language re-reads it everywhere without any code behind the window.
  /// </summary>
  [MarkupExtensionReturnType(typeof(object))]
  public sealed class TrExtension : MarkupExtension
  {
    public TrExtension()
    {
    }

    public TrExtension(string key)
    {
      this.Key = key;
    }

    [ConstructorArgument("key")]
    public string Key { get; set; } = string.Empty;

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
      var binding = new Binding($"[{this.Key}]") { Source = LocalizationSource.Instance, Mode = BindingMode.OneWay };
      return binding.ProvideValue(serviceProvider);
    }
  }

  /// <summary>The indexer <see cref="TrExtension"/> binds to; raises a change for every key when the language changes.</summary>
  public sealed class LocalizationSource : INotifyPropertyChanged
  {
    private LocalizationSource()
    {
      Localization.LanguageChanged += () => this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public static LocalizationSource Instance { get; } = new LocalizationSource();

    public string this[string key] => Localization.Get(key);
  }
}
