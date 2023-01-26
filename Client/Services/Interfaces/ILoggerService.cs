using Contracts;

namespace Client.Services.Interfaces;

public interface ILoggerService : IDisposable
{
    Task ProcessExistingFiles();
    void SetupFileWatcher();
    Task<TypesReply> GetAllTypesAsync();
    IAsyncEnumerable<LogItem?> GetByType(String type);
}