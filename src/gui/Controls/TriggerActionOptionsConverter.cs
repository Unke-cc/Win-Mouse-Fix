using System.Globalization;
using System.Windows.Data;
using WinMouseFix.Gui.Models;

namespace WinMouseFix.Gui.Controls;

public sealed class TriggerActionOptionsConverter : IValueConverter
{
    private static readonly ActionOption[] Common =
    [
        new("Original", "执行原功能", "\uE72C"),
        new("Back", "后退", "\uE72B"),
        new("Forward", "前进", "\uE72A"),
        new("MiddleClick", "中键", "\uE700"),
        new("PrimaryClick", "主键", "\uE700"),
        new("SecondaryClick", "次键", "\uE700"),
        new("TaskView", "任务视图", "\uE7C4"),
        new("ShowDesktop", "显示桌面", "\uE770"),
        new("StartMenu", "开始菜单", "\uE782"),
        new("DesktopLeft", "左侧虚拟桌面", "\uE892"),
        new("DesktopRight", "右侧虚拟桌面", "\uE893"),
        new("AltTab", "切换窗口", "\uE8B9"),
        new("CloseWindow", "关闭窗口", "\uE8BB"),
        new("CloseTab", "关闭标签页", "\uE8BB"),
        new("NextTab", "下一个标签页", "\uE76C"),
        new("PreviousTab", "上一个标签页", "\uE76B"),
        new("VolumeUp", "提高音量", "\uE767"),
        new("VolumeDown", "降低音量", "\uE74F"),
        new("VolumeMute", "静音", "\uE74F"),
        new("MediaPlayPause", "播放或暂停", "\uE768"),
        new("CustomShortcut", "自定义快捷键", "\uE765"),
        new("None", "不执行动作", "\uE711")
    ];

    private static readonly ActionOption[] HoldScroll =
    [
        new("Zoom", "缩放", "\uE71E"),
        new("VolumeControl", "音量调节", "\uE767"),
        new("TabNavigation", "标签页切换", "\uE8A7"),
        new("BrowserNavigation", "页面前进后退", "\uE72B"),
        new("DesktopSwitch", "虚拟桌面切换", "\uE7C4"),
        new("DesktopStartMenu", "桌面与开始菜单", "\uE782"),
        new("FastScroll", "快速滚动", "\uE74A"),
        new("PrecisionScroll", "精确滚动", "\uE749")
    ];

    private static readonly ActionOption[] HoldDrag =
    [
        new("ScrollMove", "滚动与移动", "\uE8B0"),
        new("DesktopNavigation", "虚拟桌面与任务视图", "\uE7C4"),
        new("BrowserTabNavigation", "网页与标签导航", "\uE8A7")
    ];

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value switch
    {
        "holdScroll" => HoldScroll,
        "holdDrag" => HoldDrag,
        _ => Common
    };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
