namespace ImgForge.Core;

public static class TemplatePathResolver
{
    // Per the tools-dir spec (https://github.com/tools-dir/spec), ImgForge's repository-local
    // configuration lives in .tools/imgforge/. The legacy .imgforge/ location is still supported;
    // when both exist, .tools/imgforge/ takes precedence.
    public const string ToolsDirTemplatePath = ".tools/imgforge/template.html";
    public const string LegacyTemplatePath = ".imgforge/template.html";

    private static readonly string[] DefaultTemplatePaths = [ToolsDirTemplatePath, LegacyTemplatePath];

    public static (string Template, bool UsedDefault) ResolveTemplate(string? template)
    {
        if (!string.IsNullOrWhiteSpace(template))
            return (template, false);

        foreach (var path in DefaultTemplatePaths)
        {
            if (File.Exists(path))
                return (path, true);
        }

        throw new ArgumentException(
            $"No template was provided on the command line and no default template exists at '{ToolsDirTemplatePath}' or '{LegacyTemplatePath}'.");
    }
}
