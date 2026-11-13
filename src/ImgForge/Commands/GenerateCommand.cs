using System.CommandLine;
using ImgForge.Core;
using ImgForge.Core.RandomBackgrounds;

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

    /// <summary>Example command lines shown at the end of <c>imgforge generate --help</c>.</summary>
    internal static readonly IReadOnlyList<HelpExample> Examples =
    [
        new("Blog / Open Graph card with a random background",
            "generate --template blog --title \"My Post Title\" --bg random --format blog"),
        new("YouTube thumbnail from a local background image, written to a specific file",
            "generate --template youtube --title \"My Video Title\" --bg ./bg.jpg --out thumb.png --format youtube"),
        new("Blog card with subtitle and headshot",
            "generate --template blog-subtitle --title \"Advanced .NET Patterns\" --subtitle \"A practical guide\" --bg random --headshot ./photo.jpg --format blog"),
        new("Solid-color background from one provider (random:<provider>, no fallback)",
            "generate --template blog --title \"My Post\" --bg random:placeholder:0f172a --format blog"),
        new("Random photo from picsum.photos only",
            "generate --template blog --title \"My Post\" --bg random:picsum --format blog"),
        new("Podcast episode art with a greyscale headshot and template variables (template file from the repository)",
            "generate --template ./templates/podcast-episode.html --title \"Build vs Buy\" --headshot ./guest.jpg --headshot-filter mono --var season=3 --var episode=42 --format podcast-episode"),
        new("Custom template folder with an overlay image and explicit size, written to a directory",
            "generate --template ./my-template --title \"Hello\" --overlay https://example.com/logo.png --width 1600 --height 900 --out-dir ./output"),
        new("Use the default template (.tools/imgforge/template.html); omit --format to be prompted for a size",
            "generate --title \"My Episode Title\""),
    ];

    /// <summary>Width used when only <c>--height</c> is given and there's no <c>--format</c>.</summary>
    private const int FallbackWidth = 1200;

    /// <summary>Height used when only <c>--width</c> is given and there's no <c>--format</c>.</summary>
    private const int FallbackHeight = 630;

    public static Command Build()
    {
        var templateOpt = new Option<string?>(
            name: "--template",
            description: "Built-in template name (blog, youtube, blog-subtitle), path to a .html file, or a directory containing template.html. " +
                         "If omitted, defaults to .tools/imgforge/template.html (or legacy .imgforge/template.html) in the current directory " +
                         "or the nearest parent directory up to the repository root.");

        var titleOpt = new Option<string>(
            name: "--title",
            description: "Main heading text injected into the template.")
        { IsRequired = true };

        var subtitleOpt = new Option<string?>(
            name: "--subtitle",
            description: "Optional subtitle text rendered below the title in templates that support it.");

        var bgOpt = new Option<string?>(
            name: "--bg",
            description: "Background image: local file path, HTTP(S) URL, or \"random\". " +
                         "\"random\" downloads a random image before rendering, trying Unsplash, Pexels, and Pixabay (each only if its UNSPLASH_ACCESS_KEY, PEXELS_API_KEY, or PIXABAY_API_KEY environment variable is set), " +
                         "then picsum.photos, then a solid-color placeholder from placehold.co. " +
                         "\"random:<provider>\" uses only that provider (no fallback): picsum, loremflickr (optionally random:loremflickr:<keyword>, e.g. random:loremflickr:technology; " +
                         "loremflickr.com has been unreliable, so it isn't in the default order), " +
                         "placeholder (optionally random:placeholder:<hex color>, e.g. random:placeholder:0f172a), " +
                         "unsplash (needs UNSPLASH_ACCESS_KEY), pexels (needs PEXELS_API_KEY), or pixabay (needs PIXABAY_API_KEY). Optional.");

        var overlayOpt = new Option<string[]>(
            name: "--overlay",
            description: "Overlay image source (HTTP(S) URL or file:/// URI), passed to the template as-is. " +
                         "A relative path resolves against the template's folder, so it only works with a file or folder template, not a built-in one. " +
                         "May be repeated for multiple overlays.")
        {
            AllowMultipleArgumentsPerToken = false,
            Arity = ArgumentArity.ZeroOrMore
        };

        var headshotOpt = new Option<string?>(
            name: "--headshot",
            description: "Guest headshot image (local path or HTTP(S) URL) to place in the template's headshot slot.");

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
                         "Choices: youtube (1280×720), blog or og (1200×630), github (1280×640), " +
                         "podcast-show (3000×3000), podcast-episode (3000×3000). " +
                         "Explicit --width/--height override the preset.");

        var outOpt = new Option<string?>(
            name: "--out",
            description: "Output PNG file path. Defaults to the title as a slug in the current directory.");

        var outDirOpt = new Option<string?>(
            name: "--out-dir",
            description: "Output directory. Filename is derived from --title. Ignored if --out is provided.");

        var widthOpt = new Option<int?>(
            name: "--width",
            description: $"Viewport width in pixels. Overrides --format. With no --format, --width, or --height, you're prompted to pick a format; " +
                         $"if only --height is given (and no --format), width is {FallbackWidth}.");

        var heightOpt = new Option<int?>(
            name: "--height",
            description: $"Viewport height in pixels. Overrides --format. With no --format, --width, or --height, you're prompted to pick a format; " +
                         $"if only --width is given (and no --format), height is {FallbackHeight}.");

        var cmd = new Command("generate", "Generate an image from an HTML template.")
        {
            templateOpt,
            titleOpt,
            subtitleOpt,
            bgOpt,
            overlayOpt,
            headshotOpt,
            headshotFilterOpt,
            varOpt,
            formatOpt,
            outOpt,
            outDirOpt,
            widthOpt,
            heightOpt
        };

        cmd.SetHandler(async (context) =>
        {
            var template = context.ParseResult.GetValueForOption(templateOpt);
            var title = context.ParseResult.GetValueForOption(titleOpt)!;
            var subtitle = context.ParseResult.GetValueForOption(subtitleOpt);
            var bg = context.ParseResult.GetValueForOption(bgOpt);
            var overlays = context.ParseResult.GetValueForOption(overlayOpt);
            var headshot = context.ParseResult.GetValueForOption(headshotOpt);
            var headshotFilter = context.ParseResult.GetValueForOption(headshotFilterOpt)!;
            var vars = context.ParseResult.GetValueForOption(varOpt);
            var format = context.ParseResult.GetValueForOption(formatOpt);
            var outDir = context.ParseResult.GetValueForOption(outDirOpt);
            var out_ = FileNameHelper.ResolveOutputPath(
                                   context.ParseResult.GetValueForOption(outOpt), outDir, title);

            // --width/--height have no default, so a value means the user supplied it.
            int? widthArg = context.ParseResult.GetValueForOption(widthOpt);
            int? heightArg = context.ParseResult.GetValueForOption(heightOpt);
            bool widthExplicit = widthArg is not null;
            bool heightExplicit = heightArg is not null;

            int width = widthArg ?? FallbackWidth;
            int height = heightArg ?? FallbackHeight;

            try
            {
                // Catch a mistyped --bg random:<provider> before resolving the template or prompting for a format.
                ValidateBackground(bg);

                var (resolvedTemplate, usedDefaultTemplate) = TemplatePathResolver.ResolveTemplate(template);
                if (usedDefaultTemplate)
                {
                    Console.WriteLine($"No --template provided; using default template at '{resolvedTemplate}'.");
                    if (TemplatePathResolver.IsLegacyTemplatePath(resolvedTemplate))
                    {
                        Console.WriteLine("Tip: '.imgforge/' is a legacy location; consider moving it to '.tools/imgforge/' (https://github.com/tools-dir/spec).");
                    }
                }

                // Resolve dimensions: explicit flags > --format > interactive prompt
                if (!widthExplicit || !heightExplicit)
                {
                    (int pw, int ph) = ResolvePresetDimensions(format)
                        ?? (widthExplicit || heightExplicit ? (width, height) : ((int, int)?)null)
                        ?? await PromptForFormatAsync();

                    if (!widthExplicit) width = pw;
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
                    Template: resolvedTemplate,
                    Title: title,
                    Subtitle: subtitle,
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

    /// <summary>Checks a <c>--bg random[:provider[:option]]</c> value. Other backgrounds aren't checked here.</summary>
    /// <exception cref="ArgumentException">The random background names an unknown provider or has an invalid option.</exception>
    internal static void ValidateBackground(string? background)
    {
        if (RandomBackgroundSpec.IsRandom(background))
            _ = RandomBackgroundSpec.Parse(background!);
    }

    internal static (int Width, int Height)? ResolvePresetDimensions(string? format) =>
        format?.ToLowerInvariant() switch
        {
            "youtube" => (1280, 720),
            "blog" or "og" => (1200, 630),
            "github" => (1280, 640),
            "podcast-show" => (3000, 3000),
            "podcast-episode" => (3000, 3000),
            null => null,
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
