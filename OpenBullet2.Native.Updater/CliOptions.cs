using CommandLine;

namespace OpenBullet2.Native.Updater;

public class CliOptions
{
    [Option('r', "repository", Required = false, HelpText = "The repository, e.g. ocim128/OB2-Edited")]
    public string Repository { get; set; } = "ocim128/OB2-Edited";
    
    [Option('u', "username", Required = false, HelpText = "The username to authenticate to the repository if private")]
    public string? Username { get; set; }
    
    [Option('t', "token", Required = false, HelpText = "The token to authenticate to the repository if private")]
    public string? Token { get; set; }
    
    [Option('c', "channel", Required = false, HelpText = "The channel to use for updates (staging, release)")]
    public BuildChannel? Channel { get; set; } = null;
}
