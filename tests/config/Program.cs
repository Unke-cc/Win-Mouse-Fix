using System.Text.Json;
using System.Globalization;
using System.Net;
using System.Net.Http;
using WinMouseFix.Gui.Controls;
using WinMouseFix.Gui.Models;
using WinMouseFix.Gui.Services;

var temporaryDirectory = Path.Combine(Path.GetTempPath(), $"winmousefix-config-check-{Guid.NewGuid():N}");
Directory.CreateDirectory(temporaryDirectory);

try
{
    var configPath = Path.Combine(temporaryDirectory, "config.json");
    var service = new ConfigurationService(temporaryDirectory);

    var modelDefault = new AppConfig();
    Assert(modelDefault.DesktopSwipeDirection == "followMouse", "Model default direction is incorrect.");
    Assert(!modelDefault.ReceiveBetaUpdates, "Beta updates must be disabled by default.");
    AssertFiveOrMoreDefaults(modelDefault.Remaps);
    Assert(modelDefault.Scroll.HorizontalModifier == "shift" && modelDefault.Scroll.FastModifier == "alt" &&
           modelDefault.Scroll.PrecisionModifier == "win" && modelDefault.Scroll.ZoomModifier == "ctrl",
        "Model default scroll modifiers are incorrect.");
    CheckTriggerOrder();
    CheckNewRemapDefaults();
    CheckHoldScrollMigration();
    CheckActionOptions();
    CheckDefaultFile();
    CheckSchema();
    await CheckProfileServiceAsync(Path.Combine(temporaryDirectory, "profile-service"));
    await CheckUpdateServiceAsync();

    File.WriteAllText(configPath, """
        {
          "configVersion": 1,
          "enabled": false,
          "pauseInFullscreen": false,
          "buttons": {
            "MButton": {
              "click": { "type": "Original", "shortcut": "" },
              "dragUp": { "type": "TaskView", "shortcut": "" }
            },
            "XButton1": {},
            "XButton2": {
              "wheelDown": { "type": "VolumeDown", "shortcut": "" }
            }
          },
          "scroll": { "reverse": true, "speed": 2.0, "smooth": true },
          "excludedApps": [],
          "startup": { "runAtLogin": true }
        }
        """);

    var migrated = await service.LoadAsync();
    Assert(migrated.ConfigVersion == 2, "configVersion was not migrated to 2.");
    Assert(migrated.DoubleClickSpeed == "fast", "Legacy double-click speed did not default to fast.");
    Assert(migrated.DesktopSwipeDirection == "followMouse", "Legacy direction did not default to followMouse.");
    Assert(HasRemap(migrated, "MButton", "click", "Original"), "Click mapping was not migrated.");
    Assert(HasRemap(migrated, "MButton", "dragUp", "TaskView"), "Directional drag mapping was not preserved.");
    Assert(HasRemap(migrated, "XButton2", "wheelDown", "VolumeDown"), "Directional wheel mapping was not preserved.");
    Assert(Directory.EnumerateFiles(temporaryDirectory, "config.v1-*.json").Any(),
        "The original version 1 configuration was not preserved before conversion.");

    using (var saved = JsonDocument.Parse(File.ReadAllText(configPath)))
    {
        Assert(!saved.RootElement.TryGetProperty("buttons", out _), "Obsolete buttons field remained after migration.");
        Assert(saved.RootElement.GetProperty("configVersion").GetInt32() == 2, "Migrated file was not rewritten as version 2.");
        Assert(ReadDirection(saved) == "followMouse", "Migrated file did not contain the default direction.");
    }

    File.WriteAllText(configPath, """{"configVersion":2}""");
    var missingField = await service.LoadAsync();
    Assert(missingField.DesktopSwipeDirection == "followMouse", "Missing direction did not default to followMouse.");
    using (var saved = JsonDocument.Parse(File.ReadAllText(configPath)))
    {
        Assert(ReadDirection(saved) == "followMouse", "Missing direction was not written back to disk.");
    }

    File.WriteAllText(configPath, """{"configVersion":2,"desktopSwipeDirection":"sideways"}""");
    var invalidField = await service.LoadAsync();
    Assert(invalidField.DesktopSwipeDirection == "followMouse", "Invalid direction was not normalized.");
    using (var saved = JsonDocument.Parse(File.ReadAllText(configPath)))
    {
        Assert(ReadDirection(saved) == "followMouse", "Normalized direction was not written back to disk.");
    }

    invalidField.DesktopSwipeDirection = "oppositeMouse";
    invalidField.ReceiveBetaUpdates = true;
    invalidField.Scroll.HorizontalModifier = "invalid";
    invalidField.Scroll.FastModifier = "ctrl";
    await service.SaveAsync(invalidField);
    var roundTrip = await service.LoadAsync();
    Assert(roundTrip.DesktopSwipeDirection == "oppositeMouse", "Direction did not survive save and reload.");
    Assert(roundTrip.ReceiveBetaUpdates, "Beta update preference did not survive save and reload.");
    Assert(roundTrip.Scroll.HorizontalModifier == "shift" && roundTrip.Scroll.FastModifier == "ctrl",
        "Scroll modifier normalization or persistence is incorrect.");
    roundTrip.Scroll.HorizontalModifier = "alt";
    roundTrip.Scroll.FastModifier = "alt";
    roundTrip.Normalize();
    Assert(roundTrip.Scroll.HorizontalModifier == "alt" && roundTrip.Scroll.FastModifier == "none",
        "Duplicate scroll modifiers were not removed in priority order.");
    using (var saved = JsonDocument.Parse(File.ReadAllText(configPath)))
    {
        Assert(saved.RootElement.GetProperty("remaps").EnumerateArray()
            .All(item => !item.TryGetProperty("triggerOrder", out _)), "triggerOrder was written to configuration JSON.");
    }

    migrated.Remaps.Clear();
    migrated.RestoreDefaultRemaps(threeButtonMouse: true);
    AssertThreeButtonDefaults(migrated.Remaps);
    migrated.RestoreDefaultRemaps(threeButtonMouse: false);
    AssertFiveOrMoreDefaults(migrated.Remaps);

    Console.WriteLine("Configuration, mouse defaults, scroll modifiers, filtered action options, profile management, and schema check passed.");
}
finally
{
    Directory.Delete(temporaryDirectory, recursive: true);
}

static void CheckDefaultFile()
{
    using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "default.json")));
    Assert(ReadDirection(document) == "followMouse", "Default file direction is incorrect.");
    Assert(!document.RootElement.GetProperty("receiveBetaUpdates").GetBoolean(),
        "Default file must disable Beta updates.");
    var remaps = document.RootElement.GetProperty("remaps").EnumerateArray().ToArray();
    Assert(remaps.Length == 6 && remaps.Take(3).All(item => item.GetProperty("button").GetString() == "XButton1") &&
           remaps.Skip(3).All(item => item.GetProperty("button").GetString() == "XButton2"),
        "Default file five-or-more-button mappings are incorrect.");
    Assert(remaps.Any(item => item.GetProperty("trigger").GetString() == "holdScroll" &&
                             item.GetProperty("action").GetProperty("type").GetString() == "DesktopStartMenu"),
        "Default file desktop and Start menu mapping is missing.");
    var scroll = document.RootElement.GetProperty("scroll");
    Assert(scroll.GetProperty("horizontalModifier").GetString() == "shift" &&
           scroll.GetProperty("fastModifier").GetString() == "alt" &&
           scroll.GetProperty("precisionModifier").GetString() == "win" &&
           scroll.GetProperty("zoomModifier").GetString() == "ctrl",
        "Default file scroll modifiers are incorrect.");
}

static void CheckSchema()
{
    using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "config.schema.json")));
    var root = document.RootElement;
    var directions = root.GetProperty("properties").GetProperty("desktopSwipeDirection").GetProperty("enum")
        .EnumerateArray().Select(item => item.GetString()).ToArray();
    Assert(directions.SequenceEqual(new[] { "followMouse", "oppositeMouse" }), "Schema direction values are incorrect.");

    var buttons = root.GetProperty("$defs").GetProperty("remap").GetProperty("properties")
        .GetProperty("button").GetProperty("enum").EnumerateArray()
        .Select(item => item.GetString()).ToArray();
    Assert(buttons.SequenceEqual(new[] { "MButton", "XButton1", "XButton2" }), "Schema does not limit mappings to the three supported buttons.");
    Assert(root.GetProperty("properties").TryGetProperty("receiveBetaUpdates", out _),
        "Schema does not define receiveBetaUpdates.");
    var scrollProperties = root.GetProperty("properties").GetProperty("scroll").GetProperty("properties");
    Assert(new[] { "horizontalModifier", "fastModifier", "precisionModifier", "zoomModifier" }
            .All(name => scrollProperties.TryGetProperty(name, out _)),
        "Schema does not define all scroll modifiers.");
    var modifiers = root.GetProperty("$defs").GetProperty("modifier").GetProperty("enum")
        .EnumerateArray().Select(item => item.GetString()).ToArray();
    Assert(modifiers.SequenceEqual(new[] { "none", "ctrl", "alt", "shift", "win" }),
        "Schema modifier values are incorrect.");
    var actionTypes = root.GetProperty("$defs").GetProperty("action").GetProperty("properties")
        .GetProperty("type").GetProperty("enum").EnumerateArray()
        .Select(item => item.GetString()).ToArray();
    Assert(new[] { "VolumeControl", "TabNavigation", "BrowserNavigation", "DesktopSwitch", "DesktopStartMenu", "BrowserTabNavigation" }
            .All(actionTypes.Contains),
        "Schema does not define all fused gesture actions.");
}

static void CheckTriggerOrder()
{
    var expected = new (string Trigger, int Order)[]
    {
        ("click", 0),
        ("hold", 1),
        ("doubleClick", 2),
        ("holdScroll", 3),
        ("holdDrag", 4),
        ("wheelUp", 5),
        ("wheelDown", 6),
        ("dragUp", 7),
        ("dragDown", 8),
        ("dragLeft", 9),
        ("dragRight", 10),
        ("unknown", 11)
    };
    var remap = new RemapConfig();
    var notifications = new List<string?>();
    remap.PropertyChanged += (_, eventArgs) => notifications.Add(eventArgs.PropertyName);

    foreach (var item in expected)
    {
        remap.Trigger = item.Trigger;
        Assert(remap.TriggerOrder == item.Order, $"Trigger order is incorrect for {item.Trigger}.");
    }

    Assert(notifications.Contains(nameof(RemapConfig.Trigger)), "Trigger change notification is missing.");
    Assert(notifications.Contains(nameof(RemapConfig.TriggerOrder)), "TriggerOrder change notification is missing.");

    var sorted = expected.Reverse()
        .Select(item => new RemapConfig { Trigger = item.Trigger })
        .OrderBy(item => item.TriggerOrder)
        .Select(item => item.Trigger)
        .ToArray();
    Assert(sorted.SequenceEqual(expected.Select(item => item.Trigger)), "Trigger sorting is not stable.");
}

static void CheckNewRemapDefaults()
{
    var expected = new (string Button, string Trigger, string Action)[]
    {
        ("MButton", "click", "TaskView"),
        ("XButton1", "click", "Back"),
        ("XButton2", "click", "Forward"),
        ("MButton", "hold", "TaskView"),
        ("XButton1", "doubleClick", "TaskView"),
        ("MButton", "holdScroll", "DesktopStartMenu"),
        ("XButton1", "holdScroll", "DesktopStartMenu"),
        ("XButton2", "holdScroll", "Zoom"),
        ("XButton1", "holdDrag", "DesktopNavigation"),
        ("XButton2", "holdDrag", "ScrollMove")
    };

    foreach (var item in expected)
    {
        Assert(RemapConfig.Create(item.Button, item.Trigger).Action.Type == item.Action,
            $"New remap default is incorrect for {item.Button}/{item.Trigger}.");
    }
}

static void CheckHoldScrollMigration()
{
    var config = new AppConfig();
    config.Remaps.Clear();
    var pairs = new (string Button, string Up, string Down, string Fused)[]
    {
        ("MButton", "FastScroll", "FastScroll", "FastScroll"),
        ("XButton1", "Zoom", "Zoom", "Zoom"),
        ("XButton2", "VolumeUp", "VolumeDown", "VolumeControl"),
        ("MButton", "PreviousTab", "NextTab", "TabNavigation"),
        ("XButton1", "Back", "Forward", "BrowserNavigation"),
        ("XButton2", "DesktopLeft", "DesktopRight", "DesktopSwitch"),
        ("MButton", "ShowDesktop", "StartMenu", "DesktopStartMenu")
    };
    foreach (var pair in pairs)
    {
        config.Remaps.Add(RemapConfig.Create(pair.Button, "wheelUp", pair.Up));
        config.Remaps.Add(RemapConfig.Create(pair.Button, "wheelDown", pair.Down));
        config.Normalize();
        Assert(HasRemap(config, pair.Button, "holdScroll", pair.Fused),
            $"{pair.Fused} directions were not merged.");
        config.Remaps.Clear();
    }

    config.Remaps.Add(RemapConfig.Create("XButton2", "wheelUp", "VolumeDown"));
    config.Remaps.Add(RemapConfig.Create("XButton2", "wheelUp", "VolumeUp"));
    config.Remaps.Add(RemapConfig.Create("XButton2", "wheelDown", "VolumeDown"));
    config.Remaps.Add(RemapConfig.Create("XButton1", "wheelUp", "Back"));
    config.Remaps.Add(RemapConfig.Create("XButton1", "wheelDown", "Forward"));
    config.Remaps.Add(RemapConfig.Create("MButton", "wheelUp", "StartMenu"));
    config.Remaps.Add(RemapConfig.Create("MButton", "wheelDown", "TaskView"));
    config.Normalize();

    Assert(HasRemap(config, "XButton2", "holdScroll", "VolumeControl"),
        "Volume directions were not merged into volume control.");
    Assert(HasRemap(config, "XButton1", "holdScroll", "BrowserNavigation"),
        "Browser directions were not merged into browser navigation.");
    Assert(HasRemap(config, "MButton", "wheelUp", "StartMenu") &&
           HasRemap(config, "MButton", "wheelDown", "TaskView"),
        "Unmatched directional mappings were not preserved.");
    Assert(config.Remaps.Count == 4, "Directional migration produced an incorrect mapping count.");
}

static void CheckActionOptions()
{
    var converter = new TriggerActionOptionsConverter();
    var common = ((ActionOption[])converter.Convert(
        "click", typeof(ActionOption[]), null!, CultureInfo.InvariantCulture))
        .Select(option => option.Type)
        .ToArray();
    foreach (var trigger in new[] { "click", "doubleClick", "hold", "wheelUp", "wheelDown" })
    {
        var options = (ActionOption[])converter.Convert(
            trigger, typeof(ActionOption[]), null!, CultureInfo.InvariantCulture);
        Assert(options.Select(option => option.Type).SequenceEqual(common),
            $"Common action options are incorrect for {trigger}.");
        Assert(options[0].Type == "Original" && options[0].Name == "执行原功能",
            $"Original action is not first for {trigger}.");
        Assert(options[options.Length - 1].Type == "None" && options[options.Length - 1].Name == "不执行动作",
            $"None action is not last for {trigger}.");
    }

    var holdScroll = ((ActionOption[])converter.Convert(
        "holdScroll", typeof(ActionOption[]), null!, CultureInfo.InvariantCulture))
        .Select(option => option.Type).ToArray();
    Assert(holdScroll.SequenceEqual(new[]
        { "Zoom", "VolumeControl", "TabNavigation", "BrowserNavigation", "DesktopSwitch", "DesktopStartMenu" }),
        "Hold-scroll action options are incorrect.");

    var holdDrag = ((ActionOption[])converter.Convert(
        "holdDrag", typeof(ActionOption[]), null!, CultureInfo.InvariantCulture))
        .Select(option => option.Type).ToArray();
    Assert(holdDrag.SequenceEqual(new[]
        { "ScrollMove", "DesktopNavigation", "BrowserTabNavigation" }),
        "Hold-drag action options are incorrect.");
    Assert(new[] { "FastScroll", "Zoom", "VolumeControl", "TabNavigation", "BrowserNavigation", "DesktopSwitch", "DesktopStartMenu",
                   "ScrollMove", "DesktopNavigation", "BrowserTabNavigation" }.All(item => !common.Contains(item)),
        "Gesture-only actions were exposed to unrelated operations.");
}

static async Task CheckProfileServiceAsync(string directory)
{
    var service = new ProfileService(directory);
    var initial = await service.InitializeAsync();
    Assert(initial.Name == ProfileService.DefaultProfileName, "Default profile was not created.");

    initial.Config.ReceiveBetaUpdates = true;
    initial.Config.Scroll.Speed = 1.75;
    await service.SaveCurrentAsync(initial.Config);
    var created = await service.CreateAsync("游戏", initial.Config);
    Assert(created.Name == "游戏", "New profile was not selected.");

    await service.RenameAsync("游戏", "游戏配置");
    Assert((await service.ListAsync()).Contains("游戏配置"), "Profile rename did not persist.");

    var backup = await service.CreateBackupAsync(created.Config);
    created.Config.Scroll.Speed = 3.0;
    await service.SaveCurrentAsync(created.Config);
    var restored = await service.RestoreBackupAsync(backup, created.Config);
    Assert(Math.Abs(restored.Config.Scroll.Speed - 1.75) < 0.001, "Profile backup was not restored.");

    var exportPath = Path.Combine(Path.GetDirectoryName(directory)!, "exported-profile.json");
    await service.ExportAsync("游戏配置", exportPath);

    var incompletePath = Path.Combine(Path.GetDirectoryName(directory)!, "incomplete-profile.json");
    File.WriteAllText(incompletePath, $"{{\"configVersion\":{AppConfig.CurrentVersion},\"enabled\":true}}");
    await AssertThrowsAsync(
        () => service.ImportAsync(incompletePath, "不完整配置", restored.Config),
        "Incomplete imported configuration was accepted.");

    var unexpectedPath = Path.Combine(Path.GetDirectoryName(directory)!, "unexpected-profile.json");
    var exportedJson = File.ReadAllText(exportPath).Trim();
    File.WriteAllText(unexpectedPath, exportedJson.Substring(0, exportedJson.Length - 1) + ",\n  \"unknownTopLevel\": true\n}");
    await AssertThrowsAsync(
        () => service.ImportAsync(unexpectedPath, "未知字段配置", restored.Config),
        "Imported configuration with an unknown top-level field was accepted.");

    var imported = await service.ImportAsync(exportPath, "导入配置", restored.Config);
    Assert(imported.Name == "导入配置" && imported.Config.ReceiveBetaUpdates,
        "Profile import or export lost settings.");

    var switched = await service.SwitchAsync(ProfileService.DefaultProfileName, imported.Config);
    Assert(switched.Name == ProfileService.DefaultProfileName &&
           File.ReadAllText(service.ActiveProfilePath) == ProfileService.DefaultProfileName,
        "Profile switch did not keep the active profile marker in sync.");

    for (var index = 0; index < 31; index++)
    {
        await service.CreateBackupAsync(switched.Config);
    }
    Assert((await service.ListBackupsAsync()).Count == 30, "Configuration backups were not limited to 30 files.");

    var afterDelete = await service.DeleteAsync("导入配置", switched.Config);
    Assert(afterDelete.Name != "导入配置" && !(await service.ListAsync()).Contains("导入配置"),
        "Profile deletion did not remove the selected profile.");
}

static async Task AssertThrowsAsync(Func<Task> operation, string message)
{
    try
    {
        await operation();
    }
    catch (InvalidDataException)
    {
        return;
    }

    throw new InvalidOperationException(message);
}

static async Task CheckUpdateServiceAsync()
{
    const string releases = """
        [
          { "tag_name": "v0.2.0-beta.1", "name": "Beta 0.2.0", "draft": false, "prerelease": true,
            "html_url": "https://github.com/Unke-cc/Win-Mouse-Fix/releases/tag/v0.2.0-beta.1" },
          { "tag_name": "v0.1.2", "name": "Stable 0.1.2", "draft": false, "prerelease": false,
            "html_url": "https://github.com/Unke-cc/Win-Mouse-Fix/releases/tag/v0.1.2" }
        ]
        """;
    using var githubClient = new HttpClient(new StubHttpHandler(_ =>
        new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(releases) }));
    var service = new UpdateService(githubClient);
    var stable = await service.CheckAsync(new Version(0, 1, 1), includeBeta: false);
    Assert(stable.Source == UpdateSource.GitHub && stable.Version == new Version(0, 1, 2),
        "Stable update selection is incorrect.");
    var beta = await service.CheckAsync(new Version(0, 1, 1), includeBeta: true);
    Assert(beta.Source == UpdateSource.GitHub && beta.Version == new Version(0, 2, 0),
        "Beta update selection is incorrect.");

    Assert(ReleaseVersion.TryParse("v0.1.2-beta.1", out var betaOne) &&
           ReleaseVersion.TryParse("v0.1.2-beta.2", out var betaTwo) &&
           ReleaseVersion.TryParse("v0.1.2", out var stableVersion) &&
           betaTwo.CompareTo(betaOne) > 0 && stableVersion.CompareTo(betaTwo) > 0,
        "Beta release version ordering is incorrect.");

    using var betaOrderClient = new HttpClient(new StubHttpHandler(_ =>
        new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""
                [{ "tag_name": "v0.1.2-beta.2", "name": "Beta 0.1.2", "draft": false, "prerelease": true,
                   "html_url": "https://github.com/Unke-cc/Win-Mouse-Fix/releases/tag/v0.1.2-beta.2" }]
                """)
        }));
    var betaOrder = await new UpdateService(betaOrderClient).CheckAsync(betaOne, includeBeta: true);
    Assert(betaOrder.Status == UpdateCheckStatus.UpdateAvailable && betaOrder.ReleaseVersion?.BetaNumber == 2,
        "A later Beta build was not treated as an update.");

    using var fallbackClient = new HttpClient(new StubHttpHandler(request =>
    {
        if (request.RequestUri?.Host == "api.github.com")
        {
            throw new HttpRequestException("GitHub unavailable");
        }

        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""
                [{ "tag_name": "v0.1.3", "name": "Gitee 0.1.3", "draft": null, "prerelease": false }]
                """)
        };
    }));
    var fallback = await new UpdateService(fallbackClient)
        .CheckAsync(new Version(0, 1, 1), includeBeta: false);
    Assert(fallback.Source == UpdateSource.Gitee && fallback.Version == new Version(0, 1, 3) &&
           fallback.PageUrl?.IndexOf("gitee.com", StringComparison.OrdinalIgnoreCase) >= 0,
        "Gitee update fallback is incorrect.");

    using var invalidGitHubClient = new HttpClient(new StubHttpHandler(request =>
        request.RequestUri?.Host == "api.github.com"
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{ invalid") }
            : new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""
                    [{ "tag_name": "v0.1.4", "name": "Gitee 0.1.4", "draft": null, "prerelease": false }]
                    """)
            }));
    var invalidGitHubFallback = await new UpdateService(invalidGitHubClient)
        .CheckAsync(new Version(0, 1, 1), includeBeta: false);
    Assert(invalidGitHubFallback.Source == UpdateSource.Gitee && invalidGitHubFallback.Version == new Version(0, 1, 4),
        "Invalid GitHub release data did not fall back to Gitee.");

    using var sourceClient = new HttpClient(new StubHttpHandler(request =>
    {
        if (request.RequestUri?.Host == "api.github.com")
        {
            throw new HttpRequestException("GitHub unavailable");
        }

        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("[]") };
    }));
    var source = await new UpdateService(sourceClient).ResolvePreferredSourceAsync();
    Assert(source.Source == UpdateSource.Gitee, "About page source selection did not fall back to Gitee.");

    using var unavailableClient = new HttpClient(new StubHttpHandler(_ => throw new HttpRequestException("Unavailable")));
    var unavailable = await new UpdateService(unavailableClient).ResolvePreferredSourceAsync();
    Assert(!unavailable.IsAvailable && unavailable.ErrorMessage?.IndexOf("GitHub", StringComparison.Ordinal) >= 0 &&
           unavailable.ErrorMessage.IndexOf("Gitee", StringComparison.Ordinal) >= 0,
        "Dual-source unavailability was not reported accurately.");
}

static void AssertThreeButtonDefaults(IEnumerable<RemapConfig> remaps)
{
    var items = remaps.ToArray();
    Assert(items.Length == 4 && items.All(item => item.Button == "MButton") &&
           items.Any(item => item.Trigger == "click" && item.Action.Type == "MiddleClick") &&
           items.Any(item => item.Trigger == "hold" && item.Action.Type == "ShowDesktop") &&
           items.Any(item => item.Trigger == "doubleClick" && item.Action.Type == "StartMenu") &&
           items.Any(item => item.Trigger == "holdDrag" && item.Action.Type == "DesktopNavigation"),
        "Three-button defaults are incorrect.");
}

static void AssertFiveOrMoreDefaults(IEnumerable<RemapConfig> remaps)
{
    var items = remaps.ToArray();
    Assert(items.Select(item => item.Button).SequenceEqual(new[]
    {
        "XButton1", "XButton1", "XButton1",
        "XButton2", "XButton2", "XButton2"
    }) && items.Any(item => item.Button == "XButton1" && item.Trigger == "click" && item.Action.Type == "Back") &&
         items.Any(item => item.Button == "XButton1" && item.Trigger == "holdScroll" && item.Action.Type == "DesktopStartMenu") &&
         items.Any(item => item.Button == "XButton1" && item.Trigger == "holdDrag" && item.Action.Type == "DesktopNavigation") &&
         items.Any(item => item.Button == "XButton2" && item.Trigger == "click" && item.Action.Type == "Forward") &&
         items.Any(item => item.Button == "XButton2" && item.Trigger == "holdScroll" && item.Action.Type == "Zoom") &&
         items.Any(item => item.Button == "XButton2" && item.Trigger == "holdDrag" && item.Action.Type == "ScrollMove"),
        "Five-or-more-button defaults are incorrect.");
}

static string ReadDirection(JsonDocument document) =>
    document.RootElement.GetProperty("desktopSwipeDirection").GetString() ?? string.Empty;

static bool HasRemap(AppConfig config, string button, string trigger, string actionType) =>
    config.Remaps.Any(item => item.Button == button && item.Trigger == trigger && item.Action.Type == actionType);

static void Assert(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

sealed class StubHttpHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> responseFactory;

    public StubHttpHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory) =>
        this.responseFactory = responseFactory;

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken) => Task.FromResult(responseFactory(request));
}
