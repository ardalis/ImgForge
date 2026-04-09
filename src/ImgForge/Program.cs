using System.CommandLine;
using ImgForge.Commands;

var rootCommand = new RootCommand("ImgForge \u2014 generate images from HTML templates");
rootCommand.AddCommand(GenerateCommand.Build());
return await rootCommand.InvokeAsync(args);
