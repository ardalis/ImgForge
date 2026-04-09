using System.CommandLine;
using ImgForge.Core;

namespace ImgForge.Commands;

public static class GenerateCommand
{
    private record FormatPreset(string Label, int Width, int Height);

    private static readonly IReadOnlyList<FormatPreset> Formats =
    [
        new("YouTube Video Thumbnail",  1280, 720),
        new("Blog / Open Graph (og:image)", 1200, 630),
        new("GitHub Social Preview",    1280, 640),
        new("Podcast Show Art",         3000, 3000),
        new("Podcast Episode Art",      3000, 3000),
    ];

    public static Command Build()
    {
        var templateOpt = new Option<string>(
            name: "--template",
            description: "Built-in template name (blog, youtube) or path to a .html file.")
        { IsRequired = true };

        var titleOpt = new Option<string>(
            name: "--title",
            description: "Main heading text injected into the template.")
        { IsRequired = true };

        var bgOpt = new Option<string?>(
            name: "--bg",
            description: "Background image: local file path or HTTP(S) URL. Optional.");

        var overlayOpt = new Option<string[]>(
            name: "--overlay",
            description: "Overlay image path. May be repeated for multiple overlays.")
        {
            AllowMultipleArgumentsPerToken = false,
            Arity = ArgumentArity.ZeroOrMore
        };

        var headshotOpt = new Option<string?>(
            name: "--headshot",
            description: "Guest headshot image path to place in the template's headshot slot.");

        var headshotFilterOpt = new Option<string>(
            name: "--headshot-filter",
            getDefaultValue: () => "blue-mono",
            description: "Filter applied to the headshot. Built-in: blue-mono, mono, none. Or supply a raw CSS filter string.");

        var varOpt = new Option<string[]>(
            name: "--var",
            description: "Arbitrary template variable as key=value (e.g. --var episode=42 --var season=3). Accessible in templates as {{ vars.episode }}.")
        {
            AllowMultipleArgumentsPerToken = false,
            Arity = ArgumentArity.ZeroOrMore
        };

        var formatOpt = new Option<string?>(
            name: "--format",
            description: "Output format preset that sets width and height. " +
                         "Choices: youtube (1280×720), blog (1200×630), github (1280×640), " +
                         "podcast-show (3000×3000), podcast-episode (3000×3000). " +
                         "Explicit --width/--height override the preset.");

        var outOpt = new Option<string>(
            name: "--out",
            description: "Output PNG file path.")
        { IsRequired = true };

        var widthOpt = new Option<int>(
            name: "--width",
            getDefaultValue: () => 1200,
            description: "Viewport width in pixels. Overrides --format.");

        var heightOpt = new Option<int>(
            name: "--height",
            getDefaultValue: () => 630,
            description: "Viewport height in pixels. Overrides --format.");

        var cmd = new Command("generate", "Generate an image from an HTML template.")
        {
            templateOpt,
            titleOpt,
            bgOpt,
            overlayOpt,
            headshotOpt,
            headshotFilterOpt,
            varOpt,
            formatOpt,
            outOpt,
            widthOpt,
            heightOpt
        };

        cmd.SetHandler(async (context) =>
        {
            var template       = context.ParseResult.GetValueForOption(templateOpt)!;
            var title          = context.ParseResult.GetValueForOption(titleOpt)!;
            var bg             = context.ParseResult.GetValueForOption(bgOpt);
            var overlays       = context.ParseResult.GetValueForOption(overlayOpt);
            var headshot       = context.ParseResult.GetValueForOption(headshotOpt);
            var headshotFilter = context.ParseResult.GetValueForOption(headshotFilterOpt)!;
            var vars           = context.ParseResult.GetValueForOption(varOpt);
            var format         = context.ParseResult.GetValueForOption(formatOpt);
            var out_           = context.ParseResult.GetValueForOption(outOpt)!;

            // Detect whether --width/--height were explicitly supplied by the user
            // (IsImplicit = true means the value came from getDefaultValue, not the command line)
            bool widthExplicit  = context.ParseResult.FindResultFor(widthOpt)  is { IsImplicit: false };
            bool heightExplicit = context.ParseResult.FindResultFor(heightOpt) is { IsImplicit: false };

            int width  = context.ParseResult.GetValueForOption(widthOpt);
            int height = context.ParseResult.GetValueForOption(heightOpt);

            try
            {
                // Resolve dimensions: explicit flags > --format > interactive prompt
                if (!widthExplicit || !heightExplicit)
                {
                    (int pw, int ph) = ResolvePresetDimensions(format)
                        ?? (widthExplicit || heightExplicit ? (width, height) : ((int, int)?)null)
                        ?? await PromptForFormatAsync();

                    if (!widthExplicit)  width  = pw;
                    if (!heightExplicit) height = ph;
                }

                var headshotOptions = headshot is not null
                    ? new HeadshotOptions(headshot, headshotFilter)
                    : null;

                var varDict = (vars ?? [])
                    .Select(v => v.Split('=', 2))
                    .Where(parts => parts.Length == 2)
                    .ToDictionary(parts => parts[0].Trim(), parts => parts[1]);

                var opts = new GenerateOptions(
                    Template: template,
                    Title: title,
                    Background: bg,
                    Overlays: (overlays ?? []).Select(o => new OverlayImage(o)).ToList(),
                    Out: out_,
                    Width: width,
                    Height: height,
                    Headshot: headshotOptions,
                    Vars: varDict
                );

                var renderer = new TemplateRenderer();
                var generator = new ImageGenerator(renderer);
                var result = await generator.GenerateAsync(opts);
                Console.WriteLine(result);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error: {ex.Message}");
                context.ExitCode = 1;
            }
        });

        return cmd;
    }

    private static (int Width, int Height)? ResolvePresetDimensions(string? format) =>
        format?.ToLowerInvariant() switch
        {
            "youtube"          => (1280, 720),
            "blog" or "og"     => (1200, 630),
            "github"           => (1280, 640),
            "podcast-show"     => (3000, 3000),
            "podcast-episode"  => (3000, 3000),
            null               => null,
            _ => throw new ArgumentException(
                $"Unknown format '{format}'. Valid choices: youtube, blog, github, podcast-show, podcast-episode.")
        };

    private static async Task<(int Width, int Height)> PromptForFormatAsync()
    {
        Console.WriteLine();
        Console.WriteLine("No output dimensions specified. Choose a destination format:");
        Console.WriteLine();

        for (int i = 0; i < Formats.Count; i++)
        {
            var f = Formats[i];
            Console.WriteLine($"  [{i + 1}] {f.Label,-35} {f.Width}×{f.Height}");
        }

        Console.WriteLine();

        while (true)
        {
            Console.Write($"Enter a number (1-{Formats.Count}): ");
            var line = await Task.Run(() => Console.ReadLine());
            if (int.TryParse(line?.Trim(), out int choice) &&
                choice >= 1 && choice <= Formats.Count)
            {
                var selected = Formats[choice - 1];
                Console.WriteLine($"Using {selected.Label} ({selected.Width}×{selected.Height})");
                Console.WriteLine();
                return (selected.Width, selected.Height);
            }
            Console.WriteLine($"  Please enter a number between 1 and {Formats.Count}.");
        }
    }
}
