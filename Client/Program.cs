using Autofac;
using Client.Configuration;
using Client.Services.Interfaces;

var loggerService = Startup.Container.Resolve<ILoggerService>();
await loggerService.ProcessExistingFiles();
loggerService.SetupFileWatcher();
await Startup.SetupConsoleMenu(loggerService);