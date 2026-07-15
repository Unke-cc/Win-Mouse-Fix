using System.Runtime.InteropServices;

namespace WinMouseFix.Gui.Services;

public enum MouseDeviceType
{
    ThreeButton,
    FiveOrMore
}

public sealed class MouseDeviceService
{
    private const int MouseButtonCountMetric = 43;

    public MouseDeviceType Detect()
    {
        var buttonCount = GetSystemMetrics(MouseButtonCountMetric);
        return buttonCount is > 0 and <= 3
            ? MouseDeviceType.ThreeButton
            : MouseDeviceType.FiveOrMore;
    }

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);
}
