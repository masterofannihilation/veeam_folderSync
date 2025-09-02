using CommandLine;
using Microsoft.Extensions.Logging;
using veeam_fold_sync.CliParser;
using veeam_fold_sync.DataSync;
using veeam_fold_sync.Logger;

namespace veeam_fold_sync;

internal class Program
{
    private static async Task Main(string[] args)
    {
        await Parser.Default.ParseArguments<Options>(args)
            .WithParsedAsync(async (options) =>
            {
                if (options.Validate())
                {
                    // Setup logger
                    using var loggerFactory = FileLogger.SetupLogger(options.LogFile);
                    var logger = loggerFactory.CreateLogger<Program>();
                    PrintInfo(options, logger);
                    
                    // Initial merkle tree build and synchronization
                    var synchronizer = new Synchronizer(options, logger);
                    await synchronizer.InitialSync(options);

                    // Periodic sync
                    using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(options.SyncInterval));
                    while (await timer.WaitForNextTickAsync())
                    {
                        await synchronizer.PeriodicSync();
                    }
                }
            });
    }
    
    private static void PrintInfo(Options options, ILogger logger)
    {
        logger.Log(LogLevel.Information, "Logger initialized.");
        logger.Log(LogLevel.Information, "Source Folder: {SrcFolder}", options.SrcFolder);
        logger.Log(LogLevel.Information, "Replica Folder: {RepFolder}", options.RepFolder);
        logger.Log(LogLevel.Information, "Log File: {LogFile}", options.LogFile);
        logger.Log(LogLevel.Information, "Synchronization interval: {interval} ms", options.SyncInterval);
    }
}