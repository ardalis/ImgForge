using System.CommandLine;
using System.CommandLine.Invocation;
using System.CommandLine.IO;
using ImgForge.Core.Configuration;

namespace ImgForge.Commands;

/// <summary>
/// <c>imgforge config</c>: view, change, and clear the per-user settings in <see cref="UserSettingsStore"/>.
/// </summary>
internal static class ConfigCommand
{
    /// <summary>Example command lines shown at the end of <c>imgforge config --help</c>.</summary>
    internal static readonly IReadOnlyList<HelpExample> Examples =
    [
        new("Show the settings file location and current settings", "config show"),
        new("Add a local image folder", "config add image-directories ~/Pictures/backgrounds"),
        new("Remove a template", "config remove templates ./my-template"),
        new("Clear one setting", "config clear image-directories"),
        new("Delete all settings", "config clear"),
    ];

    private static readonly string[] SettingNames = [.. UserSetting.All.Select(s => s.Name)];

    /// <param name="createStore">Creates the store when a command runs (so the environment is read then, not at startup).</param>
    public static Command Build(Func<UserSettingsStore> createStore)
    {
        var config = new Command("config",
            $"View, change, or clear your ImgForge settings, stored as JSON in {UserSettingsStore.DefaultDirectoryName}/{UserSettingsStore.FileName} " +
            $"in your user profile folder (or in the folder named by the {UserSettingsStore.DirectoryEnvironmentVariable} environment variable; " +
            $"run 'imgforge config path' to see where). Settings: " +
            string.Join("; ", UserSetting.All.Select(s => $"{s.Name} ({s.Description})")) + ".");

        var show = new Command("show", "Show the settings file location and the current settings as JSON.");
        show.SetHandler(context => Run(context, createStore, (store, console) =>
        {
            var settings = store.Load();
            if (settings.HadRemovedSettings)
            {
                console.Error.WriteLine(
                    "Note: the settings file has settings ImgForge no longer uses (such as dropboxFolders). They're ignored and " +
                    "will be removed the next time ImgForge saves the file, or run 'imgforge config clear' to delete the whole file now.");
            }
            console.Out.WriteLine(store.Exists
                ? $"Settings file: {store.FilePath}"
                : $"Settings file: {store.FilePath} (not created yet)");
            console.Out.WriteLine(UserSettingsStore.ToJson(settings));
        }));

        var path = new Command("path", "Print the settings file path (the file may not exist yet).");
        path.SetHandler(context => Run(context, createStore, (store, console) => console.Out.WriteLine(store.FilePath)));

        var addSetting = SettingArgument();
        var addValue = new Argument<string>("value", "The folder or template path to add. Paths are stored as full paths.");
        var add = new Command("add", "Add a value to a setting.") { addSetting, addValue };
        add.SetHandler(context => Run(context, createStore, (store, console) =>
        {
            var setting = UserSetting.Find(context.ParseResult.GetValueForArgument(addSetting));
            var value = setting.Normalize(context.ParseResult.GetValueForArgument(addValue));
            var settings = store.Load();
            var values = setting.GetValues(settings);

            if (values.Contains(value, setting.Comparer))
            {
                console.Out.WriteLine($"{setting.Name} already contains '{value}'.");
                return;
            }

            values.Add(value);
            store.Save(settings);
            console.Out.WriteLine($"Added '{value}' to {setting.Name}.");
            if (setting.IsPath && !File.Exists(value) && !Directory.Exists(value))
                console.Error.WriteLine($"Warning: '{value}' doesn't exist.");
        }));

        var removeSetting = SettingArgument();
        var removeValue = new Argument<string>("value", "The value to remove. Paths are matched as full paths.");
        var remove = new Command("remove", "Remove a value from a setting.") { removeSetting, removeValue };
        remove.SetHandler(context => Run(context, createStore, (store, console) =>
        {
            var setting = UserSetting.Find(context.ParseResult.GetValueForArgument(removeSetting));
            var value = context.ParseResult.GetValueForArgument(removeValue);
            var settings = store.Load();
            var values = setting.GetValues(settings);

            var removed = values.Where(v => setting.Matches(v, value)).ToList();
            if (removed.Count == 0)
            {
                console.Error.WriteLine($"Error: {setting.Name} doesn't contain '{value.Trim()}'.");
                context.ExitCode = 1;
                return;
            }

            values.RemoveAll(removed.Contains);
            store.Save(settings);
            foreach (var r in removed)
                console.Out.WriteLine($"Removed '{r}' from {setting.Name}.");
        }));

        var clearSetting = SettingArgument();
        clearSetting.Arity = ArgumentArity.ZeroOrOne;
        clearSetting.Description = "The setting to empty. Omit it to delete the settings file and all settings.";
        var clear = new Command("clear", "Delete all settings, or empty one setting.") { clearSetting };
        clear.SetHandler(context => Run(context, createStore, (store, console) =>
        {
            var name = context.ParseResult.GetValueForArgument(clearSetting);
            if (string.IsNullOrEmpty(name))
            {
                console.Out.WriteLine(store.Delete()
                    ? $"Deleted all settings ({store.FilePath})."
                    : $"No settings to clear ({store.FilePath} doesn't exist).");
                return;
            }

            var setting = UserSetting.Find(name);
            var settings = store.Load();
            var values = setting.GetValues(settings);
            if (values.Count == 0)
            {
                console.Out.WriteLine($"{setting.Name} is already empty.");
                return;
            }

            values.Clear();
            store.Save(settings);
            console.Out.WriteLine($"Cleared {setting.Name}.");
        }));

        config.AddCommand(show);
        config.AddCommand(path);
        config.AddCommand(add);
        config.AddCommand(remove);
        config.AddCommand(clear);
        return config;
    }

    private static Argument<string> SettingArgument()
    {
        var argument = new Argument<string>("setting", $"The setting to change: {string.Join(", ", SettingNames)}.") { HelpName = "setting" };
        argument.AddCompletions(SettingNames);
        argument.AddValidator(result =>
        {
            var value = result.GetValueOrDefault<string>();
            if (value is null || SettingNames.Contains(value))
                return;
            result.ErrorMessage = value == UserSetting.RemovedDropboxFoldersName
                ? UserSetting.RemovedDropboxFoldersMessage
                : result.LocalizationResources.UnrecognizedArgument(value, SettingNames);
        });
        return argument;
    }

    private static void Run(InvocationContext context, Func<UserSettingsStore> createStore, Action<UserSettingsStore, IConsole> action)
    {
        try
        {
            action(createStore(), context.Console);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidDataException or InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            context.Console.Error.WriteLine($"Error: {ex.Message}");
            context.ExitCode = 1;
        }
    }
}
