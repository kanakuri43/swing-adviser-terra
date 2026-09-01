using System.Globalization;
using System.Windows.Data;

namespace SwingAdviser.Presentation;

public sealed class BooleanNegationConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is not true;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => value is not true;
}
