using System.CommandLine;
using ImgForge.Core;

namespace ImgForge.Commands;

public static class GenerateCommand
{
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

        var outOpt = new Option<string>(
            name: "--out",
            description: "Output PNG file path.")
        { IsRequired = true };

        var widthOpt = new Option<int>(
            name: "--width",
            getDefaultValue: () => 1200,
            description: "Viewport width in pixels.");

        var heightOpt = new Option<int>(
            name: "--height",
            getDefaultValue: () => 630,
            description: "Viewport height in pixels.");

        var cmd = new Command("generate", "Generate an image from an HTML template.")
        {
            templateOpt,
            titleOpt,
            bgOpt,
            overlayOpt,
            outOpt,
            widthOpt,
            heightOpt
        };

        cmd.SetHandler(async (template, title, bg, overlays, out_, width, height) =>
        {
            try
            {
                var opts = new GenerateOptions(
                    Template: template,
                    Title: title,
                    Background: bg,
                    Overlays: (overlays ?? []).Select(o => new OverlayImage(o)).ToList(),
                    Out: out_,
                    Width: width,
                    Height: height
                );

                var renderer = new TemplateRenderer();
                var generator = new ImageGenerator(renderer);
                var result = await generator.GenerateAsync(opts);
                Console.WriteLine(result);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error: {ex.Message}");
                Environment.Exit(1);
            }
        },
        templateOpt, titleOpt, bgOpt, overlayOpt, outOpt, widthOpt, heightOpt);

        return cmd;
    }
}
