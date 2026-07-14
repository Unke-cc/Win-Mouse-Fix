using System.Collections.ObjectModel;
using System.Text.Json.Serialization;

namespace WinMouseFix.Gui.Models;

public sealed class AppConfig : BindableBase
{
    public const int CurrentVersion = 2;

    private int configVersion = CurrentVersion;
    private bool enabled = true;
    private bool pauseInFullscreen = true;
    private string doubleClickSpeed = "fast";
    private string desktopSwipeDirection = "followMouse";
    private ObservableCollection<RemapConfig> remaps = CreateRecommendedRemaps();
    private ScrollConfig scroll = new();
    private ObservableCollection<ExcludedApp> excludedApps = [];
    private StartupConfig startup = new();

    public int ConfigVersion
    {
        get => configVersion;
        set => SetProperty(ref configVersion, value);
    }

    public bool Enabled
    {
        get => enabled;
        set => SetProperty(ref enabled, value);
    }

    public bool PauseInFullscreen
    {
        get => pauseInFullscreen;
        set => SetProperty(ref pauseInFullscreen, value);
    }

    public string DoubleClickSpeed
    {
        get => doubleClickSpeed;
        set => SetProperty(ref doubleClickSpeed, NormalizeDoubleClickSpeed(value));
    }

    public string DesktopSwipeDirection
    {
        get => desktopSwipeDirection;
        set => SetProperty(ref desktopSwipeDirection, IsDesktopSwipeDirection(value) ? value : "followMouse");
    }

    public ObservableCollection<RemapConfig> Remaps
    {
        get => remaps;
        set => SetProperty(ref remaps, value ?? []);
    }

    public ScrollConfig Scroll
    {
        get => scroll;
        set => SetProperty(ref scroll, value ?? new ScrollConfig());
    }

    public ObservableCollection<ExcludedApp> ExcludedApps
    {
        get => excludedApps;
        set => SetProperty(ref excludedApps, value ?? []);
    }

    public StartupConfig Startup
    {
        get => startup;
        set => SetProperty(ref startup, value ?? new StartupConfig());
    }

    public void Normalize()
    {
        ConfigVersion = CurrentVersion;
        DoubleClickSpeed = DoubleClickSpeed;
        DesktopSwipeDirection = DesktopSwipeDirection;
        Remaps ??= [];
        for (var index = Remaps.Count - 1; index >= 0; index--)
        {
            var remap = Remaps[index];
            if (remap is null)
            {
                Remaps.RemoveAt(index);
            }
            else
            {
                remap.Normalize();
            }
        }

        Scroll ??= new ScrollConfig();
        ExcludedApps ??= [];
        for (var index = ExcludedApps.Count - 1; index >= 0; index--)
        {
            if (ExcludedApps[index] is null)
            {
                ExcludedApps.RemoveAt(index);
            }
        }

        Startup ??= new StartupConfig();
    }

    public void RestoreRecommendedRemaps()
    {
        Remaps.Clear();
        foreach (var remap in CreateRecommendedRemaps())
        {
            Remaps.Add(remap);
        }
    }

    public static ObservableCollection<RemapConfig> CreateRecommendedRemaps() =>
    [
        RemapConfig.Create("MButton", "click", "Original"),
        RemapConfig.Create("MButton", "holdScroll", "FastScroll"),
        RemapConfig.Create("XButton1", "click", "Back"),
        RemapConfig.Create("XButton1", "doubleClick", "TaskView"),
        RemapConfig.Create("XButton1", "holdDrag", "DesktopNavigation"),
        RemapConfig.Create("XButton2", "click", "Forward"),
        RemapConfig.Create("XButton2", "holdScroll", "Zoom"),
        RemapConfig.Create("XButton2", "holdDrag", "ScrollMove")
    ];

    internal static bool IsDesktopSwipeDirection(string? value) =>
        value is "followMouse" or "oppositeMouse";

    private static string NormalizeDoubleClickSpeed(string? value) => value?.ToLowerInvariant() switch
    {
        "medium" => "medium",
        "slow" => "slow",
        _ => "fast"
    };
}

public sealed class RemapConfig : BindableBase
{
    private string button = string.Empty;
    private string trigger = "click";
    private int triggerOrder;
    private ActionConfig action = new();

    public string Button
    {
        get => button;
        set => SetProperty(ref button, value?.Trim() ?? string.Empty);
    }

    public string Trigger
    {
        get => trigger;
        set
        {
            var normalized = value?.Trim() ?? "click";
            if (SetProperty(ref trigger, normalized))
            {
                TriggerOrder = GetTriggerOrder(normalized);
            }
        }
    }

    [JsonIgnore]
    public int TriggerOrder
    {
        get => triggerOrder;
        private set => SetProperty(ref triggerOrder, value);
    }

    public ActionConfig Action
    {
        get => action;
        set => SetProperty(ref action, value ?? new ActionConfig());
    }

    public void Normalize()
    {
        Button = Button;
        Trigger = Trigger;
        TriggerOrder = GetTriggerOrder(Trigger);
        Action ??= new ActionConfig();
    }

    public static RemapConfig Create(string button, string trigger, string actionType) => new()
    {
        Button = button,
        Trigger = trigger,
        Action = ActionConfig.Create(actionType)
    };

    public static RemapConfig Create(string button, string trigger) =>
        Create(button, trigger, (button, trigger) switch
        {
            ("XButton1", "click") => "Back",
            ("XButton2", "click") => "Forward",
            ("XButton1", "holdDrag") => "DesktopNavigation",
            ("XButton2", "holdScroll") => "Zoom",
            (_, "holdScroll") => "FastScroll",
            (_, "holdDrag") => "ScrollMove",
            _ => "TaskView"
        });

    private static int GetTriggerOrder(string value) => value switch
    {
        "click" => 0,
        "hold" => 1,
        "doubleClick" => 2,
        "holdDrag" => 3,
        "holdScroll" => 4,
        "dragUp" => 5,
        "dragDown" => 6,
        "dragLeft" => 7,
        "dragRight" => 8,
        "wheelUp" => 9,
        "wheelDown" => 10,
        _ => 11
    };
}

public sealed class ActionConfig : BindableBase
{
    private string type = "None";
    private string shortcut = string.Empty;

    public string Type
    {
        get => type;
        set => SetProperty(ref type, string.IsNullOrWhiteSpace(value) ? "None" : value);
    }

    public string Shortcut
    {
        get => shortcut;
        set => SetProperty(ref shortcut, value ?? string.Empty);
    }

    public static ActionConfig Create(string type) => new() { Type = type };
}

public sealed class ScrollConfig : BindableBase
{
    private bool reverse;
    private double speed = 1.0;
    private bool smooth;

    public bool Reverse
    {
        get => reverse;
        set => SetProperty(ref reverse, value);
    }

    public double Speed
    {
        get => speed;
        set => SetProperty(ref speed, Math.Max(0.25, Math.Min(4.0, value)));
    }

    public bool Smooth
    {
        get => smooth;
        set => SetProperty(ref smooth, value);
    }
}

public sealed class ExcludedApp
{
    public string Name { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public string Mode { get; set; } = "disableAll";
}

public sealed class StartupConfig : BindableBase
{
    private bool runAtLogin;

    public bool RunAtLogin
    {
        get => runAtLogin;
        set => SetProperty(ref runAtLogin, value);
    }
}

public sealed class ActionOption
{
    public ActionOption(string type, string name)
    {
        Type = type;
        Name = name;
    }

    public string Type { get; }
    public string Name { get; }
}

internal sealed class LegacyAppConfig
{
    public bool Enabled { get; set; } = true;
    public bool PauseInFullscreen { get; set; } = true;
    public LegacyButtonCollection? Buttons { get; set; } = new();
    public ScrollConfig? Scroll { get; set; } = new();
    public ObservableCollection<ExcludedApp>? ExcludedApps { get; set; } = [];
    public StartupConfig? Startup { get; set; } = new();
}

internal sealed class LegacyButtonCollection
{
    public LegacyMouseButtonConfig? MButton { get; set; } = new();
    public LegacyMouseButtonConfig? XButton1 { get; set; } = new();
    public LegacyMouseButtonConfig? XButton2 { get; set; } = new();
}

internal sealed class LegacyMouseButtonConfig
{
    public ActionConfig? Click { get; set; } = new();
    public ActionConfig? DoubleClick { get; set; } = new();
    public ActionConfig? Hold { get; set; } = new();
    public ActionConfig? DragUp { get; set; } = new();
    public ActionConfig? DragDown { get; set; } = new();
    public ActionConfig? DragLeft { get; set; } = new();
    public ActionConfig? DragRight { get; set; } = new();
    public ActionConfig? WheelUp { get; set; } = new();
    public ActionConfig? WheelDown { get; set; } = new();
}
