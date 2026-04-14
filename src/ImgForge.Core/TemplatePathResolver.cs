namespace ImgForge.Core;

public static class TemplatePathResolver
{
    private const string DefaultTemplatePath = ".imgforge/template.html";

    public static (string Template, bool UsedDefault) ResolveTemplate(string? template)
    {
        if (!string.IsNullOrWhiteSpace(template))
            return (template, false);

        if (File.Exists(DefaultTemplatePath))
            return (DefaultTemplatePath, true);

        throw new ArgumentException(
            $"No template was provided on the command line and no default template exists at '{DefaultTemplatePath}'.");
    }
}
