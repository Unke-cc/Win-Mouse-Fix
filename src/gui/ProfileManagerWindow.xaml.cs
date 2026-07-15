using System.IO;
using System.Text.Json;
using System.Windows;
using Microsoft.VisualBasic;
using WinMouseFix.Gui.Models;
using WinMouseFix.Gui.Services;
using MessageBox = System.Windows.MessageBox;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
using SaveFileDialog = Microsoft.Win32.SaveFileDialog;

namespace WinMouseFix.Gui;

public partial class ProfileManagerWindow : Window
{
    private readonly ProfileService profileService;
    private string activeProfileName = ProfileService.DefaultProfileName;

    public ProfileManagerWindow(ProfileService profileService, AppConfig currentConfig)
    {
        this.profileService = profileService;
        CurrentConfig = currentConfig;
        InitializeComponent();
        StorageLocationText.Text = profileService.ProfilesDirectory;
    }

    public AppConfig CurrentConfig { get; private set; }

    public bool ConfigurationChanged { get; private set; }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            await RefreshListsAsync();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or JsonException)
        {
            MessageBox.Show(this, ex.Message, "配置管理", MessageBoxButton.OK, MessageBoxImage.Warning);
            Close();
        }
    }

    private async Task RefreshListsAsync()
    {
        activeProfileName = await profileService.GetActiveNameAsync();
        ProfilesComboBox.ItemsSource = await profileService.ListAsync();
        ProfilesComboBox.SelectedItem = activeProfileName;
        BackupsComboBox.ItemsSource = (await profileService.ListBackupsAsync())
            .Select(item => item.FileName)
            .ToArray();
        if (BackupsComboBox.Items.Count > 0)
        {
            BackupsComboBox.SelectedIndex = 0;
        }
    }

    private async void SwitchProfile_Click(object sender, RoutedEventArgs e) =>
        await RunAsync(async () => ApplySelection(await profileService.SwitchAsync(SelectedProfile(), CurrentConfig)));

    private async void CreateProfile_Click(object sender, RoutedEventArgs e)
    {
        var name = PromptForName("输入新配置档案名称", "新配置");
        if (name is null)
        {
            return;
        }

        await RunAsync(async () => ApplySelection(await profileService.CreateAsync(name, CurrentConfig)));
    }

    private async void RenameProfile_Click(object sender, RoutedEventArgs e)
    {
        var selected = SelectedProfile();
        var name = PromptForName("输入新的配置档案名称", selected);
        if (name is null)
        {
            return;
        }

        await RunAsync(async () =>
        {
            await profileService.RenameAsync(selected, name);
            activeProfileName = await profileService.GetActiveNameAsync();
            ConfigurationChanged = true;
        });
    }

    private async void DeleteProfile_Click(object sender, RoutedEventArgs e)
    {
        var selected = SelectedProfile();
        if (MessageBox.Show(this, $"确定删除配置档案“{selected}”吗？", "删除配置",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }

        await RunAsync(async () => ApplySelection(await profileService.DeleteAsync(selected, CurrentConfig)));
    }

    private async void ImportConfig_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "导入 Win Mouse Fix 配置",
            Filter = "JSON 配置 (*.json)|*.json",
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        var name = PromptForName("输入导入后的配置档案名称", Path.GetFileNameWithoutExtension(dialog.FileName));
        if (name is null)
        {
            return;
        }

        await RunAsync(async () =>
        {
            await profileService.CreateBackupAsync(CurrentConfig);
            ApplySelection(await profileService.ImportAsync(dialog.FileName, name, CurrentConfig));
        });
    }

    private async void ExportConfig_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Title = "导出 Win Mouse Fix 配置",
            Filter = "JSON 配置 (*.json)|*.json",
            FileName = activeProfileName + ".json",
            AddExtension = true,
            DefaultExt = ".json"
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        await RunAsync(async () =>
        {
            await profileService.SaveCurrentAsync(CurrentConfig);
            await profileService.ExportAsync(activeProfileName, dialog.FileName);
            MessageBox.Show(this, "配置已导出。", "配置管理", MessageBoxButton.OK, MessageBoxImage.Information);
        });
    }

    private async void CreateBackup_Click(object sender, RoutedEventArgs e) => await RunAsync(async () =>
    {
        var fileName = await profileService.CreateBackupAsync(CurrentConfig);
        MessageBox.Show(this, $"已创建备份：{fileName}", "配置管理",
            MessageBoxButton.OK, MessageBoxImage.Information);
    });

    private async void RestoreBackup_Click(object sender, RoutedEventArgs e)
    {
        if (BackupsComboBox.SelectedItem is not string backupFileName)
        {
            MessageBox.Show(this, "请先选择一个备份。", "配置管理",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (MessageBox.Show(this, "恢复备份会替换当前配置，是否继续？", "恢复备份",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }

        await RunAsync(async () =>
        {
            await profileService.CreateBackupAsync(CurrentConfig);
            ApplySelection(await profileService.RestoreBackupAsync(backupFileName, CurrentConfig));
        });
    }

    private async Task RunAsync(Func<Task> operation)
    {
        IsEnabled = false;
        try
        {
            await operation();
            await RefreshListsAsync();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
                                   InvalidDataException or JsonException or ArgumentException or
                                   InvalidOperationException)
        {
            MessageBox.Show(this, ex.Message, "配置管理", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            IsEnabled = true;
        }
    }

    private void ApplySelection(ProfileSelection selection)
    {
        activeProfileName = selection.Name;
        CurrentConfig = selection.Config;
        ConfigurationChanged = true;
    }

    private string SelectedProfile() => ProfilesComboBox.SelectedItem as string ?? activeProfileName;

    private static string? PromptForName(string prompt, string defaultValue)
    {
        var value = Interaction.InputBox(prompt, "配置管理", defaultValue).Trim();
        return value.Length == 0 ? null : value;
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
