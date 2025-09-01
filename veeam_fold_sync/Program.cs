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

                    // Initial sync
                    await Run(options, logger);
                    // Periodic sync
                    using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(options.SyncInterval));
                    while (await timer.WaitForNextTickAsync())
                    {
                        await Run(options, logger);
                    }
                }
            });
    }

    private static async Task Run(Options options, ILogger logger)
    {
        // Build merkle trees of source and replica folders
        var buildTasks = new List<Task>();
        var sourceTree = new MerkleTree(options.SrcFolder, logger);
        buildTasks.Add(MerkleTree.BuildTreeAsync(sourceTree.Root));
    
        var replicaTree = new MerkleTree(options.RepFolder, logger);
        buildTasks.Add(MerkleTree.BuildTreeAsync(replicaTree.Root));
        
        await Task.WhenAll(buildTasks);
    
        //Synchronize folders
        await sourceTree.SyncTreesAsync(sourceTree.Root, replicaTree.Root);
    }

    private static void PrintInfo(Options options, ILogger logger)
    {
        logger.Log(LogLevel.Information, "Logger initialized.");
        logger.Log(LogLevel.Information, "Source Folder: {SrcFolder}", options.SrcFolder);
        logger.Log(LogLevel.Information, "Replica Folder: {RepFolder}", options.RepFolder);
        logger.Log(LogLevel.Information, "Log File: {LogFile}", options.LogFile);
        logger.Log(LogLevel.Information, "Synchronization interval: {interval} ms", options.SyncInterval);
    }

    private static void HandleErrors(IEnumerable<Error> errors)
    {
        foreach (var error in errors)
        {
            Console.WriteLine(error.ToString());
        }
    }
}