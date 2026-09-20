using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace mdaiAgent
{
    public class BoolToBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool boolValue && parameter is string paramStr)
            {
                var parts = paramStr.Split(';');
                var colorHex = boolValue ? parts[0] : (parts.Length > 1 ? parts[1] : parts[0]);
                try
                {
                    if (ColorConverter.ConvertFromString(colorHex) is Color color)
                    {
                        return new SolidColorBrush(color);
                    }
                }
                catch
                {
                    // Hata olursa varsayılan renk
                }
            }
            return DependencyProperty.UnsetValue;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class NullToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return string.IsNullOrEmpty(value as string) ? Visibility.Collapsed : Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
