#!/usr/bin/env dotnet
// Run this with:
// dotnet ./BuildTestFormat.cs

using System.Diagnostics;

static int Run(string file, string args)
{
  using var process = Process.Start(
    new ProcessStartInfo(file, args) { UseShellExecute = false })!;
  process.WaitForExit();
  return process.ExitCode;
}

var buildExitCode = Run("dotnet", "build");
if (buildExitCode != 0) return buildExitCode;

var exitCode = Run("dotnet", "test --no-build");
if (exitCode != 0) return exitCode;

return Run("dotnet", "format --verify-no-changes");
