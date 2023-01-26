namespace Client.Configuration.Interfaces;

public interface IConfiguration
{
    string? ListeningDirectoryPath { get; set; }
    string? ServerUrl { get; set; }
    int BatchSize { get; set; }
}