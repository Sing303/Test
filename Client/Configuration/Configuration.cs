using Client.Configuration.Interfaces;

namespace Client.Configuration;

public sealed class Configuration : IConfiguration
{
    public string? ListeningDirectoryPath { get; set; }
    public string? ServerUrl { get; set; }
    public int BatchSize { get; set; }
}