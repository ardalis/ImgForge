#!/usr/bin/env dotnet
#:property PublishAot=false
using System.Diagnostics;
using System.Net.Http;

const string GeekdocVersion = "v2.0.0";
const string ThemeDir = "themes/hugo-geekdoc";
const string TarballUrl = $"https://github.com/thegeeklab/hugo-geekdoc/releases/download/{GeekdocVersion}/hugo-geekdoc.tar.gz";

// Auto-download Geekdoc theme if not already present
if (!Directory.Exists(ThemeDir))
{
  Console.WriteLine($"Downloading Geekdoc theme {GeekdocVersion}...");
  Directory.CreateDirectory(ThemeDir);

  var tarball = "themes/hugo-geekdoc.tar.gz";
  using var http = new HttpClient();
  var bytes = await http.GetByteArrayAsync(TarballUrl);
  await File.WriteAllBytesAsync(tarball, bytes);

  RunCommand("tar", $"-xzf {tarball} -C {ThemeDir} --strip-components=1");
  File.Delete(tarball);
  Console.WriteLine("Theme downloaded.");
}

Console.WriteLine("Starting ImgForge Hugo site...");
Console.WriteLine("Site will be available at http://localhost:1315");

// Start Hugo server (run from the docs directory)
return RunCommand("hugo", "server --disableFastRender --noHTTPCache --port 1315");

int RunCommand(string command, string arguments)
{
    var process = new Process
    {
        StartInfo = new ProcessStartInfo
        {
            FileName = command,
            Arguments = arguments,
            UseShellExecute = false
        }
    };

    process.Start();
    process.WaitForExit();
    return process.ExitCode;
}
