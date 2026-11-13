using System.CommandLine;
using System.CommandLine.IO;
using System.CommandLine.Parsing;
using ImgForge.Commands;
using ImgForge.Core;

namespace ImgForge.Tests;

public class CliHelpTests
{
    public static TheoryData<string> GenerateExampleArguments() =>
        new(GenerateCommand.Examples.Select(e => e.Arguments));

    [Fact]
    public async Task GenerateHelp_EndsWithEveryExample()
    {
        var help = await HelpAsync("generate", "--help");

        var examplesAt = help.IndexOf("Examples:", StringComparison.Ordinal);
        Assert.True(examplesAt > help.IndexOf("Options:", StringComparison.Ordinal), help);
        foreach (var example in GenerateCommand.Examples)
        {
            Assert.Contains($"  # {example.Description}{Environment.NewLine}  imgforge {example.Arguments}", help);
            Assert.True(help.IndexOf(example.Arguments, StringComparison.Ordinal) > examplesAt);
        }
    }

    [Fact]
    public async Task GenerateHelp_DescribesEveryOption()
    {
        var help = await HelpAsync("generate", "--help");

        foreach (var option in GenerateCommand.Build().Options)
        {
            Assert.False(string.IsNullOrWhiteSpace(option.Description), $"--{option.Name} has no description");
            Assert.Contains($"  --{option.Name} <{option.Name}>", help);
            Assert.Contains(option.Description!, help);
        }
    }

    [Theory]
    [MemberData(nameof(GenerateExampleArguments))]
    public void GenerateExample_ParsesWithoutErrors(string arguments)
    {
        var result = Cli.BuildParser().Parse(arguments);

        Assert.Empty(result.Errors);
        Assert.Equal("generate", result.CommandResult.Command.Name);
    }

    [Theory]
    [MemberData(nameof(GenerateExampleArguments))]
    public void GenerateExample_UsesValidFormatBackgroundAndBuiltInTemplate(string arguments)
    {
        var generate = Cli.BuildParser().Parse(arguments).CommandResult;
        string? Value(string name) =>
            generate.Children.OfType<OptionResult>().SingleOrDefault(o => o.Option.Name == name)?.Tokens.SingleOrDefault()?.Value;

        // Each throws on an unknown format, a bad random:<provider>, or a built-in template name that doesn't exist.
        _ = GenerateCommand.ResolvePresetDimensions(Value("format"));
        GenerateCommand.ValidateBackground(Value("bg"));
        if (Value("template") is { } template && template.IndexOfAny(['/', '\\']) < 0)
            TemplateRenderer.EnsureTemplateExists(template);
    }

    [Fact]
    public void GenerateExamples_UseEveryOption()
    {
        var used = GenerateCommand.Examples
            .SelectMany(e => CommandLineStringSplitter.Instance.Split(e.Arguments))
            .ToHashSet();

        var unused = GenerateCommand.Build().Options
            .Select(o => $"--{o.Name}")
            .Where(alias => !used.Contains(alias))
            .ToList();

        Assert.Empty(unused);
    }

    [Fact]
    public async Task RootHelp_ShowsToolNameAndPointsToGenerateHelp()
    {
        var help = await HelpAsync("--help");

        Assert.Contains("imgforge [command] [options]", help);
        Assert.Contains($"generate  Generate an image from an HTML template.{Environment.NewLine}{Environment.NewLine}Examples:", help);
        foreach (var example in Cli.RootExamples)
            Assert.Contains($"  # {example.Description}{Environment.NewLine}  imgforge {example.Arguments}", help);
        Assert.Contains("imgforge generate --help", help);
    }

    [Fact]
    public void RootExamples_ParseWithoutErrors()
    {
        foreach (var example in Cli.RootExamples)
            Assert.Empty(Cli.BuildParser().Parse(example.Arguments).Errors);
    }

    [Fact]
    public async Task Invoke_MissingRequiredTitle_FailsWithParseError()
    {
        var console = new TestConsole();

        var exitCode = await Cli.BuildParser().InvokeAsync(["generate", "--format", "blog"], console);

        Assert.Equal(1, exitCode);
        Assert.Contains("Option '--title' is required.", console.Error.ToString());
    }

    [Fact]
    public async Task Invoke_Version_Succeeds()
    {
        var console = new TestConsole();

        var exitCode = await Cli.BuildParser().InvokeAsync(["--version"], console);

        Assert.Equal(0, exitCode);
        Assert.False(string.IsNullOrWhiteSpace(console.Out.ToString()));
    }

    private static async Task<string> HelpAsync(params string[] args)
    {
        var console = new TestConsole();
        var exitCode = await Cli.BuildParser().InvokeAsync(args, console);

        Assert.Equal(0, exitCode);
        return console.Out.ToString()!;
    }
}
