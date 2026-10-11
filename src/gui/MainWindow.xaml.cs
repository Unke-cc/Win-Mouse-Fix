using WinMouseFix.Shared;
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
using WinMouseFix.Gui.Controls;
using WinMouseFix.Gui.Models;
using WinMouseFix.Gui.Services;

namespace WinMouseFix.Gui;

public partial class MainWindow : Window
{
    private readonly ConfigurationService configurationService = new();
    private readonly CoreProcessService coreProcessService;
    private readonly ProfileService profileService;
    private readonly MouseDeviceService mouseDeviceService = new();
    private readonly StartupService startupService = new();
    [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWCP_ROUND = 2;

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        ApplyWindows11Style();
    }

    private void ApplyWindows11Style()
    {
        if (Environment.OSVersion.Version.Build < 22000)
        {
            return;
        }

        try
        {
            var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            if (hwnd != IntPtr.Zero)
            {
                var cornerPreference = DWMWCP_ROUND;
                DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref cornerPreference, sizeof(int));
            }
        }
        catch
        {
        }
    }

    private bool isUpdatingScrollMode;
    private readonly UpdateService updateService = new();
    private readonly DispatcherTimer saveTimer;
    private readonly DispatcherTimer statusTimer;
    private readonly DispatcherTimer captureClickTimer;
    private readonly DispatcherTimer captureHoldTimer;
    private readonly DispatcherTimer capturePulseResetTimer = new() { Interval = TimeSpan.FromMilliseconds(1500) };
    private int resizeVersion;
    private readonly DispatcherTimer coreStatusTimer;
    private readonly SemaphoreSlim saveLock = new(1, 1);
    private AppConfig config = new();
    private AppConfig lastSavedConfig = new();
    private INotifyCollectionChanged? observedRemaps;
    private bool loading = true;
    private bool exiting;
    private bool enteringLightweightMode;
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
    private bool centerWindowAfterInitialResize = true;
    private UpdateSource? aboutSource;
    private bool aboutSourceResolved;
    private UpdateCheckResult? pendingUpdate;
    private DownloadedUpdate? downloadedUpdate;
    private CancellationTokenSource? updateDownloadCancellation;

    internal event EventHandler? WindowHiddenToTray;
    internal event EventHandler? ActivityDetected;

    internal bool StartHidden { get; set; }

    public MainWindow(CoreProcessService coreProcessService, ProfileService profileService)
    {
        this.coreProcessService = coreProcessService;
        this.profileService = profileService;
        saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        saveTimer.Tick += SaveTimer_Tick;

        statusTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(2000) };
        statusTimer.Tick += StatusTimer_Tick;

        captureClickTimer = new DispatcherTimer();
        captureClickTimer.Tick += CaptureClickTimer_Tick;

        captureHoldTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        captureHoldTimer.Tick += CaptureHoldTimer_Tick;
        capturePulseResetTimer.Tick += (_, _) =>
        {
            capturePulseResetTimer.Stop();
            CapturePlusIcon.Foreground = new SolidColorBrush(Color.FromRgb(0x8E, 0x95, 0xA2));
        };

        coreStatusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        coreStatusTimer.Tick += CoreStatusTimer_Tick;

        InitializeComponent();
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
        UpdateScrollModeRadios();
        ConfigPathText.Text = configurationService.ConfigPath;
        AboutVersionText.Text = $"版本 {GetCurrentReleaseVersion()}";
        loading = false;
        SyncScrollModifiersFromRemaps();
        RefreshModifierDisplays();
        await SaveConfigurationAsync(showConfirmation: false);
        if (loadError is not null)
        {
            ShowStatus(loadError);
        }
        else if (defaultMessage is not null)
        {
            ShowStatus(defaultMessage);
        }

        ButtonsNavigation.IsChecked = true;
        UpdateApplicationEmptyState();
        UpdateRemapEmptyState();
        RefreshModifierDisplays();
        UpdateCoreStatus();
        coreStatusTimer.Start();

        // 固定开机运行与全屏自动暂停为启用
        config.Startup.RunAtLogin = true;
        config.PauseInFullscreen = true;
        var startupResult = startupService.SetRunAtLogin(true);
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
            WindowHiddenToTray?.Invoke(this, EventArgs.Empty);
        MemoryService.TrimWorkingSet();
        }
    }

    private void SubscribeToConfig(AppConfig appConfig)
    {
        appConfig.PropertyChanged += Config_PropertyChanged;
        appConfig.Scroll.PropertyChanged += Config_PropertyChanged;
        appConfig.Timing.PropertyChanged += Config_PropertyChanged;
        appConfig.Startup.PropertyChanged += Config_PropertyChanged;
        appConfig.ExcludedApps.CollectionChanged += ExcludedApps_CollectionChanged;
        BindRemapsCollection();
    }

    private void UnsubscribeFromConfig(AppConfig appConfig)
    {
        appConfig.PropertyChanged -= Config_PropertyChanged;
        appConfig.Scroll.PropertyChanged -= Config_PropertyChanged;
        appConfig.Timing.PropertyChanged -= Config_PropertyChanged;
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
            WarnMouseModifierConflict(e.PropertyName!);
            RefreshModifierDisplays();
        }

        if (!loading && sender is ActionConfig action && e.PropertyName == nameof(ActionConfig.Type))
        {
            SyncScrollModifierFromAction(action);
        }

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
        RefreshModifierDisplays();
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

    private async Task<bool> SaveConfigurationAsync(bool showConfirmation)
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

            return true;
        }
        catch (IOException ex)
        {
            RestoreLastSavedConfiguration();
            ShowStatus($"无法保存设置：{ex.Message}");
            return false;
        }
        catch (UnauthorizedAccessException ex)
        {
            RestoreLastSavedConfiguration();
            ShowStatus($"无法保存设置：{ex.Message}");
            return false;
        }
        finally
        {
            saveLock.Release();
        }
    }

    private void StatusTimer_Tick(object? sender, EventArgs e)
    {
        statusTimer.Stop();
        var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(180))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
        };
        fadeOut.Completed += (_, _) =>
        {
            if (!statusTimer.IsEnabled)
            {
                StatusHud.Visibility = Visibility.Collapsed;
            }
        };
        StatusHud.BeginAnimation(OpacityProperty, fadeOut);
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
        UpdateScrollModeRadios();
        loading = false;
        UpdateApplicationEmptyState();
        UpdateRemapEmptyState();

        // 固定开机运行与全屏自动暂停为启用
        config.Startup.RunAtLogin = true;
        config.PauseInFullscreen = true;
        startupService.SetRunAtLogin(true);
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

        if (activePage != null)
        {
            var fadeIn = new DoubleAnimation(0.5, 1.0, TimeSpan.FromMilliseconds(120))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            activePage.BeginAnimation(OpacityProperty, fadeIn);
        }

        Dispatcher.BeginInvoke(ResizeWindowForActivePage, DispatcherPriority.Loaded);
        if (sender == AboutNavigation)
        {
            _ = ResolveAboutSourceAsync();
        }
    }

    /// <summary>
    /// 根据当前活动页面动态自适应窗口宽度与高度，实现通用页小巧精致、手势按键页舒展完整的响应式布局
    /// </summary>
        /// <summary>
    /// 根据当前活动页面动态自适应窗口宽度与高度，实现通用页小巧精致、手势与滚动页宽屏舒展的居中平滑伸缩
    /// </summary>
        /// <summary>
    /// 根据当前活动页面自适应窗口尺寸：按键页为 580 宽画幅；通用、滚动、关于三页固定为统一的 480 紧凑无滚动条卡片
    /// </summary>
        /// <summary>
    /// 所有页面统一以 480 为标准固定窗口宽度，左上角固定吸附，仅对高度执行平滑单向展开/收缩动画
    /// </summary>
    private void ResizeWindowForActivePage()
    {
        if (activePage is null || WindowState != WindowState.Normal || !IsLoaded)
        {
            return;
        }

        var measuredContent = activePage is System.Windows.Controls.ScrollViewer { Content: FrameworkElement content }
            ? content
            : activePage;


        // 所有页面的内容宽度严格统一为 440，窗口宽度严格统一为 480
        const double standardContentWidth = 440.0;
        var frameWidth = Math.Max(0, ActualWidth - RootLayout.ActualWidth);
        var targetWidth = standardContentWidth + 40 + frameWidth;
        var workArea = SystemParameters.WorkArea;
        targetWidth = Clamp(targetWidth, MinWidth, Math.Max(MinWidth, workArea.Width - 32));

        // 测算内容展开所需的自然高度，确保无滚动条完整展示
        measuredContent.Measure(new System.Windows.Size(standardContentWidth, double.PositiveInfinity));

        var frameHeight = Math.Max(0, ActualHeight - RootLayout.ActualHeight);
        var targetHeight = 54 + measuredContent.DesiredSize.Height + frameHeight + (activePage == ButtonsPage ? 8 : 0);
        var safeTop = workArea.Top + 16;
        var safeBottom = workArea.Bottom - 16;
        var safeLeft = workArea.Left + 16;
        var safeRight = workArea.Right - 16;
        var centerAfterResize = centerWindowAfterInitialResize;
        centerWindowAfterInitialResize = false;

        var maxHeight = activePage == ButtonsPage || centerAfterResize
            ? Math.Max(MinHeight, safeBottom - safeTop)
            : Math.Max(MinHeight, safeBottom - Top);


        targetHeight = Clamp(targetHeight, MinHeight, maxHeight);

        // 首次启动全屏居中；后续切换 Tab 时左上角固定吸附（Left 和 Top 恒定不动！）
        if (centerAfterResize)
        {
            var initialLeft = Clamp(workArea.Left + (workArea.Width - targetWidth) / 2.0, safeLeft, Math.Max(safeLeft, safeRight - targetWidth));
            var initialTop = Clamp(workArea.Top + (workArea.Height - targetHeight) / 2.0, safeTop, Math.Max(safeTop, safeBottom - targetHeight));
            Left = initialLeft;
            Top = initialTop;
            Width = targetWidth;
            Height = targetHeight;
            return;
        }

        // 宽度恒定保持 480，彻底消灭任何横向跳跃与晃动
        if (Math.Abs(Width - targetWidth) > 1)
        {
            Width = targetWidth;
        }

        // 仅对高度执行 220ms 平滑缓动展开/收缩动画（从左上角自然向下延展）
        var version = ++resizeVersion;
        if (Math.Abs(targetHeight - ActualHeight) < 2)
        {
            BeginAnimation(HeightProperty, null);
            Height = targetHeight;
            return;
        }

        var duration = TimeSpan.FromMilliseconds(220);
        var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
        var heightAnimation = new DoubleAnimation(ActualHeight, targetHeight, duration)
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
            Height = targetHeight;
        };

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

    private void PulseCapturePlusIcon()
    {
        CapturePlusIcon.Foreground = new SolidColorBrush(Color.FromRgb(0x00, 0x7A, 0xFF));
        capturePulseResetTimer.Stop();
        capturePulseResetTimer.Start();
    }

    private void CaptureArea_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        pointerInCaptureArea = true;
        coreProcessService.Pause();
        CaptureArea.BorderBrush = new SolidColorBrush(Color.FromRgb(0xD0, 0xD3, 0xD9));
        CaptureTransform.Y = -1;
        CaptureGlowEffect.Opacity = 0.22;
    }

    private void CaptureArea_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        pointerInCaptureArea = false;
        CaptureArea.BorderBrush = new SolidColorBrush(Color.FromRgb(0xE3, 0xE3, 0xE3));
        CaptureTransform.Y = 0;
        CaptureGlowEffect.Opacity = 0;
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
        PulseCapturePlusIcon();
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
            PulseCapturePlusIcon();
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
        CaptureStateText.Text = pointerInCaptureArea ? "按住中键或侧键以录入或测试手势" : "移入此区域以录入或测试手势";
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
        CaptureStateText.Text = pointerInCaptureArea ? "按住中键或侧键以录入或测试手势" : "移入此区域以录入或测试手势";
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
            var actionDisplay = GetActionDisplayName(existing.Action.Type);
            var feedback = $"✓ 演练命中：{GetButtonDisplayName(button)} · {GetTriggerDisplayName(trigger)} → {actionDisplay}";
            CaptureStateText.Text = feedback;
            ShowStatus(feedback);
            return existing;
        }

        var remap = RemapConfig.Create(button, trigger);
        config.Remaps.Add(remap);
        created = true;
        SelectRemap(remap);
        var addFeedback = $"✦ 已添加：{GetButtonDisplayName(button)} · {GetTriggerDisplayName(trigger)}（请在下方指定动作）";
        CaptureStateText.Text = addFeedback;
        ShowStatus(addFeedback);
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

    private void ModifierShortcutBox_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (sender is not System.Windows.Controls.TextBox { Tag: string propertyName } shortcutBox)
        {
            return;
        }

        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.Delete or Key.Back or Key.Escape)
        {
            SetScrollModifier(propertyName, "none");
            shortcutBox.Focus();
            return;
        }

        var token = key switch
        {
            Key.LeftCtrl or Key.RightCtrl => "ctrl",
            Key.LeftAlt or Key.RightAlt => "alt",
            Key.LeftShift or Key.RightShift => "shift",
            Key.LWin or Key.RWin => "win",
            _ => null
        };
        if (token is null)
        {
            ShowStatus("可使用 Ctrl、Alt、Shift、Win 或中键、按键4、按键5");
            shortcutBox.Focus();
            return;
        }

        var pressed = new HashSet<string>(StringComparer.Ordinal);
        if (Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl) || token == "ctrl")
        {
            pressed.Add("ctrl");
        }
        if (Keyboard.IsKeyDown(Key.LeftAlt) || Keyboard.IsKeyDown(Key.RightAlt) || token == "alt")
        {
            pressed.Add("alt");
        }
        if (Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift) || token == "shift")
        {
            pressed.Add("shift");
        }
        if (Keyboard.IsKeyDown(Key.LWin) || Keyboard.IsKeyDown(Key.RWin) || token == "win")
        {
            pressed.Add("win");
        }

        SetScrollModifier(propertyName, string.Join("+", ModifierOrder.Where(pressed.Contains)));
        shortcutBox.Focus();
    }

    private void ModifierShortcutBox_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not System.Windows.Controls.TextBox { Tag: string propertyName } shortcutBox)
        {
            return;
        }

        var mouseToken = e.ChangedButton switch
        {
            MouseButton.Middle => "mbutton",
            MouseButton.XButton1 => "xbutton1",
            MouseButton.XButton2 => "xbutton2",
            _ => null
        };
        if (mouseToken is null)
        {
            return;
        }

        e.Handled = true;
        coreProcessService.Pause();
        var pressed = new HashSet<string>(StringComparer.Ordinal);
        if (Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl))
        {
            pressed.Add("ctrl");
        }
        if (Keyboard.IsKeyDown(Key.LeftAlt) || Keyboard.IsKeyDown(Key.RightAlt))
        {
            pressed.Add("alt");
        }
        if (Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift))
        {
            pressed.Add("shift");
        }
        if (Keyboard.IsKeyDown(Key.LWin) || Keyboard.IsKeyDown(Key.RWin))
        {
            pressed.Add("win");
        }
        pressed.Add(mouseToken);

        SetScrollModifier(propertyName, string.Join("+", ModifierOrder.Where(pressed.Contains)));
        shortcutBox.Focus();
    }

    private void ModifierShortcutBox_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e) =>
        coreProcessService.Pause();

    private void ModifierShortcutBox_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e) =>
        coreProcessService.Resume();

    private static readonly string[] ModifierOrder =
        { "ctrl", "alt", "shift", "win", "mbutton", "xbutton1", "xbutton2" };

    private void SetScrollModifier(string propertyName, string value)
    {
        switch (propertyName)
        {
            case nameof(ScrollConfig.HorizontalModifier): config.Scroll.HorizontalModifier = value; break;
            case nameof(ScrollConfig.FastModifier): config.Scroll.FastModifier = value; break;
            case nameof(ScrollConfig.PrecisionModifier): config.Scroll.PrecisionModifier = value; break;
            case nameof(ScrollConfig.ZoomModifier): config.Scroll.ZoomModifier = value; break;
        }
    }

    private void SyncScrollModifiersFromRemaps()
    {
        foreach (var remap in config.Remaps)
        {
            SyncScrollModifierFromAction(remap.Action);
        }
    }

    private void SyncScrollModifierFromAction(ActionConfig action)
    {
        RefreshModifierDisplays();
    }

    private void RefreshModifierDisplays()
    {
        // 滚动修饰键卡片已从界面移除，无需再更新修饰键文本框
    }

    private string BuildModifierDisplay(string configuredModifier, string actionType)
    {
        var modifiers = new List<string>();
        AddModifier(configuredModifier);
        foreach (var button in config.Remaps
                     .Where(item => item.Action.Type == actionType)
                     .Select(item => item.Button))
        {
            AddModifier(button switch
            {
                "MButton" => "mbutton",
                "XButton1" => "xbutton1",
                "XButton2" => "xbutton2",
                _ => "none"
            });
        }

        return modifiers.Count == 0 ? "关闭" : string.Join("、", modifiers);

        void AddModifier(string value)
        {
            if (value == "none" || modifiers.Contains(value, StringComparer.OrdinalIgnoreCase))
            {
                return;
            }

            var formatted = new ModifierShortcutConverter().Convert(
                value,
                typeof(string),
                null,
                System.Globalization.CultureInfo.InvariantCulture);
            modifiers.Add((string)formatted);
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

    private void WarnMouseModifierConflict(string propertyName)
    {
        var selected = propertyName switch
        {
            nameof(ScrollConfig.HorizontalModifier) => config.Scroll.HorizontalModifier,
            nameof(ScrollConfig.FastModifier) => config.Scroll.FastModifier,
            nameof(ScrollConfig.PrecisionModifier) => config.Scroll.PrecisionModifier,
            nameof(ScrollConfig.ZoomModifier) => config.Scroll.ZoomModifier,
            _ => "none"
        };
        var button = selected switch
        {
            "mbutton" => "MButton",
            "xbutton1" => "XButton1",
            "xbutton2" => "XButton2",
            _ => null
        };
        if (button is not null && config.Remaps.Any(remap =>
                string.Equals(remap.Button, button, StringComparison.OrdinalIgnoreCase) &&
                remap.Trigger == "holdScroll" && remap.Action.Type != "None"))
        {
            ShowStatus("该鼠标按钮同时设置了按住并滚动，滚动时优先使用修饰键功能");
        }
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

    private static string GetActionDisplayName(string? actionType) => actionType switch
    {
        "TaskView" => "任务视图",
        "ShowDesktop" => "显示桌面",
        "StartMenu" => "开始菜单",
        "DesktopLeft" => "左侧虚拟桌面",
        "DesktopRight" => "右侧虚拟桌面",
        "AltTab" => "切换窗口",
        "CloseWindow" => "关闭窗口",
        "CloseTab" => "关闭标签页",
        "NextTab" => "下一个标签页",
        "PreviousTab" => "上一个标签页",
        "VolumeUp" => "提高音量",
        "VolumeDown" => "降低音量",
        "VolumeMute" => "静音",
        "MediaPlayPause" => "播放或暂停",
        "CustomShortcut" => "自定义快捷键",
        "Original" => "执行原功能",
        "Back" => "后退",
        "Forward" => "前进",
        "MiddleClick" => "中键",
        "Zoom" => "缩放",
        "VolumeControl" => "音量调节",
        "TabNavigation" => "标签页切换",
        "BrowserNavigation" => "页面前进后退",
        "DesktopSwitch" => "虚拟桌面切换",
        "DesktopStartMenu" => "桌面与开始菜单",
        "FastScroll" => "快速滚动",
        "PrecisionScroll" => "精确滚动",
        "ScrollMove" => "滚动与移动",
        "DesktopNavigation" => "虚拟桌面与任务视图",
        "BrowserTabNavigation" => "网页与标签导航",
        "None" => "不执行动作",
        _ => actionType ?? "未设置"
    };

    private static string GetButtonDisplayName(string button) => button switch
    {
        "MButton" => "中键",
        "XButton1" => "按键 4",
        "XButton2" => "按键 5",
        _ => "鼠标按键"
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
    }

    private void CoreStatusTimer_Tick(object? sender, EventArgs e) => SyncRunningState();

    internal void SyncRunningState()
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

    protected override void OnStateChanged(EventArgs e)
    {
        base.OnStateChanged(e);
        if (WindowState == WindowState.Minimized)
        {
            MemoryService.TrimWorkingSet();
        }
    }

    private void UpdateScrollModeRadios()
    {
        if (config?.Scroll is null || ScrollModeNativeRadio is null || ScrollModeSmoothRadio is null || ScrollModeInertiaRadio is null)
        {
            return;
        }

        isUpdatingScrollMode = true;
        try
        {
            if (!config.Scroll.TakeOverWheel)
            {
                ScrollModeNativeRadio.IsChecked = true;
                if (ScrollSpeedSlider is not null) ScrollSpeedSlider.IsEnabled = false;
                if (ScrollSpeedDescription is not null) ScrollSpeedDescription.Text = "原生模式下滚动速度由 Windows 系统设置管理。";
            }
            else if (config.Scroll.Inertia)
            {
                ScrollModeInertiaRadio.IsChecked = true;
                if (ScrollSpeedSlider is not null) ScrollSpeedSlider.IsEnabled = true;
                if (ScrollSpeedDescription is not null) ScrollSpeedDescription.Text = "较低适合精细阅读，较高适合快速浏览。";
            }
            else
            {
                ScrollModeSmoothRadio.IsChecked = true;
                if (ScrollSpeedSlider is not null) ScrollSpeedSlider.IsEnabled = true;
                if (ScrollSpeedDescription is not null) ScrollSpeedDescription.Text = "较低适合精细阅读，较高适合快速浏览。";
            }
        }
        finally
        {
            isUpdatingScrollMode = false;
        }
    }

    private void ScrollModeRadio_Checked(object sender, RoutedEventArgs e)
    {
        if (isUpdatingScrollMode || config?.Scroll is null)
        {
            return;
        }

        if (ReferenceEquals(sender, ScrollModeNativeRadio))
        {
            config.Scroll.TakeOverWheel = false;
            config.Scroll.Smooth = false;
            config.Scroll.Inertia = false;
            if (ScrollSpeedSlider is not null) ScrollSpeedSlider.IsEnabled = false;
            if (ScrollSpeedDescription is not null) ScrollSpeedDescription.Text = "原生模式下滚动速度由 Windows 系统设置管理。";
        }
        else if (ReferenceEquals(sender, ScrollModeSmoothRadio))
        {
            config.Scroll.TakeOverWheel = true;
            config.Scroll.Smooth = true;
            config.Scroll.Inertia = false;
            if (ScrollSpeedSlider is not null) ScrollSpeedSlider.IsEnabled = true;
            if (ScrollSpeedDescription is not null) ScrollSpeedDescription.Text = "较低适合精细阅读，较高适合快速浏览。";
        }
        else if (ReferenceEquals(sender, ScrollModeInertiaRadio))
        {
            config.Scroll.TakeOverWheel = true;
            config.Scroll.Smooth = false;
            config.Scroll.Inertia = true;
            if (ScrollSpeedSlider is not null) ScrollSpeedSlider.IsEnabled = true;
            if (ScrollSpeedDescription is not null) ScrollSpeedDescription.Text = "较低适合精细阅读，较高适合快速浏览。";
        }

        ScheduleSave();
    }

    /// <summary>
    /// 弹出底部现代悬浮交互岛（Floating HUD Capsule），带平滑上浮淡入与自动淡出动效
    /// </summary>
    private void ShowStatus(string message)
    {
        StatusMessageText.Text = message;

        // 智能微标：错误使用感叹号，正常提示使用翠绿对勾
        var isWarn = message.Contains("无法") || message.Contains("失败") || message.Contains("错误") || message.Contains("请");
        StatusHudIcon.Text = isWarn ? "\uE7BA" : "\uE73E";
        StatusHudIcon.Foreground = new SolidColorBrush(isWarn ? Color.FromRgb(0xF5, 0x9E, 0x0B) : Color.FromRgb(0x34, 0xD3, 0x99));

        StatusHud.Visibility = Visibility.Visible;
        statusTimer.Stop();

        // 丝滑上浮与淡入动画 (160ms CubicEase)
        var floatUp = new DoubleAnimation(8, 0, TimeSpan.FromMilliseconds(160))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(160))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };

        StatusHudTransform.BeginAnimation(TranslateTransform.YProperty, floatUp);
        StatusHud.BeginAnimation(OpacityProperty, fadeIn);

        statusTimer.Start();
    }

    private void CloseStatus_Click(object sender, RoutedEventArgs e)
    {
        statusTimer.Stop();
        StatusHud.Visibility = Visibility.Collapsed;
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
        pendingUpdate = null;
        downloadedUpdate = null;
        DownloadUpdateButton.Visibility = Visibility.Collapsed;
        CancelUpdateButton.Visibility = Visibility.Collapsed;
        InstallUpdateButton.Visibility = Visibility.Collapsed;
        UpdateProgressPanel.Visibility = Visibility.Collapsed;

        try
        {
            var currentVersion = GetCurrentReleaseVersion();
            var result = await updateService.CheckAsync(currentVersion, config.ReceiveBetaUpdates);
            if (result.Status == UpdateCheckStatus.Error)
            {
                UpdateBadge.Visibility = Visibility.Collapsed;
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
                UpdateBadge.Visibility = Visibility.Collapsed;
                DownloadUpdateButton.Visibility = Visibility.Collapsed;
                UpdateProgressPanel.Visibility = Visibility.Collapsed;
                ShowStatus(config.ReceiveBetaUpdates
                    ? "暂未发布可检查的正式版或 Beta 版"
                    : "暂未发布可检查的正式版本");
                return;
            }

            var sourceName = result.Source == UpdateSource.Gitee ? "Gitee" : "GitHub";
            if (result.Status == UpdateCheckStatus.UpToDate)
            {
                UpdateBadge.Visibility = Visibility.Collapsed;
                DownloadUpdateButton.Visibility = Visibility.Collapsed;
                UpdateProgressPanel.Visibility = Visibility.Collapsed;
                ShowStatus($"当前已是最新版本 {currentVersion}（{sourceName}）");
                return;
            }

            if (result.ReleaseVersion is null)
            {
                ShowStatus("新版本信息不完整，请稍后重试");
                return;
            }

            pendingUpdate = result;
            downloadedUpdate = null;
            UpdateBadge.Visibility = Visibility.Visible;
            DownloadUpdateButton.Visibility = result.CanDownload ? Visibility.Visible : Visibility.Collapsed;
            UpdateProgressPanel.Visibility = Visibility.Visible;
            UpdateStatusText.Text = result.CanDownload
                ? $"发现新版本 {result.DisplayName ?? result.ReleaseVersion.ToString()}（{sourceName}），点击“下载更新”"
                : "发现新版本，但 Release 中没有可用安装包。";
            ShowStatus(UpdateStatusText.Text);
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

    private async void DownloadUpdate_Click(object sender, RoutedEventArgs e)
    {
        if (pendingUpdate is null || !pendingUpdate.CanDownload)
        {
            return;
        }

        updateDownloadCancellation?.Dispose();
        updateDownloadCancellation = new CancellationTokenSource();
        DownloadUpdateButton.Visibility = Visibility.Collapsed;
        CancelUpdateButton.Visibility = Visibility.Visible;
        InstallUpdateButton.Visibility = Visibility.Collapsed;
        UpdateProgressBar.Value = 0;
        UpdateProgressPanel.Visibility = Visibility.Visible;
        UpdateStatusText.Text = "正在下载更新…";
        try
        {
            var progress = new Progress<DownloadProgress>(value =>
            {
                UpdateProgressBar.Value = value.Percentage;
                UpdateStatusText.Text = value.TotalBytes > 0
                    ? $"正在下载更新… {value.Percentage:0}%（{value.BytesReceived / 1024d / 1024d:0.0} / {value.TotalBytes / 1024d / 1024d:0.0} MB）"
                    : $"正在下载更新… {value.BytesReceived / 1024d / 1024d:0.0} MB";
            });
            downloadedUpdate = await updateService.DownloadAndVerifyAsync(pendingUpdate, progress, updateDownloadCancellation.Token);
            UpdateProgressBar.Value = 100;
            UpdateStatusText.Text = "下载完成，完整性校验通过。可以立即安装。";
            InstallUpdateButton.Visibility = Visibility.Visible;
            UpdateBadge.Visibility = Visibility.Collapsed;
        }
        catch (OperationCanceledException)
        {
            UpdateStatusText.Text = "已取消下载。";
            DownloadUpdateButton.Visibility = Visibility.Visible;
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidDataException or IOException or InvalidOperationException)
        {
            UpdateStatusText.Text = $"下载失败：{ex.Message}";
            DownloadUpdateButton.Visibility = Visibility.Visible;
        }
        finally
        {
            CancelUpdateButton.Visibility = Visibility.Collapsed;
            updateDownloadCancellation?.Dispose();
            updateDownloadCancellation = null;
        }
    }

    private void CancelUpdate_Click(object sender, RoutedEventArgs e)
    {
        updateDownloadCancellation?.Cancel();
    }

    private async void InstallUpdate_Click(object sender, RoutedEventArgs e)
    {
        if (downloadedUpdate is null || !File.Exists(downloadedUpdate.InstallerPath))
        {
            UpdateStatusText.Text = "安装包不存在，请重新下载。";
            DownloadUpdateButton.Visibility = Visibility.Visible;
            InstallUpdateButton.Visibility = Visibility.Collapsed;
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(downloadedUpdate.InstallerPath) { UseShellExecute = true });
            await PrepareForApplicationShutdownAsync();
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException)
        {
            UpdateStatusText.Text = $"无法启动安装程序：{ex.Message}";
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

        return ReleaseVersion.FromVersion(assembly.GetName().Version ?? new Version(0, 1, 3));
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

    internal void ShowSettingsWindow()
    {
        StartHidden = false;
        Opacity = 1;
        ShowActivated = true;
        ShowInTaskbar = true;
        Show();
        coreStatusTimer.Start();
        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        Activate();
        Topmost = true;
        Topmost = false;
        Focus();
    }

    internal async Task<bool> EnterLightweightModeAsync()
    {
        if (exiting || enteringLightweightMode)
        {
            return false;
        }

        if (!await SaveConfigurationAsync(showConfirmation: false))
        {
            return false;
        }

        enteringLightweightMode = true;
        saveTimer.Stop();
        statusTimer.Stop();
        captureClickTimer.Stop();
        captureHoldTimer.Stop();
        coreStatusTimer.Stop();
        saveTimer.Tick -= SaveTimer_Tick;
        statusTimer.Tick -= StatusTimer_Tick;
        captureClickTimer.Tick -= CaptureClickTimer_Tick;
        captureHoldTimer.Tick -= CaptureHoldTimer_Tick;
        coreStatusTimer.Tick -= CoreStatusTimer_Tick;
        foreach (var ownedWindow in OwnedWindows.Cast<Window>().ToArray())
        {
            ownedWindow.Close();
        }

        UnsubscribeFromConfig(config);
        DataContext = null;
        coreProcessService.Resume();
        Close();
        return true;
    }

    internal async Task PrepareForApplicationShutdownAsync()
    {
        if (exiting)
        {
            return;
        }

        exiting = true;
        updateDownloadCancellation?.Cancel();
        saveTimer.Stop();
        statusTimer.Stop();
        captureClickTimer.Stop();
        captureHoldTimer.Stop();
        coreStatusTimer.Stop();
        foreach (var ownedWindow in OwnedWindows.Cast<Window>().ToArray())
        {
            ownedWindow.Close();
        }

        coreProcessService.Resume();
        await SaveConfigurationAsync(showConfirmation: false);
        Close();
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (exiting || enteringLightweightMode)
        {
            return;
        }

        e.Cancel = true;
        captureHoldTimer.Stop();
        capturedButton = null;
        ResetCapturedGestureState();
        coreProcessService.Resume();
        Hide();
        coreStatusTimer.Stop();
        ScheduleSave();
        WindowHiddenToTray?.Invoke(this, EventArgs.Empty);

    }

    private void Window_MouseActivity(object sender, System.Windows.Input.MouseEventArgs e) =>
        ActivityDetected?.Invoke(this, EventArgs.Empty);

    private void Window_KeyActivity(object sender, System.Windows.Input.KeyEventArgs e) =>
        ActivityDetected?.Invoke(this, EventArgs.Empty);
}
