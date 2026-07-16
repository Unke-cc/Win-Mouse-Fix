using System.Globalization;
using System.Windows.Data;
using WinMouseFix.Gui.Models;

namespace WinMouseFix.Gui.Controls;

public sealed class TriggerActionOptionsConverter : IValueConverter
{
    private static readonly ActionOption[] Common =
    [
        new("Original", "执行原功能"),
        new("Back", "后退"),
        new("Forward", "前进"),
        new("MiddleClick", "中键"),
        new("PrimaryClick", "主键"),
        new("SecondaryClick", "次键"),
        new("TaskView", "任务视图"),
        new("ShowDesktop", "显示桌面"),
        new("StartMenu", "开始菜单"),
        new("DesktopLeft", "左侧虚拟桌面"),
        new("DesktopRight", "右侧虚拟桌面"),
        new("AltTab", "切换窗口"),
        new("CloseWindow", "关闭窗口"),
        new("CloseTab", "关闭标签页"),
        new("NextTab", "下一个标签页"),
        new("PreviousTab", "上一个标签页"),
        new("VolumeUp", "提高音量"),
        new("VolumeDown", "降低音量"),
        new("VolumeMute", "静音"),
        new("MediaPlayPause", "播放或暂停"),
        new("CustomShortcut", "自定义快捷键"),
        new("None", "不执行动作")
    ];

    private static readonly ActionOption[] HoldScroll =
    [
        new("Zoom", "缩放"),
        new("VolumeControl", "音量调节"),
        new("TabNavigation", "标签页切换"),
        new("BrowserNavigation", "页面前进后退"),
        new("DesktopSwitch", "虚拟桌面切换"),
        new("DesktopStartMenu", "桌面与开始菜单"),
        new("FastScroll", "快速滚动"),
        new("PrecisionScroll", "精确滚动")
    ];

    private static readonly ActionOption[] HoldDrag =
    [
        new("ScrollMove", "滚动与移动"),
        new("DesktopNavigation", "虚拟桌面与任务视图"),
        new("BrowserTabNavigation", "网页与标签导航")
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
