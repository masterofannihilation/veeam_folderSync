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
        buildTasks.Add(_sourceTree.BuildTreeAsync(_sourceTree.Root));
        buildTasks.Add(_replicaTree.BuildTreeAsync(_replicaTree.Root));
        
        await Task.WhenAll(buildTasks);

        DebugPrint();
        
        // Start watching source folder and replica folder for changes
        _ = Task.Run( () => _sourceWatcher.Watch(options.SrcFolder));
        _ = Task.Run( () => _replicaWatcher.Watch(options.RepFolder));
        
        // Initial folder synchronization
        await SyncTreesAsync(_sourceTree.Root, _replicaTree.Root);
    }

    public async Task PeriodicSync()
    {
        // Update source tree and replica tree based on watcher changes
        var updateTasks = new List<Task>();
        updateTasks.Add(ProcessWatcherListAsync(_sourceWatcher.DeletedAddresses, _sourceTree.RemoveNode));
        updateTasks.Add(ProcessWatcherListAsync(_sourceWatcher.AddedAddresses, _sourceTree.AddNode));
        updateTasks.Add(ProcessWatcherListAsync(_sourceWatcher.UpdatedAddresses, _sourceTree.UpdateNode));
        
        updateTasks.Add(ProcessWatcherListAsync(_replicaWatcher.DeletedAddresses, _replicaTree.RemoveNode));
        updateTasks.Add(ProcessWatcherListAsync(_replicaWatcher.AddedAddresses, _replicaTree.AddNode));
        updateTasks.Add(ProcessWatcherListAsync(_replicaWatcher.UpdatedAddresses, _replicaTree.UpdateNode));
        
        await Task.WhenAll(updateTasks);
        
        // Sync source and replica trees
        await SyncTreesAsync(_sourceTree.Root, _replicaTree.Root);
        
        DebugPrint();
    }

    private static async Task ProcessWatcherListAsync(List<string> addresses, Func<string, Task> processFunc)
    {
        if (addresses.Count > 0)
        {
            // PrintAddresses(addresses);
            foreach (var addr in addresses)
            {
                await processFunc(addr);
                
            }
            addresses.Clear();
        }
    }

    private async Task SyncTreesAsync(Node source, Node replica)
    {
        // If root hashes match, trees are identical
        if (source.Hash == replica.Hash) return;

        if (!source.IsDirectory) // File
        {
            await CopyAsync(source, replica.Address);
        }
        else // Directory
        {
            await SyncDirAsync(source, replica);
            // After syncing, delete any extra files in replica that are not in source
            await DeleteExtraFiles(source, replica);
        }
    }
    
    private async Task CopyAsync(Node nodeToBeCopied, string dstAddress)
    {
        if (nodeToBeCopied.IsDirectory)
        {
            await CopyDirectoryAsync(nodeToBeCopied, dstAddress);
        }
        else
        {
            await CopyFileAsync(nodeToBeCopied.Address, dstAddress);
        }
    }

    private async Task CopyFileAsync(string srcAddr, string dstAddress)
    {
        // Use file streams for efficient copying
        await using var sourceStream = new FileStream(srcAddr, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous);
        await using var destStream = new FileStream(dstAddress, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous);
        await sourceStream.CopyToAsync(destStream);
        // Explicitly flush and close before AddNodes to prevent accessing a file by another process
        await destStream.FlushAsync();
        destStream.Close();
        
        // Add new node to replica tree
        await _replicaTree.AddNode(dstAddress);
        
        logger.Log(LogLevel.Information, "Copied file: {FilePath}", srcAddr + " to " + dstAddress);
    }

    private async Task CopyDirectoryAsync(Node nodeToBeCopied, string dstAddress)
    {
        // Create directory
        Directory.CreateDirectory(dstAddress);
        await _replicaTree.AddNode(dstAddress);
        logger.Log(LogLevel.Information, "Created directory: {DirPath}", dstAddress);
        
        // Copy children
        foreach (var child in nodeToBeCopied.Children)
        {
            var childDestPath = Path.Combine(dstAddress, Path.GetFileName(child.Address));
            await CopyAsync(child, childDestPath);
        }
    }

    private async Task SyncDirAsync(Node source, Node replica)
    {
        var syncTasks = new List<Task>();

        foreach (var sourceChild in source.Children)
        {
            // Find matching child in replica by comparing filesystem addresses
            var replicaChild = replica.Children.FirstOrDefault(c =>
                Path.GetFileName(c.Address) == Path.GetFileName(sourceChild.Address));
                
            // If replica is missing the child, copy it over
            if (replicaChild == null)
            {
                var destPath = Path.Combine(replica.Address, Path.GetFileName(sourceChild.Address));
                syncTasks.Add(CopyAsync(sourceChild, destPath));
            }
            else
            {
                // If child exists, recursively sync
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
    
        // Find entries in replica that are not in source
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

    private async Task DeleteFileAsync(Node nodeToBeDeleted)
    {
        File.Delete(nodeToBeDeleted.Address);
        await _replicaTree.RemoveNode(nodeToBeDeleted.Address);
        logger.Log(LogLevel.Information, "Deleted file: {FilePath}", nodeToBeDeleted.Address);
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
        await _replicaTree.RemoveNode(nodeToBeDeleted.Address);
        logger.Log(LogLevel.Information, "Deleted directory (recursively): {DirPath}", nodeToBeDeleted.Address);
    }
    
    // Normalize path to ensure consistent representation, sometimes paths can have trailing slashes
    private static string NormalizePath(string path)
    {
        return Path.GetFullPath(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
    }
    
    private void DebugPrint()
    {
        Console.WriteLine("\n Source tree");
        _sourceTree.PrintTree(_sourceTree.Root);
        Console.WriteLine("");
        foreach (var key  in _sourceTree.NodeLookup.Keys)
        {
            Console.WriteLine(key);
        }
        Console.WriteLine("\n Replica tree");
        _replicaTree.PrintTree(_replicaTree.Root);
        Console.WriteLine("");
        foreach (var key  in _replicaTree.NodeLookup.Keys)
        {
            Console.WriteLine(key);
        }
    }
    
    private static void PrintAddresses(List<string> addresses)
    {
        foreach (var address in addresses)
        {
            Console.WriteLine("Processing: " + address);
        }
    }
}