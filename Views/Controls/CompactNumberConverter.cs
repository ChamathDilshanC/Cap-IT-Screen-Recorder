using Microsoft.UI.Xaml.Data;
using System.Globalization;

namespace ScreenRecorderApp.Views.Controls;

public sealed class CompactNumberConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        value is IFormattable number ? number.ToString("0.##", CultureInfo.CurrentCulture) : value;
    public object ConvertBack(object value, Type targetType, object parameter, string language) => value;
}
