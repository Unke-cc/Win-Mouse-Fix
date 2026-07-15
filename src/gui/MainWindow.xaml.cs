using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using WinMouseFix.Gui.Models;
using WinMouseFix.Gui.Services;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace WinMouseFix.Gui;

public partial class MainWindow : Window
{
    private readonly ConfigurationService configurationService = new();
    private readonly CoreProcessService coreProcessService = new();
    private readonly ProfileService profileService = new();
    private readonly MouseDeviceService mouseDeviceService = new();
    private readonly StartupService startupService = new();
    private readonly UpdateService updateService = new();
    private readonly DispatcherTimer saveTimer;
    private readonly DispatcherTimer statusTimer;
    private readonly DispatcherTimer captureClickTimer;
    private readonly DispatcherTimer captureHoldTimer;
    private readonly SemaphoreSlim saveLock = new(1, 1);
    private readonly Forms.NotifyIcon trayIcon;
    private readonly Forms.ToolStripMenuItem trayToggleItem;
    private readonly Forms.ContextMenuStrip trayMenu;
    private AppConfig config = new();
    private AppConfig lastSavedConfig = new();
    private INotifyCollectionChanged? observedRemaps;
    private bool loading = true;
    private bool exiting;
    private bool trayHintShown;
    private bool pointerInCaptureArea;
    private string? capturedButton;
    private string? pendingClickButton;
    private bool capturedScroll;
    private bool capturedDrag;
    private bool secondClickCandidate;
    private bool holdCaptured;
    private string? capturedGestureTrigger;
    private RemapConfig? provisionalHoldRemap;
    private RemapConfig? provisionalGestureRemap;
    private System.Windows.Point captureOrigin;
    private FrameworkElement? activePage;
    private int resizeVersion;
    private bool centerWindowAfterInitialResize = true;
    private UpdateSource? aboutSource;
    private bool aboutSourceResolved;

    internal bool StartHidden { get; set; }

    public MainWindow()
    {
        saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        saveTimer.Tick += SaveTimer_Tick;

        statusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
        statusTimer.Tick += (_, _) =>
        {
            statusTimer.Stop();
            StatusBar.Visibility = Visibility.Collapsed;
        };

        captureClickTimer = new DispatcherTimer();
        captureClickTimer.Tick += CaptureClickTimer_Tick;

        captureHoldTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        captureHoldTimer.Tick += CaptureHoldTimer_Tick;

        trayToggleItem = new Forms.ToolStripMenuItem("停止 Win Mouse Fix", null, (_, _) => RunTrayAction(ToggleEnabledFromTray));
        trayMenu = new Forms.ContextMenuStrip
        {
            AutoClose = true,
            ShowCheckMargin = false,
            ShowImageMargin = false
        };
        trayMenu.Items.Add(new Forms.ToolStripMenuItem("打开设置", null, (_, _) => RunTrayAction(ShowSettingsWindow)) { Font = new Drawing.Font("Segoe UI", 9F, Drawing.FontStyle.Bold) });
        trayMenu.Items.Add(trayToggleItem);
        trayMenu.Items.Add(new Forms.ToolStripSeparator());
        trayMenu.Items.Add(new Forms.ToolStripMenuItem("退出", null, (_, _) => RunTrayAction(() => _ = ExitApplicationAsync())));

        trayIcon = new Forms.NotifyIcon
        {
            Text = "Win Mouse Fix",
            Icon = LoadTrayIcon(),
            ContextMenuStrip = trayMenu,
            Visible = true
        };
        trayIcon.DoubleClick += (_, _) => Dispatcher.BeginInvoke(ShowSettingsWindow);

        InitializeComponent();
        coreProcessService.StatusChanged += (_, _) => Dispatcher.BeginInvoke(SyncRunningState);
    }

    internal static Drawing.Icon LoadTrayIcon()
    {
        try
        {
            var resource = System.Windows.Application.GetResourceStream(
                new Uri("pack://application:,,,/assets/WinMouseFix-Tray.ico"));
            if (resource is not null)
            {
                using (resource.Stream)
                using (var icon = new Drawing.Icon(resource.Stream))
                {
                    return (Drawing.Icon)icon.Clone();
                }
            }
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException)
        {
        }

        return (Drawing.Icon)Drawing.SystemIcons.Application.Clone();
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        string? loadError = null;
        string? defaultMessage = null;
        try
        {
            var firstRun = !File.Exists(configurationService.ConfigPath) &&
                           (!Directory.Exists(profileService.ProfilesDirectory) ||
                            !Directory.EnumerateFiles(profileService.ProfilesDirectory, "config.json", SearchOption.AllDirectories).Any());
            config = (await profileService.InitializeAsync()).Config;
            if (firstRun)
            {
                var detectedType = mouseDeviceService.Detect();
                config.RestoreDefaultRemaps(detectedType == MouseDeviceType.ThreeButton);
                defaultMessage = detectedType == MouseDeviceType.ThreeButton
                    ? "已识别为三键鼠标，并应用对应默认值"
                    : "已识别为五键或多键鼠标，并应用对应默认值";
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or JsonException)
        {
            config = new AppConfig();
            loadError = "无法读取已保存的配置，当前使用默认设置。";
        }
        lastSavedConfig = CloneConfig(config);
        DataContext = config;
        SubscribeToConfig(config);
        ConfigPathText.Text = configurationService.ConfigPath;
        AboutVersionText.Text = $"版本 {GetCurrentReleaseVersion()}";
        loading = false;
        await SaveConfigurationAsync(showConfirmation: false);
        if (loadError is not null)
        {
            ShowStatus(loadError);
        }
        else if (defaultMessage is not null)
        {
            ShowStatus(defaultMessage);
        }

        GeneralNavigation.IsChecked = true;
        UpdateApplicationEmptyState();
        UpdateRemapEmptyState();
        UpdateCoreStatus();

        var startupResult = startupService.SetRunAtLogin(config.Startup.RunAtLogin);
        if (!startupResult.Applied)
        {
            ShowStatus(startupResult.Message);
        }

        if (config.Enabled)
        {
            StartCore(showMessage: false);
        }

        if (StartHidden)
        {
            Hide();
            Opacity = 1;
            ShowActivated = true;
            ShowInTaskbar = true;
        }
    }

    private void SubscribeToConfig(AppConfig appConfig)
    {
        appConfig.PropertyChanged += Config_PropertyChanged;
        appConfig.Scroll.PropertyChanged += Config_PropertyChanged;
        appConfig.Startup.PropertyChanged += Config_PropertyChanged;
        appConfig.ExcludedApps.CollectionChanged += ExcludedApps_CollectionChanged;
        BindRemapsCollection();
    }

    private void UnsubscribeFromConfig(AppConfig appConfig)
    {
        appConfig.PropertyChanged -= Config_PropertyChanged;
        appConfig.Scroll.PropertyChanged -= Config_PropertyChanged;
        appConfig.Startup.PropertyChanged -= Config_PropertyChanged;
        appConfig.ExcludedApps.CollectionChanged -= ExcludedApps_CollectionChanged;
        if (observedRemaps is not null)
        {
            observedRemaps.CollectionChanged -= Remaps_CollectionChanged;
        }
        foreach (var remap in appConfig.Remaps)
        {
            remap.Action.PropertyChanged -= Config_PropertyChanged;
        }
        observedRemaps = null;
    }

    private void BindRemapsCollection()
    {
        if (observedRemaps is not null)
        {
            observedRemaps.CollectionChanged -= Remaps_CollectionChanged;
        }

        observedRemaps = config.Remaps;
        observedRemaps.CollectionChanged += Remaps_CollectionChanged;
        foreach (var remap in config.Remaps)
        {
            remap.Action.PropertyChanged -= Config_PropertyChanged;
            remap.Action.PropertyChanged += Config_PropertyChanged;
        }

        UpdateRemapEmptyState();
    }

    private void Config_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender == config && e.PropertyName == nameof(AppConfig.Remaps))
        {
            BindRemapsCollection();
        }

        if (!loading && sender == config.Startup && e.PropertyName == nameof(StartupConfig.RunAtLogin))
        {
            var result = startupService.SetRunAtLogin(config.Startup.RunAtLogin);
            if (!result.Applied)
            {
                ShowStatus(result.Message);
            }
        }

        if (!loading && sender == config.Scroll && IsModifierProperty(e.PropertyName))
        {
            RejectDuplicateModifier(e.PropertyName!);
        }

        UpdateTrayMenu();
        ScheduleSave();
    }

    private void Remaps_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (RemapConfig remap in e.OldItems)
            {
                remap.Action.PropertyChanged -= Config_PropertyChanged;
            }
        }

        if (e.NewItems is not null)
        {
            foreach (RemapConfig remap in e.NewItems)
            {
                remap.Action.PropertyChanged += Config_PropertyChanged;
            }
        }

        UpdateRemapEmptyState();
        ScheduleSave();
        if (activePage == ButtonsPage)
        {
            Dispatcher.BeginInvoke(ResizeWindowForActivePage, DispatcherPriority.Loaded);
        }
    }

    private void ExcludedApps_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        UpdateApplicationEmptyState();
        ScheduleSave();
    }

    private void ScheduleSave()
    {
        if (loading)
        {
            return;
        }

        saveTimer.Stop();
        saveTimer.Start();
    }

    private async void SaveTimer_Tick(object? sender, EventArgs e)
    {
        saveTimer.Stop();
        await SaveConfigurationAsync(showConfirmation: false);
    }

    private async Task SaveConfigurationAsync(bool showConfirmation)
    {
        await saveLock.WaitAsync();
        try
        {
            await profileService.SaveCurrentAsync(config);
            lastSavedConfig = CloneConfig(config);
            if (showConfirmation)
            {
                ShowStatus("设置已保存");
            }
        }
        catch (IOException ex)
        {
            RestoreLastSavedConfiguration();
            ShowStatus($"无法保存设置：{ex.Message}");
        }
        catch (UnauthorizedAccessException ex)
        {
            RestoreLastSavedConfiguration();
            ShowStatus($"无法保存设置：{ex.Message}");
        }
        finally
        {
            saveLock.Release();
        }
    }

    private static AppConfig CloneConfig(AppConfig source)
    {
        var clone = JsonSerializer.Deserialize<AppConfig>(JsonSerializer.Serialize(source)) ?? new AppConfig();
        clone.Normalize();
        return clone;
    }

    private void RestoreLastSavedConfiguration()
    {
        if (exiting)
        {
            return;
        }

        foreach (var ownedWindow in OwnedWindows.Cast<Window>().ToArray())
        {
            ownedWindow.Close();
        }

        ReplaceConfiguration(lastSavedConfig);
    }

    private void ReplaceConfiguration(AppConfig replacement)
    {
        loading = true;
        UnsubscribeFromConfig(config);
        config = CloneConfig(replacement);
        lastSavedConfig = CloneConfig(config);
        DataContext = config;
        SubscribeToConfig(config);
        loading = false;
        UpdateApplicationEmptyState();
        UpdateRemapEmptyState();

        startupService.SetRunAtLogin(config.Startup.RunAtLogin);
        if (config.Enabled != coreProcessService.IsRunning)
        {
            SetAppEnabled(config.Enabled, showMessage: false);
        }
        UpdateCoreStatus();
        Dispatcher.BeginInvoke(ResizeWindowForActivePage, DispatcherPriority.Loaded);
    }

    private void Navigation_Checked(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded)
        {
            return;
        }

        GeneralPage.Visibility = sender == GeneralNavigation ? Visibility.Visible : Visibility.Collapsed;
        ButtonsPage.Visibility = sender == ButtonsNavigation ? Visibility.Visible : Visibility.Collapsed;
        ScrollPage.Visibility = sender == ScrollNavigation ? Visibility.Visible : Visibility.Collapsed;
        AboutPage.Visibility = sender == AboutNavigation ? Visibility.Visible : Visibility.Collapsed;

        activePage = sender == GeneralNavigation ? GeneralPage :
            sender == ButtonsNavigation ? ButtonsPage :
            sender == ScrollNavigation ? ScrollPage : AboutPage;
        Dispatcher.BeginInvoke(ResizeWindowForActivePage, DispatcherPriority.Loaded);
        if (sender == AboutNavigation)
        {
            _ = ResolveAboutSourceAsync();
        }
    }

    private void ResizeWindowForActivePage()
    {
        if (activePage is null || WindowState != WindowState.Normal || !IsLoaded)
        {
            return;
        }

        var measuredContent = activePage is System.Windows.Controls.ScrollViewer { Content: FrameworkElement content }
            ? content
            : activePage;
        if (activePage == ButtonsPage)
        {
            MappingsPanel.MinHeight = 420;
        }

        measuredContent.Measure(new System.Windows.Size(Math.Max(1, ContentHost.ActualWidth), double.PositiveInfinity));

        var frameHeight = Math.Max(0, ActualHeight - RootLayout.ActualHeight);
        var targetHeight = 68 + measuredContent.DesiredSize.Height + frameHeight;
        var workArea = SystemParameters.WorkArea;
        var safeTop = workArea.Top + 16;
        var safeBottom = workArea.Bottom - 16;
        var centerAfterResize = centerWindowAfterInitialResize;
        centerWindowAfterInitialResize = false;
        var maxHeight = activePage == ButtonsPage || centerAfterResize
            ? Math.Max(MinHeight, safeBottom - safeTop)
            : Math.Max(MinHeight, safeBottom - Top);
        if (activePage == ButtonsPage && targetHeight > maxHeight)
        {
            MappingsPanel.MinHeight = Math.Max(160, 420 - (targetHeight - maxHeight));
            measuredContent.Measure(new System.Windows.Size(Math.Max(1, ContentHost.ActualWidth), double.PositiveInfinity));
            targetHeight = 68 + measuredContent.DesiredSize.Height + frameHeight;
        }

        targetHeight = Clamp(targetHeight, MinHeight, maxHeight);
        var targetTop = centerAfterResize
            ? Clamp(workArea.Top + (workArea.Height - targetHeight) / 2,
                safeTop,
                Math.Max(safeTop, safeBottom - targetHeight))
            : Top;
        if (!centerAfterResize && activePage == ButtonsPage)
        {
            targetTop = Clamp(Top, safeTop, Math.Max(safeTop, safeBottom - targetHeight));
        }

        var version = ++resizeVersion;
        if (Math.Abs(targetHeight - ActualHeight) < 2 && Math.Abs(targetTop - Top) < 2)
        {
            BeginAnimation(HeightProperty, null);
            BeginAnimation(TopProperty, null);
            Height = targetHeight;
            Top = targetTop;
            return;
        }

        var duration = TimeSpan.FromMilliseconds(180);
        var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
        var heightAnimation = new DoubleAnimation(ActualHeight, targetHeight, duration)
        {
            EasingFunction = easing
        };
        var topAnimation = new DoubleAnimation(Top, targetTop, duration)
        {
            EasingFunction = easing
        };
        heightAnimation.Completed += (_, _) =>
        {
            if (version != resizeVersion)
            {
                return;
            }

            BeginAnimation(HeightProperty, null);
            BeginAnimation(TopProperty, null);
            Height = targetHeight;
            Top = targetTop;
        };
        BeginAnimation(TopProperty, topAnimation, HandoffBehavior.SnapshotAndReplace);
        BeginAnimation(HeightProperty, heightAnimation, HandoffBehavior.SnapshotAndReplace);
    }

    private static double Clamp(double value, double minimum, double maximum) =>
        Math.Max(minimum, Math.Min(maximum, value));

    private void BrowseApplication_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "选择要暂停鼠标设置的应用",
            Filter = "Windows 应用 (*.exe)|*.exe",
            CheckFileExists = true,
            Multiselect = false
        };

        if (dialog.ShowDialog(this) == true)
        {
            ApplicationPathTextBox.Text = dialog.FileName;
        }
    }

    private void AddApplication_Click(object sender, RoutedEventArgs e)
    {
        var path = ApplicationPathTextBox.Text.Trim().Trim('"');
        if (!File.Exists(path) || !string.Equals(Path.GetExtension(path), ".exe", StringComparison.OrdinalIgnoreCase))
        {
            ShowStatus("请选择有效的 .exe 文件");
            return;
        }

        if (config.ExcludedApps.Any(item => string.Equals(item.Path, path, StringComparison.OrdinalIgnoreCase)))
        {
            ShowStatus("这个应用已经在列表中");
            return;
        }

        config.ExcludedApps.Add(new ExcludedApp
        {
            Name = Path.GetFileNameWithoutExtension(path),
            Path = path,
            Mode = "disableAll"
        });
        ApplicationPathTextBox.Clear();
        ShowStatus("应用已添加");
    }

    private void RemoveApplication_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button { CommandParameter: ExcludedApp app })
        {
            config.ExcludedApps.Remove(app);
            ShowStatus("应用已移除");
        }
    }

    private void CaptureArea_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        pointerInCaptureArea = true;
        coreProcessService.Pause();
        if (capturedButton is null)
        {
            CaptureStateText.Text = "正在录入鼠标操作";
        }
    }

    private void CaptureArea_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        pointerInCaptureArea = false;
        CaptureStateText.Text = capturedButton is null ? "鼠标移入这里即可录入" : "松开按钮以完成录入";
        if (capturedButton is null)
        {
            coreProcessService.Resume();
        }
    }

    private void CaptureArea_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton is MouseButton.Left or MouseButton.Right)
        {
            CaptureStateText.Text = "左键和右键属于主按键，不可录入";
            return;
        }

        var button = GetButtonName(e.ChangedButton);
        if (button is null)
        {
            return;
        }

        if (pendingClickButton is not null && !string.Equals(pendingClickButton, button, StringComparison.Ordinal))
        {
            FlushPendingClick();
        }

        secondClickCandidate = string.Equals(pendingClickButton, button, StringComparison.Ordinal) && captureClickTimer.IsEnabled;
        if (secondClickCandidate)
        {
            captureClickTimer.Stop();
        }

        capturedButton = button;
        capturedScroll = false;
        capturedDrag = false;
        holdCaptured = false;
        capturedGestureTrigger = null;
        provisionalHoldRemap = null;
        provisionalGestureRemap = null;
        captureOrigin = e.GetPosition(CaptureArea);
        captureHoldTimer.Stop();
        captureHoldTimer.Start();
        CaptureArea.CaptureMouse();
        CaptureStateText.Text = $"正在记录 {GetButtonDisplayName(button)}";
        e.Handled = true;
    }

    private void CaptureArea_PreviewMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (capturedButton is null)
        {
            return;
        }

        var current = e.GetPosition(CaptureArea);
        if (!holdCaptured && !capturedScroll && !capturedDrag &&
            (Math.Abs(current.X - captureOrigin.X) >= 12 || Math.Abs(current.Y - captureOrigin.Y) >= 12))
        {
            capturedDrag = true;
            UpgradeCapturedGesture("holdDrag");
            CaptureStateText.Text = "已识别点击并拖动";
        }
    }

    private void CaptureArea_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (capturedButton is null)
        {
            return;
        }

        if (!holdCaptured && !capturedDrag && !capturedScroll)
        {
            capturedScroll = true;
            UpgradeCapturedGesture("holdScroll");
            CaptureStateText.Text = "已识别按住并滚动";
        }
        e.Handled = true;
    }

    private void CaptureArea_PreviewMouseUp(object sender, MouseButtonEventArgs e)
    {
        var button = GetButtonName(e.ChangedButton);
        if (capturedButton is null || !string.Equals(capturedButton, button, StringComparison.Ordinal))
        {
            return;
        }

        var completedButton = capturedButton;
        var releasePoint = e.GetPosition(CaptureArea);
        pointerInCaptureArea = releasePoint.X >= 0 && releasePoint.X <= CaptureArea.ActualWidth &&
                               releasePoint.Y >= 0 && releasePoint.Y <= CaptureArea.ActualHeight;
        capturedButton = null;
        captureHoldTimer.Stop();
        CaptureArea.ReleaseMouseCapture();

        if (capturedGestureTrigger is not null || holdCaptured)
        {
            secondClickCandidate = false;
        }
        else if (secondClickCandidate)
        {
            pendingClickButton = null;
            secondClickCandidate = false;
            AddCapturedTrigger(completedButton, "doubleClick", out _);
        }
        else
        {
            QueueClick(completedButton);
        }

        ResetCapturedGestureState();
        CaptureStateText.Text = pointerInCaptureArea ? "正在录入鼠标操作" : "鼠标移入这里即可录入";
        if (!pointerInCaptureArea)
        {
            coreProcessService.Resume();
        }

        e.Handled = true;
    }

    private void CaptureArea_LostMouseCapture(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (capturedButton is null)
        {
            return;
        }

        capturedButton = null;
        captureHoldTimer.Stop();
        ResetCapturedGestureState();
        CaptureStateText.Text = pointerInCaptureArea ? "正在录入鼠标操作" : "鼠标移入这里即可录入";
        if (!pointerInCaptureArea)
        {
            coreProcessService.Resume();
        }
    }

    private void CaptureHoldTimer_Tick(object? sender, EventArgs e)
    {
        captureHoldTimer.Stop();
        if (capturedButton is null || capturedDrag || capturedScroll)
        {
            return;
        }

        FlushCandidateClick();
        var remap = AddCapturedTrigger(capturedButton, "hold", out var created);
        provisionalHoldRemap = created ? remap : null;
        holdCaptured = true;
        CaptureStateText.Text = "已识别长按";
    }

    private void UpgradeCapturedGesture(string trigger)
    {
        if (capturedButton is null || string.Equals(capturedGestureTrigger, trigger, StringComparison.Ordinal))
        {
            return;
        }

        captureHoldTimer.Stop();
        FlushCandidateClick();
        RemoveProvisionalRemap(ref provisionalHoldRemap);
        RemoveProvisionalRemap(ref provisionalGestureRemap);

        var remap = AddCapturedTrigger(capturedButton, trigger, out var created);
        provisionalGestureRemap = created ? remap : null;
        capturedGestureTrigger = trigger;
        holdCaptured = false;
    }

    private void RemoveProvisionalRemap(ref RemapConfig? remap)
    {
        if (remap is not null)
        {
            config.Remaps.Remove(remap);
            remap = null;
        }
    }

    private void ResetCapturedGestureState()
    {
        capturedScroll = false;
        capturedDrag = false;
        secondClickCandidate = false;
        holdCaptured = false;
        capturedGestureTrigger = null;
        provisionalHoldRemap = null;
        provisionalGestureRemap = null;
    }

    private void QueueClick(string button)
    {
        pendingClickButton = button;
        captureClickTimer.Interval = config.DoubleClickSpeed switch
        {
            "slow" => TimeSpan.FromMilliseconds(400),
            "medium" => TimeSpan.FromMilliseconds(250),
            _ => TimeSpan.FromMilliseconds(150)
        };
        captureClickTimer.Stop();
        captureClickTimer.Start();
    }

    private void CaptureClickTimer_Tick(object? sender, EventArgs e)
    {
        captureClickTimer.Stop();
        FlushPendingClick();
    }

    private void FlushCandidateClick()
    {
        if (secondClickCandidate)
        {
            secondClickCandidate = false;
            FlushPendingClick();
        }
    }

    private void FlushPendingClick()
    {
        captureClickTimer.Stop();
        var button = pendingClickButton;
        pendingClickButton = null;
        if (button is not null)
        {
            AddCapturedTrigger(button, "click", out _);
        }
    }

    private RemapConfig AddCapturedTrigger(string button, string trigger, out bool created)
    {
        var existing = config.Remaps.FirstOrDefault(item =>
            string.Equals(item.Button, button, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(item.Trigger, trigger, StringComparison.OrdinalIgnoreCase));

        if (existing is not null)
        {
            created = false;
            SelectRemap(existing);
            ShowStatus("这个操作已经设置，已定位到对应行");
            return existing;
        }

        var remap = RemapConfig.Create(button, trigger);
        config.Remaps.Add(remap);
        created = true;
        SelectRemap(remap);
        ShowStatus($"已录入：{GetButtonDisplayName(button)} · {GetTriggerDisplayName(trigger)}");
        return remap;
    }

    private void SelectRemap(RemapConfig remap)
    {
        RemapsList.SelectedItem = remap;
        RemapsList.UpdateLayout();
        RemapsList.ScrollIntoView(remap);
    }

    private void RemoveRemap_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button { CommandParameter: RemapConfig remap })
        {
            config.Remaps.Remove(remap);
            ShowStatus("映射已删除");
        }
    }

    private void RemapComboBox_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is not System.Windows.Controls.ComboBox { IsDropDownOpen: false })
        {
            return;
        }

        e.Handled = true;
        var scrollViewer = FindVisualChild<System.Windows.Controls.ScrollViewer>(RemapsList);
        if (scrollViewer is not null)
        {
            if (e.Delta > 0)
            {
                scrollViewer.LineUp();
            }
            else
            {
                scrollViewer.LineDown();
            }
        }
    }

    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T match)
            {
                return match;
            }

            var nested = FindVisualChild<T>(child);
            if (nested is not null)
            {
                return nested;
            }
        }

        return null;
    }

    private void RestoreDefaults_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new DefaultPresetWindow(mouseDeviceService.Detect()) { Owner = this };
        if (dialog.ShowDialog() != true || !dialog.SelectedPreset.HasValue)
        {
            return;
        }

        config.RestoreDefaultRemaps(dialog.SelectedPreset == MouseDeviceType.ThreeButton);
        BindRemapsCollection();
        ShowStatus(dialog.SelectedPreset == MouseDeviceType.ThreeButton
            ? "已恢复三键鼠标默认值"
            : "已恢复五键或多键鼠标默认值");
        Dispatcher.BeginInvoke(ResizeWindowForActivePage, DispatcherPriority.Loaded);
    }

    private void ModifierComboBox_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is System.Windows.Controls.ComboBox { IsDropDownOpen: false })
        {
            e.Handled = true;
        }
    }

    private static bool IsModifierProperty(string? propertyName) => propertyName is
        nameof(ScrollConfig.HorizontalModifier) or nameof(ScrollConfig.FastModifier) or
        nameof(ScrollConfig.PrecisionModifier) or nameof(ScrollConfig.ZoomModifier);

    private void RejectDuplicateModifier(string propertyName)
    {
        var selected = propertyName switch
        {
            nameof(ScrollConfig.HorizontalModifier) => config.Scroll.HorizontalModifier,
            nameof(ScrollConfig.FastModifier) => config.Scroll.FastModifier,
            nameof(ScrollConfig.PrecisionModifier) => config.Scroll.PrecisionModifier,
            nameof(ScrollConfig.ZoomModifier) => config.Scroll.ZoomModifier,
            _ => "none"
        };
        if (selected == "none")
        {
            return;
        }

        var assignedCount = new[]
        {
            config.Scroll.HorizontalModifier,
            config.Scroll.FastModifier,
            config.Scroll.PrecisionModifier,
            config.Scroll.ZoomModifier
        }.Count(value => value == selected);
        if (assignedCount < 2)
        {
            return;
        }

        loading = true;
        switch (propertyName)
        {
            case nameof(ScrollConfig.HorizontalModifier): config.Scroll.HorizontalModifier = "none"; break;
            case nameof(ScrollConfig.FastModifier): config.Scroll.FastModifier = "none"; break;
            case nameof(ScrollConfig.PrecisionModifier): config.Scroll.PrecisionModifier = "none"; break;
            case nameof(ScrollConfig.ZoomModifier): config.Scroll.ZoomModifier = "none"; break;
        }
        loading = false;
        ShowStatus("该修饰键已用于其他滚轮动作，本项已设为关闭");
    }

    private void OpenButtonOptions_Click(object sender, RoutedEventArgs e)
    {
        new ButtonOptionsWindow(config) { Owner = this }.ShowDialog();
    }

    private async void OpenProfileManager_Click(object sender, RoutedEventArgs e)
    {
        saveTimer.Stop();
        await SaveConfigurationAsync(showConfirmation: false);
        var window = new ProfileManagerWindow(profileService, config) { Owner = this };
        window.ShowDialog();
        if (!window.ConfigurationChanged)
        {
            return;
        }

        ReplaceConfiguration(window.CurrentConfig);
        await SaveConfigurationAsync(showConfirmation: false);
        ShowStatus("配置已切换并应用");
    }

    private static string? GetButtonName(MouseButton button) => button switch
    {
        MouseButton.Middle => "MButton",
        MouseButton.XButton1 => "XButton1",
        MouseButton.XButton2 => "XButton2",
        _ => null
    };

    private static string GetButtonDisplayName(string button) => button switch
    {
        "MButton" => "中键",
        "XButton1" => "按键4（后退键）",
        "XButton2" => "按键5（前进键）",
        _ => "鼠标按钮"
    };

    private static string GetTriggerDisplayName(string trigger) => trigger switch
    {
        "click" => "点击",
        "doubleClick" => "双击",
        "hold" => "长按",
        "holdScroll" => "按住并滚动",
        "wheelUp" => "向上滚动（旧设置）",
        "wheelDown" => "向下滚动（旧设置）",
        "holdDrag" => "按住并拖动",
        _ => trigger
    };

    private void AppEnabledToggle_Click(object sender, RoutedEventArgs e) =>
        SetAppEnabled(AppEnabledToggle.IsChecked == true, showMessage: true);

    private void SetAppEnabled(bool enabled, bool showMessage)
    {
        config.Enabled = enabled;
        if (enabled)
        {
            StartCore(showMessage);
        }
        else
        {
            StopCore(showMessage);
        }
    }

    private void StartCore(bool showMessage)
    {
        var result = coreProcessService.Start();
        if (result.Started && pointerInCaptureArea)
        {
            coreProcessService.Pause();
        }
        config.Enabled = result.Started;
        UpdateCoreStatus();
        if (showMessage || !result.Started)
        {
            ShowStatus(result.Message);
        }
    }

    private void StopCore(bool showMessage)
    {
        var result = coreProcessService.Stop();
        config.Enabled = false;
        UpdateCoreStatus();
        if (showMessage)
        {
            ShowStatus(result.Message);
        }
    }

    private void UpdateCoreStatus()
    {
        var running = coreProcessService.IsRunning;
        AppStatusDot.Fill = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(running ? "#168554" : "#98A2B3"));
        AppStatusText.Text = running ? "已启动" : "已停止";
        UpdateTrayMenu();
    }

    private void SyncRunningState()
    {
        if (!loading && !exiting)
        {
            config.Enabled = coreProcessService.IsRunning;
        }
        UpdateCoreStatus();
    }

    private void UpdateApplicationEmptyState()
    {
        EmptyApplicationsText.Visibility = config.ExcludedApps.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateRemapEmptyState()
    {
        if (EmptyRemapsText is not null)
        {
            EmptyRemapsText.Visibility = config.Remaps.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private void ScrollSpeed_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (ScrollSpeedText is not null)
        {
            ScrollSpeedText.Text = $"{e.NewValue:0.##}×";
        }
    }

    private void ShowStatus(string message)
    {
        StatusMessageText.Text = message;
        StatusBar.Visibility = Visibility.Visible;
        statusTimer.Stop();
        statusTimer.Start();
    }

    private void CloseStatus_Click(object sender, RoutedEventArgs e)
    {
        statusTimer.Stop();
        StatusBar.Visibility = Visibility.Collapsed;
    }

    private async Task ResolveAboutSourceAsync()
    {
        if (aboutSourceResolved)
        {
            return;
        }

        aboutSourceResolved = true;
        ProjectHomepageText.Text = "正在检测 GitHub / Gitee";
        AboutSourceStatusText.Text = "正在根据当前网络选择项目来源…";
        OpenProjectHomepageButton.IsEnabled = false;
        OpenLicenseButton.IsEnabled = false;

        try
        {
            var result = await updateService.ResolvePreferredSourceAsync();
            if (result.Source.HasValue)
            {
                ApplyAboutSource(result.Source.Value);
                return;
            }

            ApplyUnavailableAboutSource(result.ErrorMessage ?? "GitHub 和 Gitee 暂不可用，请检查网络后重试。");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            ApplyUnavailableAboutSource("GitHub 和 Gitee 暂不可用，请检查网络后重试。");
        }
    }

    private void ApplyAboutSource(UpdateSource source)
    {
        aboutSource = source;
        var sourceName = source == UpdateSource.Gitee ? "Gitee" : "GitHub";
        ProjectHomepageText.Text = UpdateService.GetProjectHomepageUrl(source).Replace("https://", string.Empty);
        AboutSourceStatusText.Text = $"当前使用 {sourceName}；项目主页、许可和更新页面将保持一致。";
        OpenProjectHomepageButton.IsEnabled = true;
        OpenLicenseButton.IsEnabled = true;
    }

    private void ApplyUnavailableAboutSource(string message)
    {
        aboutSource = null;
        ProjectHomepageText.Text = "GitHub / Gitee 均不可用";
        AboutSourceStatusText.Text = message;
        OpenProjectHomepageButton.IsEnabled = false;
        OpenLicenseButton.IsEnabled = false;
    }

    private async void CheckForUpdates_Click(object sender, RoutedEventArgs e)
    {
        CheckForUpdatesButton.IsEnabled = false;
        CheckForUpdatesButton.Content = "正在检查…";

        try
        {
            var currentVersion = GetCurrentReleaseVersion();
            var result = await updateService.CheckAsync(currentVersion, config.ReceiveBetaUpdates);
            if (result.Status == UpdateCheckStatus.Error)
            {
                ApplyUnavailableAboutSource(result.ErrorMessage ?? "GitHub 和 Gitee 暂不可用，请检查网络后重试。");
                ShowStatus(result.ErrorMessage ?? "暂时无法检查更新");
                return;
            }

            if (result.Source.HasValue)
            {
                aboutSourceResolved = true;
                ApplyAboutSource(result.Source.Value);
            }

            if (result.Status == UpdateCheckStatus.NoRelease)
            {
                ShowStatus(config.ReceiveBetaUpdates
                    ? "暂未发布可检查的正式版或 Beta 版"
                    : "暂未发布可检查的正式版本");
                return;
            }

            var sourceName = result.Source == UpdateSource.Gitee ? "Gitee" : "GitHub";
            if (result.Status == UpdateCheckStatus.UpToDate)
            {
                ShowStatus($"当前已是最新版本 {currentVersion}（{sourceName}）");
                return;
            }

            var pageUrl = result.PageUrl;
            if (result.ReleaseVersion is null || string.IsNullOrWhiteSpace(pageUrl))
            {
                ShowStatus("新版本信息不完整，请稍后重试");
                return;
            }

            if (System.Windows.MessageBox.Show(this,
                    $"发现新版本 {result.DisplayName ?? result.ReleaseVersion.ToString()}（{sourceName}），是否打开下载页面？",
                    "Win Mouse Fix 更新",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Information) == MessageBoxResult.Yes)
            {
                OpenWebPage(pageUrl!, "无法打开最新版本页面");
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            ShowStatus("无法检查更新，请稍后重试");
        }
        finally
        {
            CheckForUpdatesButton.Content = "检查更新";
            CheckForUpdatesButton.IsEnabled = true;
        }
    }

    internal static bool TryParseReleaseVersion(string? tag, out Version version)
        => UpdateService.TryParseVersion(tag, out version);

    private static ReleaseVersion GetCurrentReleaseVersion()
    {
        var assembly = typeof(MainWindow).Assembly;
        var informationVersion = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (ReleaseVersion.TryParse(informationVersion, out var releaseVersion))
        {
            return releaseVersion;
        }

        return ReleaseVersion.FromVersion(assembly.GetName().Version ?? new Version(0, 1, 2));
    }

    private void OpenProjectHomepage_Click(object sender, RoutedEventArgs e)
    {
        if (aboutSource.HasValue)
        {
            OpenWebPage(UpdateService.GetProjectHomepageUrl(aboutSource.Value), "无法打开项目主页");
        }
    }

    private void OpenLicense_Click(object sender, RoutedEventArgs e)
    {
        if (aboutSource.HasValue)
        {
            OpenWebPage(UpdateService.GetLicenseUrl(aboutSource.Value), "无法打开许可页面");
        }
    }

    private void OpenWebPage(string address, string errorMessage)
    {
        try
        {
            Process.Start(new ProcessStartInfo(address) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            ShowStatus($"{errorMessage}，请在浏览器中访问：{address}");
        }
    }

    private void ToggleEnabledFromTray()
    {
        SetAppEnabled(!coreProcessService.IsRunning, showMessage: false);
    }

    private void RunTrayAction(Action action)
    {
        trayMenu.Close(Forms.ToolStripDropDownCloseReason.ItemClicked);
        Dispatcher.BeginInvoke(action, DispatcherPriority.Normal);
    }

    private void UpdateTrayMenu()
    {
        if (trayIcon is null)
        {
            return;
        }

        var running = coreProcessService.IsRunning;
        trayToggleItem.Text = running ? "停止 Win Mouse Fix" : "启动 Win Mouse Fix";
        trayIcon.Text = running ? "Win Mouse Fix - 已启动" : "Win Mouse Fix - 已停止";
    }

    internal void ShowSettingsWindow()
    {
        StartHidden = false;
        Opacity = 1;
        ShowActivated = true;
        ShowInTaskbar = true;
        Show();
        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        Activate();
        Topmost = true;
        Topmost = false;
        Focus();
    }

    internal void RequestApplicationShutdown() => _ = ExitApplicationAsync();

    private async Task ExitApplicationAsync()
    {
        if (exiting)
        {
            return;
        }

        exiting = true;
        saveTimer.Stop();
        captureClickTimer.Stop();
        captureHoldTimer.Stop();
        coreProcessService.Resume();
        await SaveConfigurationAsync(showConfirmation: false);
        coreProcessService.Stop();
        trayIcon.Visible = false;
        trayIcon.Icon?.Dispose();
        trayIcon.Dispose();
        System.Windows.Application.Current.Shutdown();
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (exiting)
        {
            return;
        }

        e.Cancel = true;
        captureHoldTimer.Stop();
        capturedButton = null;
        ResetCapturedGestureState();
        coreProcessService.Resume();
        Hide();
        ScheduleSave();

        if (!trayHintShown)
        {
            trayHintShown = true;
            trayIcon.ShowBalloonTip(2500, "Win Mouse Fix 仍在运行", "可以从系统托盘重新打开设置或退出。", Forms.ToolTipIcon.Info);
        }
    }
}
