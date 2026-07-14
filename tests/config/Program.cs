using System.Text.Json;
using System.Globalization;
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
    AssertRecommendedOrder(modelDefault.Remaps);
    CheckTriggerOrder();
    CheckNewRemapDefaults();
    CheckActionOptions();
    CheckDefaultFile();
    CheckSchema();

    await File.WriteAllTextAsync(configPath, """
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

    using (var saved = JsonDocument.Parse(await File.ReadAllTextAsync(configPath)))
    {
        Assert(!saved.RootElement.TryGetProperty("buttons", out _), "Obsolete buttons field remained after migration.");
        Assert(saved.RootElement.GetProperty("configVersion").GetInt32() == 2, "Migrated file was not rewritten as version 2.");
        Assert(ReadDirection(saved) == "followMouse", "Migrated file did not contain the default direction.");
    }

    await File.WriteAllTextAsync(configPath, """{"configVersion":2}""");
    var missingField = await service.LoadAsync();
    Assert(missingField.DesktopSwipeDirection == "followMouse", "Missing direction did not default to followMouse.");
    using (var saved = JsonDocument.Parse(await File.ReadAllTextAsync(configPath)))
    {
        Assert(ReadDirection(saved) == "followMouse", "Missing direction was not written back to disk.");
    }

    await File.WriteAllTextAsync(configPath, """{"configVersion":2,"desktopSwipeDirection":"sideways"}""");
    var invalidField = await service.LoadAsync();
    Assert(invalidField.DesktopSwipeDirection == "followMouse", "Invalid direction was not normalized.");
    using (var saved = JsonDocument.Parse(await File.ReadAllTextAsync(configPath)))
    {
        Assert(ReadDirection(saved) == "followMouse", "Normalized direction was not written back to disk.");
    }

    invalidField.DesktopSwipeDirection = "oppositeMouse";
    await service.SaveAsync(invalidField);
    var roundTrip = await service.LoadAsync();
    Assert(roundTrip.DesktopSwipeDirection == "oppositeMouse", "Direction did not survive save and reload.");
    using (var saved = JsonDocument.Parse(await File.ReadAllTextAsync(configPath)))
    {
        Assert(saved.RootElement.GetProperty("remaps").EnumerateArray()
            .All(item => !item.TryGetProperty("triggerOrder", out _)), "triggerOrder was written to configuration JSON.");
    }

    migrated.Remaps.Clear();
    migrated.RestoreRecommendedRemaps();
    Assert(migrated.Remaps.Count == 8, "Recommended settings did not restore all eight mappings.");
    AssertRecommendedOrder(migrated.Remaps);
    Assert(HasRemap(migrated, "XButton1", "holdDrag", "DesktopNavigation"), "Recommended desktop navigation mapping is missing.");

    Console.WriteLine("Configuration migration, direction, trigger ordering, schema, and recommended settings check passed.");
}
finally
{
    Directory.Delete(temporaryDirectory, recursive: true);
}

static void CheckDefaultFile()
{
    using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "default.json")));
    Assert(ReadDirection(document) == "followMouse", "Default file direction is incorrect.");
    var buttons = document.RootElement.GetProperty("remaps").EnumerateArray()
        .Select(item => item.GetProperty("button").GetString())
        .ToArray();
    Assert(buttons.SequenceEqual(new[]
    {
        "MButton", "MButton",
        "XButton1", "XButton1", "XButton1",
        "XButton2", "XButton2", "XButton2"
    }), "Default file recommended mapping order is incorrect.");
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
}

static void CheckTriggerOrder()
{
    var expected = new (string Trigger, int Order)[]
    {
        ("click", 0),
        ("hold", 1),
        ("doubleClick", 2),
        ("holdDrag", 3),
        ("holdScroll", 4),
        ("dragUp", 5),
        ("dragDown", 6),
        ("dragLeft", 7),
        ("dragRight", 8),
        ("wheelUp", 9),
        ("wheelDown", 10),
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
        ("MButton", "holdScroll", "FastScroll"),
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

static void CheckActionOptions()
{
    var converter = new TriggerActionOptionsConverter();
    foreach (var trigger in new[] { "click", "holdScroll", "holdDrag" })
    {
        var options = (ActionOption[])converter.Convert(
            trigger, typeof(ActionOption[]), null!, CultureInfo.InvariantCulture);
        Assert(options[0] == new ActionOption("Original", "执行原功能"),
            $"Original action is not first for {trigger}.");
        Assert(options[^1] == new ActionOption("None", "不执行动作"),
            $"None action is not last for {trigger}.");
    }
}

static void AssertRecommendedOrder(IEnumerable<RemapConfig> remaps)
{
    var buttons = remaps.Select(item => item.Button).ToArray();
    Assert(buttons.SequenceEqual(new[]
    {
        "MButton", "MButton",
        "XButton1", "XButton1", "XButton1",
        "XButton2", "XButton2", "XButton2"
    }), "Recommended mapping order is incorrect.");
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
