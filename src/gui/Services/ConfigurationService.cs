using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using WinMouseFix.Gui.Models;

namespace WinMouseFix.Gui.Services;

public sealed class ConfigurationService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    public ConfigurationService(string? configDirectory = null)
    {
        ConfigDirectory = string.IsNullOrWhiteSpace(configDirectory)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WinMouseFix")
            : Path.GetFullPath(configDirectory);
    }

    public string ConfigDirectory { get; }

    public string ConfigPath => Path.Combine(ConfigDirectory, "config.json");

    public Task<AppConfig> LoadAsync()
    {
        if (!File.Exists(ConfigPath))
        {
            return Task.FromResult(new AppConfig());
        }

        try
        {
            var json = File.ReadAllText(ConfigPath);
            using var document = JsonDocument.Parse(json);
            var version = 1;
            if (document.RootElement.TryGetProperty("configVersion", out var versionElement))
            {
                if (!versionElement.TryGetInt32(out version))
                {
                    throw new JsonException("configVersion must be an integer.");
                }
            }

            if (version == 1)
            {
                var legacy = document.RootElement.Deserialize<LegacyAppConfig>(JsonOptions)
                             ?? new LegacyAppConfig();
                var migrated = MigrateLegacyConfig(legacy);
                if (PreserveLegacyConfig())
                {
                    TrySaveMigrated(migrated);
                }
                return Task.FromResult(migrated);
            }

            if (version != AppConfig.CurrentVersion)
            {
                throw new JsonException($"Unsupported configVersion: {version}.");
            }

            var rewriteDesktopSwipeDirection =
                !document.RootElement.TryGetProperty("desktopSwipeDirection", out var directionElement) ||
                directionElement.ValueKind != JsonValueKind.String ||
                !AppConfig.IsDesktopSwipeDirection(directionElement.GetString());
            var config = document.RootElement.Deserialize<AppConfig>(JsonOptions) ?? new AppConfig();
            config.Normalize();
            if (rewriteDesktopSwipeDirection)
            {
                TrySaveMigrated(config);
            }
            return Task.FromResult(config);
        }
        catch (JsonException)
        {
            PreserveInvalidConfig();
            return Task.FromResult(new AppConfig());
        }
        catch (IOException)
        {
            return Task.FromResult(new AppConfig());
        }
    }

    public Task SaveAsync(AppConfig config)
    {
        config.Normalize();
        Directory.CreateDirectory(ConfigDirectory);

        var temporaryPath = ConfigPath + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(config, JsonOptions));
        if (File.Exists(ConfigPath))
        {
            File.Replace(temporaryPath, ConfigPath, null);
        }
        else
        {
            File.Move(temporaryPath, ConfigPath);
        }

        return Task.CompletedTask;
    }

    private void PreserveInvalidConfig()
    {
        try
        {
            var backupPath = Path.Combine(ConfigDirectory, $"config.invalid-{DateTime.Now:yyyyMMdd-HHmmss}.json");
            File.Copy(ConfigPath, backupPath, overwrite: false);
        }
        catch (IOException)
        {
            // The default configuration remains usable even if a backup cannot be created.
        }
    }

    private bool PreserveLegacyConfig()
    {
        try
        {
            var stem = Path.Combine(ConfigDirectory, $"config.v1-{DateTime.Now:yyyyMMdd-HHmmssfff}");
            var backupPath = stem + ".json";
            for (var suffix = 2; File.Exists(backupPath); suffix++)
            {
                backupPath = $"{stem}-{suffix}.json";
            }

            File.Copy(ConfigPath, backupPath);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private void TrySaveMigrated(AppConfig config)
    {
        try
        {
            SaveAsync(config).GetAwaiter().GetResult();
        }
        catch (IOException)
        {
            // The migrated settings remain usable for this session.
        }
        catch (UnauthorizedAccessException)
        {
            // The migrated settings remain usable for this session.
        }
    }

    private static AppConfig MigrateLegacyConfig(LegacyAppConfig legacy)
    {
        var remaps = new System.Collections.ObjectModel.ObservableCollection<RemapConfig>();
        AddLegacyButton(remaps, "MButton", legacy.Buttons?.MButton);
        AddLegacyButton(remaps, "XButton1", legacy.Buttons?.XButton1);
        AddLegacyButton(remaps, "XButton2", legacy.Buttons?.XButton2);

        var config = new AppConfig
        {
            Enabled = legacy.Enabled,
            PauseInFullscreen = legacy.PauseInFullscreen,
            DoubleClickSpeed = "fast",
            DesktopSwipeDirection = "followMouse",
            Remaps = remaps,
            Scroll = legacy.Scroll ?? new ScrollConfig(),
            ExcludedApps = legacy.ExcludedApps ?? [],
            Startup = legacy.Startup ?? new StartupConfig()
        };
        config.Normalize();
        return config;
    }

    private static void AddLegacyButton(
        System.Collections.ObjectModel.ObservableCollection<RemapConfig> remaps,
        string button,
        LegacyMouseButtonConfig? mapping)
    {
        if (mapping is null)
        {
            return;
        }

        AddLegacyRemap(remaps, button, "click", mapping.Click);
        AddLegacyRemap(remaps, button, "doubleClick", mapping.DoubleClick);
        AddLegacyRemap(remaps, button, "hold", mapping.Hold);
        AddLegacyRemap(remaps, button, "dragUp", mapping.DragUp);
        AddLegacyRemap(remaps, button, "dragDown", mapping.DragDown);
        AddLegacyRemap(remaps, button, "dragLeft", mapping.DragLeft);
        AddLegacyRemap(remaps, button, "dragRight", mapping.DragRight);
        AddLegacyRemap(remaps, button, "wheelUp", mapping.WheelUp);
        AddLegacyRemap(remaps, button, "wheelDown", mapping.WheelDown);
    }

    private static void AddLegacyRemap(
        System.Collections.ObjectModel.ObservableCollection<RemapConfig> remaps,
        string button,
        string trigger,
        ActionConfig? action)
    {
        if (action is null || action.Type == "None")
        {
            return;
        }

        remaps.Add(new RemapConfig
        {
            Button = button,
            Trigger = trigger,
            Action = new ActionConfig { Type = action.Type, Shortcut = action.Shortcut }
        });
    }
}
