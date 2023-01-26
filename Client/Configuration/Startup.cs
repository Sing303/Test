using Autofac;
using Client.Services;
using Client.Services.Interfaces;
using Contracts;
using Grpc.Core;
using Microsoft.Extensions.Configuration;
using IConfiguration = Client.Configuration.Interfaces.IConfiguration;

namespace Client.Configuration;

internal static class Startup
{
    static Startup()
    {
        Container = RegisterTypes().Build();
    }

    internal static IContainer Container { get; }

    private static ContainerBuilder RegisterTypes()
    {
        var containerBuilder = new ContainerBuilder();
        containerBuilder.Register(_ => GetConfiguration()).As<IConfiguration>().SingleInstance();
        containerBuilder.RegisterType<LoggerService>().As<ILoggerService>();
        return containerBuilder;
    }
    
    private static Configuration GetConfiguration()
    {
        var configurationBuilder = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: false);

        var config = configurationBuilder.Build();
        return config.GetSection("Configuration").Get<Configuration>();
    }
    
    /// <summary>
    /// Console menu. To make it easier to check the test assignment
    /// GOTO is not very good, but it can be done to create a menu
    /// </summary>
    internal static async Task SetupConsoleMenu(ILoggerService loggerService)
    {
        Console.WriteLine("1. Getting a list of JSON object types");
        Console.WriteLine("2. Get all loaded JSON objects by type");
        Console.WriteLine();
        
        START_MENU:
        var line = Console.ReadLine();
        if (line != "1" && line != "2")
        {
            Console.WriteLine("Unknown menu item...");
            ReturnToMenuMessage();
            goto START_MENU;
        }

        switch (line)
        {
            case "1":
            {
                var types = await loggerService.GetAllTypesAsync();
                Console.WriteLine(types.ResultCase == TypesReply.ResultOneofCase.Types_
                    ? $"Types - {string.Join(", ", types.Types_.Items)}"
                    : $"Failed to receive types, request again. Error: {types.Error.Message}");
                
                ReturnToMenuMessage();
                goto START_MENU;
            }
            case "2":
            {
                Console.WriteLine("Enter the type name: ");
                var messageType = Console.ReadLine();
                if (string.IsNullOrWhiteSpace(messageType))
                {
                    Console.WriteLine("Message type not entered");
                    ReturnToMenuMessage();
                    goto START_MENU;
                }

                Console.WriteLine("Enter the path to the existing directory where the value will be saved: ");
                var directory = Console.ReadLine();
                if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
                {
                    Console.WriteLine("The directory does not exist");
                    ReturnToMenuMessage();
                    goto START_MENU;
                }

                // Only for the menu creation to check the task, very bad!
                GET_LOGS_AGAIN:
                try
                {
                    if (!await GetLogs(messageType, directory))
                        goto START_MENU;
                }
                catch (RpcException ex) when (ex.StatusCode == StatusCode.Unavailable)
                {
                    Console.WriteLine("Server connection error, reconnecting");
                    await Task.Delay(LoggerService.RepeatErrorFileUploadDelay);
                    goto GET_LOGS_AGAIN;
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex);
                    Console.WriteLine("Uploading error. The data was not uploaded");
                }

                ReturnToMenuMessage();
                goto START_MENU;
            }
        }

        void ReturnToMenuMessage()
        {
            Console.WriteLine("Return to menu");
            Console.WriteLine();
        }
        
        async Task<bool> GetLogs(string messageType, string directory)
        {
            var logs = await loggerService.GetByType(messageType).ToListAsync();
            if (logs.All(log => log != null))
            {
                var lines = logs.Select(logItem => logItem?.Message).ToList();
                if (!lines.Any())
                {
                    Console.WriteLine($"Logs like {messageType} does not exist");
                    ReturnToMenuMessage();
                    return false;
                }

                await File.WriteAllLinesAsync($"{directory}/MessagesByType_{messageType}_{DateTime.Now.Ticks}.json", lines!);
                Console.WriteLine($"Saved to file {logs.Count} entries");
            }
            else
            {
                Console.WriteLine("Uploading error. The data was not uploaded");
            }

            return true;
        }
    }
}