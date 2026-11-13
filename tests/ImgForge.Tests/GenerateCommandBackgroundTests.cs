using System.CommandLine;
using ImgForge.Commands;

namespace ImgForge.Tests;

/// <summary>Tests that swap the process-wide <see cref="Console"/> streams, so they must not run in parallel.</summary>
[CollectionDefinition(nameof(ConsoleCollection), DisableParallelization = true)]
public sealed class ConsoleCollection;

[Collection(nameof(ConsoleCollection))]
public class GenerateCommandBackgroundTests
{
    [Fact]
    public async Task Invoke_InvalidRandomProvider_FailsBeforeResolvingTemplateOrPromptingForFormat()
    {
        // No --format/--width/--height, so a later check would prompt first; the template doesn't exist, so a later
        // check would report that instead. "1" on stdin keeps an unwanted prompt from waiting forever.
        var (exitCode, stdout, stderr) = await InvokeAsync(
            "1" + Environment.NewLine,
            "generate", "--title", "Hello", "--template", "no-such-template.html", "--bg", "random:bogus");

        Assert.Equal(1, exitCode);
        Assert.Equal(
            "Error: Invalid --bg value \"random:bogus\": unknown provider \"bogus\". " +
            "Valid providers: picsum, loremflickr, placeholder, unsplash (for example: --bg random:picsum).",
            stderr.Trim());
        Assert.DoesNotContain("Choose a destination format", stdout);
    }

    [Fact]
    public void BgOptionHelp_DescribesProviderSyntax()
    {
        var bg = GenerateCommand.Build().Options.Single(o => o.Name == "bg");

        Assert.Contains("random:<provider>", bg.Description);
        Assert.Contains("picsum, loremflickr", bg.Description);
        Assert.Contains("placeholder", bg.Description);
    }

    private static async Task<(int ExitCode, string Stdout, string Stderr)> InvokeAsync(string stdin, params string[] args)
    {
        var (originalIn, originalOut, originalError) = (Console.In, Console.Out, Console.Error);
        using var output = new StringWriter();
        using var error = new StringWriter();
        try
        {
            Console.SetIn(new StringReader(stdin));
            Console.SetOut(output);
            Console.SetError(error);

            var root = new RootCommand { GenerateCommand.Build() };
            var exitCode = await root.InvokeAsync(args);
            return (exitCode, output.ToString(), error.ToString());
        }
        finally
        {
            Console.SetIn(originalIn);
            Console.SetOut(originalOut);
            Console.SetError(originalError);
        }
    }
}
