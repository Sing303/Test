using System.Text;
using Client.Configuration.Interfaces;
using Client.DTO;
using Client.Services.Interfaces;
using Contracts;
using Grpc.Core;
using Grpc.Net.Client;
using Newtonsoft.Json;

namespace Client.Services;

public sealed class LoggerService : ILoggerService
{
    public LoggerService(IConfiguration configuration)
    {
        _configuration = configuration;
        _channel = GrpcChannel.ForAddress(_configuration.ServerUrl ?? "http://localhost:5275");
        _client = new Logger.LoggerClient(_channel);
        _listeningDirectoryPath = _configuration.ListeningDirectoryPath ?? DefaultListeningDirectoryPath;
    }

    public const int RepeatErrorFileUploadDelay = 1000;
    private const string DefaultListeningDirectoryPath = "C:\\_";
    
    private readonly string _listeningDirectoryPath;
    private readonly IConfiguration _configuration;
    private readonly GrpcChannel? _channel;
    private readonly Logger.LoggerClient _client;

    public async Task ProcessExistingFiles()
    {
        if (!Directory.Exists(_listeningDirectoryPath))
        {
            Directory.CreateDirectory(_listeningDirectoryPath);
        }
        
        foreach (var file in Directory.GetFiles(_listeningDirectoryPath, "*.json"))
        {
            await NewFileUploadHandler(file);
        }
    }

    public void SetupFileWatcher()
    {
        Console.WriteLine("Files watcher enabled");
        if (!Directory.Exists(_listeningDirectoryPath))
        {
            Directory.CreateDirectory(_listeningDirectoryPath);
        }
        
        var watcher = new FileSystemWatcher(_listeningDirectoryPath);
        watcher.Filter = "*.json";
        watcher.Created += NewFileHandler;
        watcher.Renamed += NewFileHandler;
        watcher.EnableRaisingEvents = true;
    }
    
    private void NewFileHandler(object sender, FileSystemEventArgs eventArgs)
    {
        NewFileUploadHandler(eventArgs.FullPath).GetAwaiter().GetResult();
    }
    
    private async Task NewFileUploadHandler(string fileFullPath)
    {
        try
        {
            Console.WriteLine($"Processing file {fileFullPath}");
        
            var position = 0;
            const int bufferSize = 1024;
            var logItems = new LogItems();
            var wasError = false;
        
            await using (var fileStream = File.Open(fileFullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                using (var streamReader = new StreamReader(fileStream, Encoding.UTF8, true, bufferSize))
                {
                    while (await streamReader.ReadLineAsync() is { } line)
                    {
                        position++;
                    
                        var logMessage = JsonConvert.DeserializeObject<LogMessage>(line);
                        if (logMessage == null)
                        {
                            wasError = true;
                            break;
                        }
                        
                        logItems.Items.Add(new LogItem
                        {
                            MessageType = logMessage.Type,
                            Message = line
                        });
                        
                        if (position % _configuration.BatchSize == 0)
                        {
                            var sendLogsResult = await _client.SendLogsAsync(logItems);
                            logItems.Items.Clear();
                            if (!sendLogsResult.Success)
                            {
                                wasError = true;
                                break;
                            }
                        }
                    }
                }
            }

            await FinishProcessingFile(wasError, position);
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.Unavailable)
        {
            Console.WriteLine("Server connection error, reconnecting");
            await Task.Delay(RepeatErrorFileUploadDelay);
            await NewFileUploadHandler(fileFullPath);
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex);
        }
        
        async Task FinishProcessingFile(bool wasError, int position)
        {
            if (wasError)
            {
                var allLines = await File.ReadAllLinesAsync(fileFullPath);
                var batchPart = position % _configuration.BatchSize;
                var succecedLines = position - batchPart;
                if (succecedLines > 0)
                {
                    if (batchPart == 0)
                    {
                        succecedLines -= _configuration.BatchSize;
                    }

                    await File.WriteAllTextAsync(fileFullPath, string.Empty);
                    await File.WriteAllLinesAsync(fileFullPath, allLines.Skip(succecedLines));
                }

                Console.WriteLine($"File processing error {fileFullPath}. The file was not fully downloaded");
                await Task.Delay(RepeatErrorFileUploadDelay);
                await NewFileUploadHandler(fileFullPath);
            }
            else
            {
                File.Delete(fileFullPath);
                Console.WriteLine($"Processing of file {fileFullPath} is complete");
            }
        }
    }
    
    public async Task<TypesReply> GetAllTypesAsync()
    {
        try
        {
            return await _client.GetAllTypesAsync(new GetAllTypesRequest());
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.Unavailable)
        {
            Console.WriteLine("Server connection error, reconnecting");
            await Task.Delay(RepeatErrorFileUploadDelay);
            return await GetAllTypesAsync();
        }
        catch (Exception ex)
        {
            return new TypesReply()
            {
                Error = new Error
                {
                    Message = ex.Message
                }
            };
        }
    }
    
    public async IAsyncEnumerable<LogItem?> GetByType(string type)
    {
        using var logItemsStream = _client.GetByType(new TypeRequest { Type = type });
        while (await logItemsStream.ResponseStream.MoveNext(default))
        {
            var message = logItemsStream.ResponseStream.Current;
            if (message.ResultCase == LogItemsReply.ResultOneofCase.Error)
            {
                yield return null;
                yield break;
            }

            foreach (var logItem in message.LogItems.Items)
            {
                yield return logItem;
            }
        }
    }

    public void Dispose()
    {
        _channel?.Dispose();
    }
}