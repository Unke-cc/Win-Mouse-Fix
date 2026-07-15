using System.Windows;
using WinMouseFix.Gui.Services;

namespace WinMouseFix.Gui;

public partial class DefaultPresetWindow : Window
{
    public DefaultPresetWindow(MouseDeviceType detectedType)
    {
        InitializeComponent();

        var recommendedText = detectedType == MouseDeviceType.ThreeButton
            ? ThreeButtonRecommendedText
            : FiveOrMoreRecommendedText;
        var recommendedButton = detectedType == MouseDeviceType.ThreeButton
            ? ThreeButtonPresetButton
            : FiveOrMorePresetButton;

        recommendedText.Visibility = Visibility.Visible;
        recommendedButton.IsDefault = true;
    }

    public MouseDeviceType? SelectedPreset { get; private set; }

    private void ThreeButtonPreset_Click(object sender, RoutedEventArgs e) =>
        Select(MouseDeviceType.ThreeButton);

    private void FiveOrMorePreset_Click(object sender, RoutedEventArgs e) =>
        Select(MouseDeviceType.FiveOrMore);

    private void Select(MouseDeviceType preset)
    {
        SelectedPreset = preset;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
