using System.Text;
using Contracts;
using Grpc.Core;

namespace Server.Services;

public sealed class LoggerService : Logger.LoggerBase
{
    public LoggerService(ILogger<LoggerService> logger)
    {
        _logger = logger;
    }
    
    private const string LogsRootDirectory = "/root";
    private readonly ILogger<LoggerService> _logger;
    
    public override Task<ResultReply> SendLogs(LogItems logItems, ServerCallContext context)
    {
        try
        {
            var directoryDateFormat = DateTime.Now.ToString("yyyy-MM-dd");
            var filesMessages = GetFilesMessages(logItems.Items);
            foreach (var fileMessage in filesMessages)
            {
                var directoryPath = CreateAndGetDirectoryPath(fileMessage.Key, directoryDateFormat);
                var fileFullPath = GetFilePath(directoryPath);
                File.WriteAllText(fileFullPath, fileMessage.Value.ToString());
            }
            
            return Task.FromResult(new ResultReply { Success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SendLogs error");
            return Task.FromResult(new ResultReply { Success = false });
        }

        IDictionary<string, StringBuilder> GetFilesMessages(IEnumerable<LogItem> logItemList)
        {
            var filesMessages = new Dictionary<string, StringBuilder>();
            foreach (var logItem in logItemList)
            {
                if (!filesMessages.ContainsKey(logItem.MessageType))
                {
                    filesMessages.Add(logItem.MessageType, new StringBuilder());
                }

                filesMessages[logItem.MessageType].AppendLine(logItem.Message);
            }

            return filesMessages;
        }
        
        string CreateAndGetDirectoryPath(string messageType, string directoryDateFormat)
        {
            var directoryPath = @$"{LogsRootDirectory}/{messageType}/{directoryDateFormat}";
            if (!Directory.Exists(directoryPath))
            {
                Directory.CreateDirectory(directoryPath);
            }
            
            return directoryPath;
        }
            
        string GetFilePath(string directoryPath)
        {
            return $"{directoryPath}/batch_{DateTime.Now.Ticks}.json";
        }
    }

    public override Task<TypesReply> GetAllTypes(GetAllTypesRequest request, ServerCallContext context)
    {
        try
        {
            var typesDirectories = Directory.GetDirectories(LogsRootDirectory).Select(path => new DirectoryInfo(path).Name);
            var typesReply = new TypesReply { Types_ = new Types() };
            typesReply.Types_.Items.AddRange(typesDirectories);
            return Task.FromResult(typesReply);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GetAllTypes error");
            return Task.FromResult(new TypesReply
            {
                Error = new Error
                {
                    Message = ex.Message
                }
            });
        }
    }
    
    public override async Task GetByType(TypeRequest request, IServerStreamWriter<LogItemsReply> responseStream, ServerCallContext context)
    {
        try
        {
            var typeDirectory = $"{LogsRootDirectory}/{request.Type}";
            if (!Directory.Exists(typeDirectory))
            {
                return;
            }
            
            var typeFiles = Directory.GetFiles(typeDirectory, "*.json", SearchOption.AllDirectories);
            foreach (var typeFile in typeFiles)
            {
                var logItems = new LogItemsReply() { LogItems = new LogItems() };
                var readAllLines = await File.ReadAllLinesAsync(typeFile);
                var logItemList = readAllLines.Select(line => new LogItem
                {
                    MessageType = request.Type,
                    Message = line
                });
                
                logItems.LogItems.Items.AddRange(logItemList);
                await responseStream.WriteAsync(logItems);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GetByType error");
            await responseStream.WriteAsync(new LogItemsReply
            {
                Error = new Error
                {
                    Message = ex.Message
                }
            });
        }
    }
}