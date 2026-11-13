using System.CommandLine;
using System.CommandLine.Builder;
using System.CommandLine.Help;
using System.CommandLine.Parsing;
using ImgForge.Commands;

namespace ImgForge;

/// <summary>Builds the <c>imgforge</c> command line, including the Examples sections shown by <c>--help</c>.</summary>
internal static class Cli
{
    internal const string ToolName = "imgforge";

    /// <summary>Example command lines shown at the end of <c>imgforge --help</c>.</summary>
    internal static readonly IReadOnlyList<HelpExample> RootExamples =
    [
        new("Blog / Open Graph card with a random background",
            "generate --template blog --title \"Hello!\" --bg random --format blog"),
        new("Show every generate option and more examples", "generate --help"),
    ];

    internal static Parser BuildParser()
    {
        var generate = GenerateCommand.Build();
        var root = new RootCommand("ImgForge — generate images from HTML templates") { generate };
        root.Name = ToolName;

        return new CommandLineBuilder(root)
            .UseDefaults()
            .UseHelp(help => help.HelpBuilder.CustomizeLayout(context =>
            {
                var examples = context.Command == generate ? GenerateCommand.Examples
                    : context.Command == root ? RootExamples
                    : null;
                var layout = HelpBuilder.Default.GetLayout();
                return examples is null
                    ? layout
                    : layout.Append(ctx => WriteExamples(ctx.Output, examples, ctx.Command.Subcommands.Count > 0));
            }))
            .Build();
    }

    private static void WriteExamples(TextWriter output, IReadOnlyList<HelpExample> examples, bool afterSubcommands)
    {
        // The default Commands section doesn't end with a blank line, unlike the Options section.
        if (afterSubcommands)
            output.WriteLine();

        output.WriteLine("Examples:");
        for (var i = 0; i < examples.Count; i++)
        {
            if (i > 0)
                output.WriteLine();
            output.WriteLine($"  # {examples[i].Description}");
            output.WriteLine($"  {ToolName} {examples[i].Arguments}");
        }
    }
}
