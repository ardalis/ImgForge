namespace ImgForge.Core;

public static class TemplatePathResolver
{
    // Per the tools-dir spec (https://github.com/tools-dir/spec), ImgForge's repository-local
    // configuration lives in .tools/imgforge/. The legacy .imgforge/ location is still supported;
    // when both exist in the same directory, .tools/imgforge/ takes precedence.
    public const string ToolsDirTemplatePath = ".tools/imgforge/template.html";
    public const string LegacyTemplatePath = ".imgforge/template.html";

    private static readonly string[] DefaultTemplatePaths = [ToolsDirTemplatePath, LegacyTemplatePath];

    public static (string Template, bool UsedDefault) ResolveTemplate(string? template)
    {
        if (!string.IsNullOrWhiteSpace(template))
            return (template, false);

        // Search the current directory first so its results keep their familiar relative form.
        foreach (var path in DefaultTemplatePaths)
        {
            if (File.Exists(path))
                return (path, true);
        }

        // Then walk up parent directories to the repository root (the nearest directory containing
        // .git), but never beyond it. Outside a repository, only the current directory is searched.
        var current = new DirectoryInfo(Directory.GetCurrentDirectory());
        var repositoryRoot = FindRepositoryRoot(current);
        if (repositoryRoot is not null)
        {
            for (var dir = current; dir.FullName != repositoryRoot.FullName;)
            {
                dir = dir.Parent!;
                foreach (var path in DefaultTemplatePaths)
                {
                    var fullPath = Path.Combine(dir.FullName, path);
                    if (File.Exists(fullPath))
                        return (Path.GetFullPath(fullPath), true);
                }
            }
        }

        throw new ArgumentException(
            $"No template was provided on the command line and no default template exists at '{ToolsDirTemplatePath}' or '{LegacyTemplatePath}' " +
            "in the current directory or any parent directory up to the repository root.");
    }

    /// <summary>
    /// Returns true when <paramref name="path"/> points at a template in the legacy <c>.imgforge/</c> location.
    /// </summary>
    public static bool IsLegacyTemplatePath(string path)
    {
        var normalized = path.Replace('\\', '/');
        return normalized == LegacyTemplatePath || normalized.EndsWith("/" + LegacyTemplatePath, StringComparison.Ordinal);
    }

    private static DirectoryInfo? FindRepositoryRoot(DirectoryInfo start)
    {
        for (var dir = start; dir is not null; dir = dir.Parent)
        {
            // .git is a directory in a normal clone and a file in worktrees and submodules.
            var gitPath = Path.Combine(dir.FullName, ".git");
            if (Directory.Exists(gitPath) || File.Exists(gitPath))
                return dir;
        }
        return null;
    }
}
