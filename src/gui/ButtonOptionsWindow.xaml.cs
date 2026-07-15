using System.Windows;
using System.Windows.Controls;
using WinMouseFix.Gui.Models;

namespace WinMouseFix.Gui;

public partial class ButtonOptionsWindow : Window
{
    public ButtonOptionsWindow(AppConfig config)
    {
        DoubleClickSpeedOptions =
        [
            new("fast", "快 · 150 ms"),
            new("medium", "中 · 250 ms"),
            new("slow", "慢 · 400 ms")
        ];

        InitializeComponent();
        DataContext = config;
    }

    public IReadOnlyList<DoubleClickSpeedOption> DoubleClickSpeedOptions { get; }

    private void ComboBox_PreviewMouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
    {
        if (sender is System.Windows.Controls.ComboBox { IsDropDownOpen: false })
        {
            e.Handled = true;
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}

public sealed class DoubleClickSpeedOption
{
    public DoubleClickSpeedOption(string value, string name)
    {
        Value = value;
        Name = name;
    }

    public string Value { get; }
    public string Name { get; }
}
