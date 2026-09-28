namespace DeepNestSharp.Ui
{
  using System.Collections.ObjectModel;
  using System.ComponentModel;

  public enum PriorityLevel
  {
    Highest = 1,
    High = 3,
    Normal = 5,
    Low = 7,
    Lowest = 10,
  }

  public class PriorityOptionItem : INotifyPropertyChanged
  {
    private string display;

    public int Value { get; }

    public PriorityLevel Level { get; }

    public string Display
    {
      get => this.display;
      set
      {
        if (this.display != value)
        {
          this.display = value;
          this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(this.Display)));
        }
      }
    }

    public PriorityOptionItem(int value, PriorityLevel level, string display)
    {
      this.Value = value;
      this.Level = level;
      this.display = display;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public override string ToString() => this.Display;
  }

  public static class PriorityHelper
  {
    public static ObservableCollection<PriorityOptionItem> CurrentOptions { get; } =
      new ObservableCollection<PriorityOptionItem>
      {
        new PriorityOptionItem(1, PriorityLevel.Highest, "Наивысший"),
        new PriorityOptionItem(3, PriorityLevel.High, "Высокий"),
        new PriorityOptionItem(5, PriorityLevel.Normal, "Обычный"),
        new PriorityOptionItem(7, PriorityLevel.Low, "Низкий"),
        new PriorityOptionItem(10, PriorityLevel.Lowest, "Наименьший"),
      };

    public static int NormalizePriority(int priority)
    {
      if (priority <= 1) return 1;
      if (priority <= 3) return 3;
      if (priority <= 6) return 5;
      if (priority <= 9) return 7;
      return 10;
    }

    public static string GetLabel(int priority, string language)
    {
      int norm = NormalizePriority(priority);
      return norm switch
      {
        1 => GetLabel(PriorityLevel.Highest, language),
        3 => GetLabel(PriorityLevel.High, language),
        5 => GetLabel(PriorityLevel.Normal, language),
        7 => GetLabel(PriorityLevel.Low, language),
        _ => GetLabel(PriorityLevel.Lowest, language),
      };
    }

    public static string GetLabel(PriorityLevel level, string language)
    {
      string lang = Localization.NormalizeLanguage(language);
      return level switch
      {
        PriorityLevel.Highest => Localization.Get("Priority_Highest"),
        PriorityLevel.High => Localization.Get("Priority_High"),
        PriorityLevel.Normal => Localization.Get("Priority_Normal"),
        PriorityLevel.Low => Localization.Get("Priority_Low"),
        PriorityLevel.Lowest => Localization.Get("Priority_Lowest"),
        _ => Localization.Get("Priority_Normal"),
      };
    }

    public static void UpdateLanguage(string language)
    {
      foreach (var item in CurrentOptions)
      {
        item.Display = GetLabel(item.Level, language);
      }
    }
  }
}
