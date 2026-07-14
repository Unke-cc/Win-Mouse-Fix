using System.Windows;
using System.Windows.Controls;
using WinMouseFix.Gui.Models;

namespace WinMouseFix.Gui;

public partial class ButtonOptionsWindow : Window
{
    private readonly AppConfig config;

    public ButtonOptionsWindow(AppConfig config)
    {
        this.config = config;
        DoubleClickSpeedOptions =
        [
            new("fast", "快 · 150 ms"),
            new("medium", "中 · 250 ms"),
            new("slow", "慢 · 400 ms")
        ];

        InitializeComponent();
        DataContext = config;
        FollowMouseOption.IsChecked = config.DesktopSwipeDirection == "followMouse";
        OppositeMouseOption.IsChecked = config.DesktopSwipeDirection == "oppositeMouse";
    }

    public IReadOnlyList<DoubleClickSpeedOption> DoubleClickSpeedOptions { get; }

    private void SwipeDirection_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.RadioButton { Tag: string direction })
        {
            config.DesktopSwipeDirection = direction;
        }
    }

    private void ComboBox_PreviewMouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
    {
        if (sender is System.Windows.Controls.ComboBox { IsDropDownOpen: false })
        {
            e.Handled = true;
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}

public sealed record DoubleClickSpeedOption(string Value, string Name);
