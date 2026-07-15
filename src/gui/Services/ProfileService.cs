using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WinMouseFix.Gui.Models;

namespace WinMouseFix.Gui.Services;

public sealed class ProfileService
{
    public const string DefaultProfileName = "默认配置";
    private const int MaximumBackupCount = 30;

    private static readonly HashSet<string> ReservedNames = new(
        new[] { "CON", "PRN", "AUX", "NUL" }
            .Concat(Enumerable.Range(1, 9).SelectMany(number => new[] { $"COM{number}", $"LPT{number}" })),
        StringComparer.OrdinalIgnoreCase);

    private readonly ConfigurationService configurationService;
    private readonly SemaphoreSlim gate = new(1, 1);
    private bool initialized;
    private string activeProfileName = DefaultProfileName;

    public ProfileService(string? configDirectory = null)
    {
        configurationService = new ConfigurationService(configDirectory);
        ProfilesDirectory = Path.Combine(configurationService.ConfigDirectory, "profiles");
        BackupsDirectory = Path.Combine(configurationService.ConfigDirectory, "backups");
        ActiveProfilePath = Path.Combine(configurationService.ConfigDirectory, "active-profile.txt");
        PendingSelectionPath = Path.Combine(configurationService.ConfigDirectory, "pending-profile.txt");
    }

    public string ProfilesDirectory { get; }
    public string BackupsDirectory { get; }
    public string ActiveProfilePath { get; }
    public string PendingSelectionPath { get; }

    public Task<ProfileSelection> InitializeAsync() => Locked(async () =>
    {
        await EnsureInitializedAsync();
        return new ProfileSelection(activeProfileName, await LoadProfileAsync(activeProfileName));
    });

    public Task<IReadOnlyList<string>> ListAsync() => Locked(async () =>
    {
        await EnsureInitializedAsync();
        return GetProfileNames();
    });

    public Task<string> GetActiveNameAsync() => Locked(async () =>
    {
        await EnsureInitializedAsync();
        return activeProfileName;
    });

    public Task<IReadOnlyList<ProfileBackup>> ListBackupsAsync() => Locked(async () =>
    {
        await EnsureInitializedAsync();
        return (IReadOnlyList<ProfileBackup>)Directory.EnumerateFiles(BackupsDirectory, "*.json", SearchOption.TopDirectoryOnly)
            .Select(path => new ProfileBackup(Path.GetFileName(path), File.GetLastWriteTime(path)))
            .OrderByDescending(backup => backup.CreatedAt)
            .ToArray();
    });

    public Task SaveCurrentAsync(AppConfig config) => Locked(async () =>
    {
        RequireConfig(config, nameof(config));
        await EnsureInitializedAsync();
        await SaveCurrentAsyncCore(config);
    });

    public Task<ProfileSelection> CreateAsync(string name, AppConfig currentConfig) => Locked(async () =>
    {
        RequireConfig(currentConfig, nameof(currentConfig));
        ValidateName(name);
        await EnsureInitializedAsync();
        EnsureMissing(name);
        await SaveCurrentAsyncCore(currentConfig);
        await SaveProfileAsync(name, currentConfig);
        await ActivateProfileAsync(name, currentConfig);
        return new ProfileSelection(name, currentConfig);
    });

    public Task<ProfileSelection> SwitchAsync(string name, AppConfig currentConfig) => Locked(async () =>
    {
        RequireConfig(currentConfig, nameof(currentConfig));
        ValidateName(name);
        await EnsureInitializedAsync();
        EnsureExists(name);
        await SaveCurrentAsyncCore(currentConfig);
        if (string.Equals(name, activeProfileName, StringComparison.OrdinalIgnoreCase))
        {
            return new ProfileSelection(activeProfileName, currentConfig);
        }

        var selected = await LoadProfileAsync(name);
        await ActivateProfileAsync(name, selected);
        return new ProfileSelection(name, selected);
    });

    public Task RenameAsync(string existingName, string newName) => Locked(async () =>
    {
        ValidateName(existingName);
        ValidateName(newName);
        await EnsureInitializedAsync();
        EnsureExists(existingName);
        if (string.Equals(existingName, newName, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        EnsureMissing(newName);
        if (string.Equals(existingName, activeProfileName, StringComparison.OrdinalIgnoreCase))
        {
            WritePendingSelection(newName);
            Directory.Move(ProfileDirectory(existingName), ProfileDirectory(newName));
            WriteActiveName(newName);
            activeProfileName = newName;
            ClearPendingSelection();
            return;
        }

        Directory.Move(ProfileDirectory(existingName), ProfileDirectory(newName));
    });

    public Task<ProfileSelection> DeleteAsync(string name, AppConfig currentConfig) => Locked(async () =>
    {
        RequireConfig(currentConfig, nameof(currentConfig));
        ValidateName(name);
        await EnsureInitializedAsync();
        EnsureExists(name);
        var names = GetProfileNames();
        if (names.Count == 1)
        {
            throw new InvalidOperationException("至少需要保留一个配置档案。");
        }

        await SaveCurrentAsyncCore(currentConfig);
        if (!string.Equals(name, activeProfileName, StringComparison.OrdinalIgnoreCase))
        {
            Directory.Delete(ProfileDirectory(name), true);
            return new ProfileSelection(activeProfileName, currentConfig);
        }

        var nextName = names.First(candidate => !string.Equals(candidate, name, StringComparison.OrdinalIgnoreCase));
        var nextConfig = await LoadProfileAsync(nextName);
        await ActivateProfileAsync(nextName, nextConfig);
        Directory.Delete(ProfileDirectory(name), true);
        return new ProfileSelection(nextName, nextConfig);
    });

    public Task<ProfileSelection> ImportAsync(
        string sourcePath,
        string profileName,
        AppConfig currentConfig) => Locked(async () =>
    {
        RequireConfig(currentConfig, nameof(currentConfig));
        ValidateName(profileName);
        var source = ValidateJsonPath(sourcePath, true);
        await EnsureInitializedAsync();
        EnsureMissing(profileName);
        ValidateImportedDocument(source);
        var imported = await LoadStrictAsync(source);
        await SaveCurrentAsyncCore(currentConfig);
        await SaveProfileAsync(profileName, imported);
        await ActivateProfileAsync(profileName, imported);
        return new ProfileSelection(profileName, imported);
    });

    public Task ExportAsync(string profileName, string destinationPath) => Locked(async () =>
    {
        ValidateName(profileName);
        var destination = ValidateJsonPath(destinationPath, false);
        EnsureOutsideConfigDirectory(destination);
        await EnsureInitializedAsync();
        EnsureExists(profileName);
        CopyAtomically(ProfileConfigPath(profileName), destination);
    });

    public Task<string> CreateBackupAsync(AppConfig currentConfig) => Locked(async () =>
    {
        RequireConfig(currentConfig, nameof(currentConfig));
        await EnsureInitializedAsync();
        await SaveCurrentAsyncCore(currentConfig);
        var stem = $"{DateTime.Now:yyyyMMdd-HHmmssfff}-{activeProfileName}";
        var path = Path.Combine(BackupsDirectory, stem + ".json");
        for (var suffix = 2; File.Exists(path); suffix++)
        {
            path = Path.Combine(BackupsDirectory, $"{stem}-{suffix}.json");
        }

        File.Copy(ProfileConfigPath(activeProfileName), path);
        TrimBackups();
        return Path.GetFileName(path);
    });

    public Task<ProfileSelection> RestoreBackupAsync(
        string backupFileName,
        AppConfig currentConfig) => Locked(async () =>
    {
        RequireConfig(currentConfig, nameof(currentConfig));
        var backupPath = BackupPath(backupFileName);
        await EnsureInitializedAsync();
        if (!File.Exists(backupPath))
        {
            throw new FileNotFoundException("找不到指定的配置备份。", backupPath);
        }

        var restored = await LoadStrictAsync(backupPath);
        await SaveCurrentAsyncCore(currentConfig);
        await SaveProfileAsync(activeProfileName, restored);
        await configurationService.SaveAsync(restored);
        return new ProfileSelection(activeProfileName, restored);
    });

    private async Task EnsureInitializedAsync()
    {
        if (initialized)
        {
            return;
        }

        Directory.CreateDirectory(ProfilesDirectory);
        Directory.CreateDirectory(BackupsDirectory);
        await RecoverPendingSelectionAsync();
        var names = GetProfileNames();
        if (names.Count == 0)
        {
            var current = await configurationService.LoadAsync();
            await SaveProfileAsync(DefaultProfileName, current);
            await configurationService.SaveAsync(current);
            WriteActiveName(DefaultProfileName);
            activeProfileName = DefaultProfileName;
        }
        else
        {
            var stored = ReadActiveName();
            activeProfileName = names.FirstOrDefault(name =>
                string.Equals(name, stored, StringComparison.OrdinalIgnoreCase)) ?? names[0];
            var current = await LoadProfileAsync(activeProfileName);
            await configurationService.SaveAsync(current);
            WriteActiveName(activeProfileName);
        }

        initialized = true;
    }

    private async Task SaveCurrentAsyncCore(AppConfig config)
    {
        await SaveProfileAsync(activeProfileName, config);
        await configurationService.SaveAsync(config);
    }

    private async Task ActivateProfileAsync(string name, AppConfig config)
    {
        WritePendingSelection(name);
        await configurationService.SaveAsync(config);
        WriteActiveName(name);
        activeProfileName = name;
        ClearPendingSelection();
    }

    private async Task RecoverPendingSelectionAsync()
    {
        var pendingName = ReadProfileName(PendingSelectionPath);
        if (pendingName is null || !File.Exists(ProfileConfigPath(pendingName)))
        {
            ClearPendingSelection();
            return;
        }

        var selected = await LoadProfileAsync(pendingName);
        await configurationService.SaveAsync(selected);
        WriteActiveName(pendingName);
        activeProfileName = pendingName;
        ClearPendingSelection();
    }

    private Task SaveProfileAsync(string name, AppConfig config)
    {
        var directory = ProfileDirectory(name);
        Directory.CreateDirectory(directory);
        return new ConfigurationService(directory).SaveAsync(config);
    }

    private Task<AppConfig> LoadProfileAsync(string name) => LoadStrictAsync(ProfileConfigPath(name));

    private async Task<AppConfig> LoadStrictAsync(string sourcePath)
    {
        ValidateVersion(sourcePath);
        var temporaryDirectory = Path.Combine(ProfilesDirectory, ".read-" + Guid.NewGuid().ToString("N"));
        EnsureContained(ProfilesDirectory, temporaryDirectory);
        Directory.CreateDirectory(temporaryDirectory);
        try
        {
            File.Copy(sourcePath, Path.Combine(temporaryDirectory, "config.json"));
            var config = await new ConfigurationService(temporaryDirectory).LoadAsync();
            if (Directory.EnumerateFiles(temporaryDirectory, "config.invalid-*.json").Any())
            {
                throw new InvalidDataException("配置文件内容无效。");
            }

            config.Normalize();
            return config;
        }
        finally
        {
            if (Directory.Exists(temporaryDirectory))
            {
                Directory.Delete(temporaryDirectory, true);
            }
        }
    }

    private static void ValidateVersion(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException("配置文件根节点必须是 JSON 对象。");
        }

        var version = 1;
        if (document.RootElement.TryGetProperty("configVersion", out var element) && !element.TryGetInt32(out version))
        {
            throw new InvalidDataException("configVersion 必须是整数。");
        }

        if (version != 1 && version != AppConfig.CurrentVersion)
        {
            throw new InvalidDataException($"不支持 configVersion {version}。");
        }
    }

    private static void ValidateImportedDocument(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException("导入配置的根节点必须是 JSON 对象。");
        }

        var fields = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in document.RootElement.EnumerateObject())
        {
            if (!fields.Add(property.Name))
            {
                throw new InvalidDataException($"导入配置包含重复的顶层字段：{property.Name}。");
            }
        }

        if (!document.RootElement.TryGetProperty("configVersion", out var versionElement) ||
            !versionElement.TryGetInt32(out var version))
        {
            throw new InvalidDataException("导入配置缺少整数 configVersion 字段。");
        }

        var requiredFields = version == 1
            ? new[] { "configVersion", "enabled", "pauseInFullscreen", "buttons", "scroll", "excludedApps", "startup" }
            : new[]
            {
                "configVersion", "enabled", "pauseInFullscreen", "doubleClickSpeed",
                "desktopSwipeDirection", "remaps", "scroll", "excludedApps", "startup"
            };
        var allowedFields = version == 1
            ? requiredFields
            : requiredFields.Concat(new[] { "receiveBetaUpdates" }).ToArray();

        if (version != 1 && version != AppConfig.CurrentVersion)
        {
            throw new InvalidDataException($"导入配置使用了不支持的 configVersion {version}。");
        }

        var missing = requiredFields.Where(field => !fields.Contains(field)).ToArray();
        if (missing.Length > 0)
        {
            throw new InvalidDataException($"导入配置不完整，缺少：{string.Join("、", missing)}。");
        }

        var unexpected = fields.Where(field => !allowedFields.Contains(field, StringComparer.Ordinal)).ToArray();
        if (unexpected.Length > 0)
        {
            throw new InvalidDataException($"导入配置包含未知字段：{string.Join("、", unexpected)}。");
        }
    }

    private IReadOnlyList<string> GetProfileNames() =>
        Directory.EnumerateDirectories(ProfilesDirectory)
            .Select(Path.GetFileName)
            .Where(name => IsValidName(name) && File.Exists(ProfileConfigPath(name!)))
            .Select(name => name!)
            .OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();

    private string? ReadActiveName() => ReadProfileName(ActiveProfilePath);

    private static string? ReadProfileName(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        var name = File.ReadAllText(path);
        return IsValidName(name) ? name : null;
    }

    private void WriteActiveName(string name)
    {
        ValidateName(name);
        WriteTextAtomically(ActiveProfilePath, name);
    }

    private void WritePendingSelection(string name)
    {
        ValidateName(name);
        WriteTextAtomically(PendingSelectionPath, name);
    }

    private void ClearPendingSelection()
    {
        if (File.Exists(PendingSelectionPath))
        {
            File.Delete(PendingSelectionPath);
        }
    }

    private void WriteTextAtomically(string path, string content)
    {
        Directory.CreateDirectory(configurationService.ConfigDirectory);
        var temporaryPath = path + ".tmp";
        File.WriteAllText(temporaryPath, content);
        if (File.Exists(path))
        {
            File.Replace(temporaryPath, path, null);
        }
        else
        {
            File.Move(temporaryPath, path);
        }
    }

    private void TrimBackups()
    {
        var expired = Directory.EnumerateFiles(BackupsDirectory, "*.json", SearchOption.TopDirectoryOnly)
            .OrderByDescending(File.GetLastWriteTime)
            .ThenByDescending(Path.GetFileName, StringComparer.Ordinal)
            .Skip(MaximumBackupCount)
            .ToArray();
        foreach (var path in expired)
        {
            File.Delete(path);
        }
    }

    private string ProfileDirectory(string name)
    {
        ValidateName(name);
        var path = Path.GetFullPath(Path.Combine(ProfilesDirectory, name));
        EnsureContained(ProfilesDirectory, path);
        return path;
    }

    private string ProfileConfigPath(string name) => Path.Combine(ProfileDirectory(name), "config.json");

    private string BackupPath(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName) ||
            !string.Equals(fileName, Path.GetFileName(fileName), StringComparison.Ordinal) ||
            !string.Equals(Path.GetExtension(fileName), ".json", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("备份文件名无效。", nameof(fileName));
        }

        var path = Path.GetFullPath(Path.Combine(BackupsDirectory, fileName));
        EnsureContained(BackupsDirectory, path);
        return path;
    }

    private void EnsureExists(string name)
    {
        if (!File.Exists(ProfileConfigPath(name)))
        {
            throw new DirectoryNotFoundException($"找不到配置档案“{name}”。");
        }
    }

    private void EnsureMissing(string name)
    {
        if (Directory.Exists(ProfileDirectory(name)))
        {
            throw new InvalidOperationException($"配置档案“{name}”已存在。");
        }
    }

    private static string ValidateJsonPath(string path, bool mustExist)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("JSON 文件路径不能为空。", nameof(path));
        }

        var fullPath = Path.GetFullPath(path);
        if (!string.Equals(Path.GetExtension(fullPath), ".json", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("配置文件必须使用 .json 扩展名。", nameof(path));
        }

        if (mustExist && !File.Exists(fullPath))
        {
            throw new FileNotFoundException("找不到指定的配置文件。", fullPath);
        }

        if (!Directory.Exists(Path.GetDirectoryName(fullPath)))
        {
            throw new DirectoryNotFoundException("配置文件所在目录不存在。");
        }

        return fullPath;
    }

    private void EnsureOutsideConfigDirectory(string path)
    {
        var root = WithTrailingSeparator(configurationService.ConfigDirectory);
        if (Path.GetFullPath(path).StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("导出路径不能位于应用配置目录内。", nameof(path));
        }
    }

    private static void CopyAtomically(string source, string destination)
    {
        var temporaryPath = destination + ".tmp";
        File.Copy(source, temporaryPath, true);
        if (File.Exists(destination))
        {
            File.Replace(temporaryPath, destination, null);
        }
        else
        {
            File.Move(temporaryPath, destination);
        }
    }

    private static void ValidateName(string name)
    {
        if (!IsValidName(name))
        {
            throw new ArgumentException(
                "配置档案名称不能为空、超过 64 个字符、包含路径字符、使用系统保留名称，或以空格和句点结尾。",
                nameof(name));
        }
    }

    private static bool IsValidName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        var value = name!;
        return value.Length <= 64 &&
               string.Equals(value, value.Trim(), StringComparison.Ordinal) &&
               !value.StartsWith(".", StringComparison.Ordinal) &&
               !value.EndsWith(".", StringComparison.Ordinal) &&
               value.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 &&
               !ReservedNames.Contains(value.Split('.')[0]);
    }

    private static void EnsureContained(string rootDirectory, string path)
    {
        if (!Path.GetFullPath(path).StartsWith(WithTrailingSeparator(rootDirectory), StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("配置路径超出允许的目录。", nameof(path));
        }
    }

    private static string WithTrailingSeparator(string path) =>
        Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
        Path.DirectorySeparatorChar;

    private async Task<T> Locked<T>(Func<Task<T>> action)
    {
        await gate.WaitAsync();
        try
        {
            return await action();
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task Locked(Func<Task> action)
    {
        await gate.WaitAsync();
        try
        {
            await action();
        }
        finally
        {
            gate.Release();
        }
    }

    private static void RequireConfig(AppConfig? config, string parameterName)
    {
        if (config is null)
        {
            throw new ArgumentNullException(parameterName);
        }
    }
}

public sealed class ProfileSelection
{
    public ProfileSelection(string name, AppConfig config)
    {
        Name = name;
        Config = config;
    }

    public string Name { get; }
    public AppConfig Config { get; }
}

public sealed class ProfileBackup
{
    public ProfileBackup(string fileName, DateTime createdAt)
    {
        FileName = fileName;
        CreatedAt = createdAt;
    }

    public string FileName { get; }
    public DateTime CreatedAt { get; }
}
