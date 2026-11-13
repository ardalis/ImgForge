namespace ImgForge.Commands;

/// <summary>An example shown in the Examples section of <c>--help</c>.</summary>
/// <param name="Description">What the example does.</param>
/// <param name="Arguments">The arguments after <c>imgforge</c>, e.g. <c>generate --title "Hi"</c>.</param>
internal sealed record HelpExample(string Description, string Arguments);
