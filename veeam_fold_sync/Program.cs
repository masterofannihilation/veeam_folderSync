using CommandLine;
using Microsoft.Extensions.Logging;
using veeam_fold_sync.CliParser;
using veeam_fold_sync.DataSync;
using veeam_fold_sync.Logger;

namespace veeam_fold_sync;

internal class Program
{
    private static void Main(string[] args)
    {
        Parser.Default.ParseArguments<Options>(args)
            .WithParsed(options =>
            {
                if (options.Validate())
                {
                    Run(options);
                }
            })
            .WithNotParsed(HandleErrors);
    }

    private static void Run(Options options)
    {
        // Setup logger
        using var loggerFactory = FileLogger.SetupLogger(options.LogFile);
        var logger = loggerFactory.CreateLogger<Program>();
        logger.Log(LogLevel.Information, "Logger initialized.");
        logger.Log(LogLevel.Information, "Source Folder: {SrcFolder}", options.SrcFolder);
        logger.Log(LogLevel.Information, "Replica Folder: {RepFolder}", options.RepFolder);
        logger.Log(LogLevel.Information, "Log File: {LogFile}", options.LogFile);
        logger.Log(LogLevel.Information, "Synchronization interval: {interval}", options.SyncInterval);
        
        // Build Merkle tree of source folder
        var sourceTree = new MerkleTree(options.SrcFolder, logger);
        MerkleTree.BuildTree(sourceTree.Root);
        Console.WriteLine(); MerkleTree.PrintTree(sourceTree.Root); Console.WriteLine();
        
        // Build Merkle tree of replica folder
        var replicaTree = new MerkleTree(options.RepFolder, logger);
        MerkleTree.BuildTree(replicaTree.Root);
        Console.WriteLine(); MerkleTree.PrintTree(replicaTree.Root);
        
        // Compare root hashes to determine if synchronization is needed
        if (sourceTree.Root.Hash == replicaTree.Root.Hash)
        {
            logger.Log(LogLevel.Information, "Folders are identical, no synchronization needed.");
        }
        else
        {
            logger.Log(LogLevel.Information, "Folders differ. Synchronization needed.");
            sourceTree.SyncTrees(sourceTree.Root, replicaTree.Root);
        }
    }
    
    private static void HandleErrors(IEnumerable<Error> errors)
    {
        foreach (var error in errors)
        {
            Console.WriteLine(error.ToString());
        }
    }
}