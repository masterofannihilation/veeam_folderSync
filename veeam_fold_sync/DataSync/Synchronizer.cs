using Microsoft.Extensions.Logging;
using veeam_fold_sync.CliParser;

namespace veeam_fold_sync.DataSync;

public class Synchronizer(Options options, ILogger logger)
{
    private readonly MerkleTree _sourceTree = new (options.SrcFolder);
    private readonly MerkleTree _replicaTree = new(options.RepFolder);
    private readonly Watcher _sourceWatcher = new ();
    private readonly Watcher _replicaWatcher = new ();

    public async Task InitialSync(Options options)
    {
        // Build merkle trees of source and replica folders
        var buildTasks = new List<Task>();
        
        _sourceTree.NodeLookup.Add(NormalizePath(_sourceTree.Root.Address), _sourceTree.Root);
        buildTasks.Add(_sourceTree.BuildTreeAsync(_sourceTree.Root));
    
        _replicaTree.NodeLookup.Add(NormalizePath(_replicaTree.Root.Address), _replicaTree.Root);
        buildTasks.Add(_replicaTree.BuildTreeAsync(_replicaTree.Root));
        
        await Task.WhenAll(buildTasks);

        // Console.WriteLine("\n Source tree");
        // _sourceTree.PrintTree(_sourceTree.Root);
        // Console.WriteLine("\n Replica tree");
        // _replicaTree.PrintTree(_replicaTree.Root);
        
        // Start watching source folder and replica folder for changes
        _ = Task.Run( () => _sourceWatcher.Watch(options.SrcFolder));
        _ = Task.Run( () => _replicaWatcher.Watch(options.RepFolder));
        
        // Initial folder synchronization
        await SyncTreesAsync(_sourceTree.Root, _replicaTree.Root);
    }

    public async Task PeriodicSync()
    {
        await ProcessWatcherListAsync(_sourceWatcher.DeletedAddresses, _sourceTree.RemoveNodes);
        await ProcessWatcherListAsync(_sourceWatcher.AddedAddresses, _sourceTree.AddNodes);
        await ProcessWatcherListAsync(_sourceWatcher.UpdatedAddresses, _sourceTree.UpdateNodes);

        await ProcessWatcherListAsync(_replicaWatcher.DeletedAddresses, _replicaTree.RemoveNodes);
        await ProcessWatcherListAsync(_replicaWatcher.AddedAddresses, _replicaTree.AddNodes);
        await ProcessWatcherListAsync(_replicaWatcher.UpdatedAddresses, _replicaTree.UpdateNodes);
        
        // Console.WriteLine("\n Source tree");
        // _sourceTree.PrintTree(_sourceTree.Root);
        // Console.WriteLine("\n Replica tree");
        // _replicaTree.PrintTree(_replicaTree.Root);
        
        // Sync source and replica trees
        await SyncTreesAsync(_sourceTree.Root, _replicaTree.Root);
    }

    private static async Task ProcessWatcherListAsync(List<string> addresses, Func<List<string>, Task> processFunc)
    {
        if (addresses.Count > 0)
        {
            // PrintAddresses(addresses);
            await processFunc(addresses);
            addresses.Clear();
        }
    }

    private async Task SyncTreesAsync(Node source, Node replica)
    {
        // If root hashes match, trees are identical
        if (source.Hash == replica.Hash) return;

        if (!source.IsDirectory) // File
        {
            await CopyFileAsync(source, replica.Address);

        }
        else // Directory
        {
            await SynchronizeDirectoryAsync(source, replica);
            await DeleteExtraFiles(source, replica);
        }
    }

    private async Task SynchronizeDirectoryAsync(Node source, Node replica)
    {
        var syncTasks = new List<Task>();

        foreach (var sourceChild in source.Children)
        {
            var replicaChild = replica.Children.FirstOrDefault(c =>
                Path.GetFileName(c.Address) == Path.GetFileName(sourceChild.Address));
                
            // If replica is missing the child, copy it over
            if (replicaChild == null)
            {
                var destPath = Path.Combine(replica.Address, Path.GetFileName(sourceChild.Address));
                syncTasks.Add(CopyToAsync(sourceChild, destPath));
            }
            else
            {
                // If child exists in both, recurse into them
                syncTasks.Add(SyncTreesAsync(sourceChild, replicaChild));
            }
        }

        await Task.WhenAll(syncTasks);
    }

    private async Task DeleteExtraFiles(Node source, Node replica)
    {
        // Identify extra entries based on filesystem addresses to avoid hash comparison (empty files don't influence hash)
        var sourceEntries = new HashSet<string>( Directory.GetFileSystemEntries(source.Address).Select(Path.GetFileName)!);
        var replicaEntries = Directory.GetFileSystemEntries(replica.Address);
    
        var extraEntries = replicaEntries
            .Where(entry => !sourceEntries.Contains(Path.GetFileName(entry)))
            .ToList();
    
        // Delete extra entries
        foreach (var extra in extraEntries)
        {
            if (Directory.Exists(extra))
            {
                await DeleteDirectoryAsync(_replicaTree.NodeLookup[NormalizePath(extra)]);
                logger.Log(LogLevel.Information, "Deleted directory: {DirPath}", extra);
            }
            else if (File.Exists(extra))
            {
                File.Delete(extra);
                logger.Log(LogLevel.Information, "Deleted file: {FilePath}", extra);
            }
        }
    }

    private async Task CopyToAsync(Node nodeToBeCopied, string dstAddress)
    {
        if (nodeToBeCopied.IsDirectory)
        {
            await CopyDirectoryAsync(nodeToBeCopied, dstAddress);
        }
        else
        {
            await CopyFileAsync(nodeToBeCopied, dstAddress);
        }
    }

    private async Task CopyFileAsync(Node nodeToBeCopied, string dstAddress)
    {
        // Use async file copy
        await using var sourceStream = new FileStream(nodeToBeCopied.Address, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous);
        await using var destStream = new FileStream(dstAddress, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous);

        await sourceStream.CopyToAsync(destStream);

        // Explicitly flush and close before AddNodes to prevent accessing a file by another process
        await destStream.FlushAsync();
        destStream.Close();

        logger.Log(LogLevel.Information, "Copied file: {FilePath}", nodeToBeCopied.Address + " to " + dstAddress);
    }

    private async Task CopyDirectoryAsync(Node nodeToBeCopied, string dstAddress)
    {
        // Ensure destination directory exists
        if (!Directory.Exists(dstAddress))
        {
            Directory.CreateDirectory(dstAddress);
            logger.Log(LogLevel.Information, "Created directory: {DirPath}", dstAddress);
        }
        // Recursively copy children
        foreach (var child in nodeToBeCopied.Children)
        {
            var childDestPath = Path.Combine(dstAddress, Path.GetFileName(child.Address));
            await CopyToAsync(child, childDestPath);
        }
    }

    private async Task DeleteAsync(Node nodeToBeDeleted)
    {
        if (nodeToBeDeleted.IsDirectory)
        {
            await DeleteDirectoryAsync(nodeToBeDeleted);
        }
        else
        {
            await DeleteFileAsync(nodeToBeDeleted);
        }
    }

    private Task DeleteFileAsync(Node nodeToBeDeleted)
    {
        File.Delete(nodeToBeDeleted.Address);
        logger.Log(LogLevel.Information, "Deleted file: {FilePath}", nodeToBeDeleted.Address);
        return Task.CompletedTask;
    }

    private async Task DeleteDirectoryAsync(Node nodeToBeDeleted)
    {
        // Make a copy of the children to avoid modifying the collection during iteration
        var childrenCopy = nodeToBeDeleted.Children.ToList();
        foreach (var child in childrenCopy)
        {
            await DeleteAsync(child);
        }
        Directory.Delete(nodeToBeDeleted.Address);
        logger.Log(LogLevel.Information, "Deleted directory (recursively): {DirPath}", nodeToBeDeleted.Address);
    }
    
    // Normalize path to ensure consistent representation, sometimes paths can have trailing slashes
    private static string NormalizePath(string path)
    {
        return Path.GetFullPath(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
    }
    
    private static void PrintAddresses(List<string> addresses)
    {
        foreach (var address in addresses)
        {
            Console.WriteLine("Processing: " + address);
        }
    }
}