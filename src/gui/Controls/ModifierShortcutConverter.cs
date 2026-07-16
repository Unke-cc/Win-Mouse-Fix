using System.Globalization;
using System.Windows.Data;

namespace WinMouseFix.Gui.Controls;

public sealed class ModifierShortcutConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var shortcut = value as string;
        if (string.IsNullOrWhiteSpace(shortcut) || shortcut == "none")
        {
            return "关闭";
        }

        return string.Join(" + ", shortcut!.Split('+').Select(FormatToken));
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => System.Windows.Data.Binding.DoNothing;

    private static string FormatToken(string token) => token.Trim().ToLowerInvariant() switch
    {
        "ctrl" => "Ctrl",
        "alt" => "Alt",
        "shift" => "Shift",
        "win" => "Win",
        "mbutton" => "中键",
        "xbutton1" => "按键4（后退键）",
        "xbutton2" => "按键5（前进键）",
        _ => token
    };
}
