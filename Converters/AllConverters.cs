using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace SmartFactoryCPS.Converters;

/// <summary>bool IsRunning → 헤더 상태 점 색상</summary>
[ValueConversion(typeof(bool), typeof(Color))]
public class RunningToStatusDotColorConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c)
        => (bool)value ? Color.FromRgb(0x00, 0xA6, 0x3E) : Color.FromRgb(0x6A, 0x72, 0x82);
    public object ConvertBack(object v, Type t, object p, CultureInfo c) => throw new NotImplementedException();
}

/// <summary>상태 문자열 → 배지 배경 Brush</summary>
[ValueConversion(typeof(string), typeof(Brush))]
public class StatusToBadgeBackgroundConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c)
        => value?.ToString() switch
        {
            "운전" or "판독 완료" or "검증 완료"
                => new SolidColorBrush(Color.FromRgb(0xDC, 0xFC, 0xE7)),
            "판독 중" or "검증 중" or "분류 중"
                => new SolidColorBrush(Color.FromRgb(0xDB, 0xEA, 0xFE)),
            _ => new SolidColorBrush(Color.FromRgb(0xEC, 0xEE, 0xF2))
        };
    public object ConvertBack(object v, Type t, object p, CultureInfo c) => throw new NotImplementedException();
}

/// <summary>상태 문자열 → 배지 전경 Brush</summary>
[ValueConversion(typeof(string), typeof(Brush))]
public class StatusToBadgeForegroundConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c)
        => value?.ToString() switch
        {
            "운전" or "판독 완료" or "검증 완료"
                => new SolidColorBrush(Color.FromRgb(0x00, 0xA6, 0x3E)),
            "판독 중" or "검증 중" or "분류 중"
                => new SolidColorBrush(Color.FromRgb(0x15, 0x5D, 0xFC)),
            _ => new SolidColorBrush(Color.FromRgb(0x45, 0x55, 0x6C))
        };
    public object ConvertBack(object v, Type t, object p, CultureInfo c) => throw new NotImplementedException();
}

/// <summary>bool → Visibility (false = Visible)</summary>
[ValueConversion(typeof(bool), typeof(Visibility))]
public class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c)
        => (bool)value ? Visibility.Collapsed : Visibility.Visible;
    public object ConvertBack(object v, Type t, object p, CultureInfo c) => throw new NotImplementedException();
}

/// <summary>알람 활성 여부 → 배경 색 (true=빨강, false=흰색)</summary>
[ValueConversion(typeof(bool), typeof(Brush))]
public class AlarmToPanelBackgroundConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c)
        => (bool)value
            ? new SolidColorBrush(Color.FromRgb(0xFF, 0xF1, 0xF2))
            : new SolidColorBrush(Color.FromRgb(0xF8, 0xFA, 0xFC));
    public object ConvertBack(object v, Type t, object p, CultureInfo c) => throw new NotImplementedException();
}

/// <summary>이벤트 Status → 전경색</summary>
[ValueConversion(typeof(string), typeof(Brush))]
public class EventStatusToColorConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c)
        => value?.ToString() switch
        {
            "ERROR" => new SolidColorBrush(Color.FromRgb(0xE7, 0x00, 0x0B)),
            _ => new SolidColorBrush(Color.FromRgb(0x00, 0xA6, 0x3E))
        };
    public object ConvertBack(object v, Type t, object p, CultureInfo c) => throw new NotImplementedException();
}
