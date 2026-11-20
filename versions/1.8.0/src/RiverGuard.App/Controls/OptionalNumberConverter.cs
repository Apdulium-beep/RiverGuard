using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace RiverGuard.Controls;

// Ввод необязательных числовых параметров
public sealed class OptionalNumberConverter : IValueConverter
{
    public object Convert(object value,Type targetType,object parameter,CultureInfo culture)=>value is double number?number.ToString("F3",culture):"";
    public object? ConvertBack(object value,Type targetType,object parameter,CultureInfo culture)
    {
        string text=(value as string??"").Trim();
        if(text.Length==0)return null;
        if((double.TryParse(text,NumberStyles.Float,culture,out double number)||double.TryParse(text,NumberStyles.Float,CultureInfo.InvariantCulture,out number))&&double.IsFinite(number))return number;
        return DependencyProperty.UnsetValue;
    }
}
