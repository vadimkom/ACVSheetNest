namespace DeepNestSharp.Ui.Converters
{
  using System;
  using System.Globalization;
  using System.Windows.Data;

  public class PriorityToOptionConverter : IValueConverter
  {
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
      if (value is int p)
      {
        return PriorityHelper.NormalizePriority(p);
      }

      return 5;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
      if (value is int p)
      {
        return p;
      }

      return 5;
    }
  }
}
