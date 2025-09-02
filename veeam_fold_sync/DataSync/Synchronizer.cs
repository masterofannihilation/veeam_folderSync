using Microsoft.Extensions.Logging;
using veeam_fold_sync.CliParser;

namespace veeam_fold_sync.DataSync;

public class Synchronizer(Options options, ILogger logger)
{
    private readonly MerkleTree _sourceTree = new (options.SrcFolder);
    private readonly MerkleTree _replicaTree = new(options.RepFolder);
    private readonly Watcher _watcher = new ();

    public async Task InitialSync(Options options)
    {
        // Build merkle trees of source and replica folders
        var buildTasks = new List<Task>();
        
        _sourceTree.NodeLookup.Add(NormalizePath(_sourceTree.Root.Address), _sourceTree.Root);
        buildTasks.Add(_sourceTree.BuildTreeAsync(_sourceTree.Root));
    
        _replicaTree.NodeLookup.Add(NormalizePath(_replicaTree.Root.Address), _replicaTree.Root);
        buildTasks.Add(_replicaTree.BuildTreeAsync(_replicaTree.Root));
        
        await Task.WhenAll(buildTasks);

        _sourceTree.PrintTree(_sourceTree.Root);
        
        // Start watching source folder for changes
        _ = Task.Run( () => _watcher.Watch(options.SrcFolder));
        
        // Initial folder synchronization
        await SyncTreesAsync(_sourceTree.Root, _replicaTree.Root);
    }

    public async Task PeriodicSync()
    {
        await ProcessWatcherListAsync(_watcher.DeletedAddresses, _sourceTree.RemoveNodes);
        await ProcessWatcherListAsync(_watcher.AddedAddresses, _sourceTree.AddNodes);
        await ProcessWatcherListAsync(_watcher.UpdatedAddresses, _sourceTree.UpdateNodes);

        _sourceTree.PrintTree(_sourceTree.Root);

        // Sync source and replica trees
        await SyncTreesAsync(_sourceTree.Root, _replicaTree.Root);
    }
    
    private async Task ProcessWatcherListAsync(List<string> addresses, Func<List<string>, Task> processFunc)
    {
        if (addresses.Count > 0)
        {
            await processFunc(addresses);
            addresses.Clear();
        }
    }

    private void printAddresses(List<string> addresses)
    {
        foreach (var address in addresses)
        {
            Console.WriteLine("Processing: " + address);
        }
    }

    private async Task SyncTreesAsync(Node source, Node replica)
    {
        Console.WriteLine("Comparing source: " + source.Address + " with replica: " + replica.Address);
        // If root hashes match, trees are identical
        if (source.Hash == replica.Hash) return;

        if (!source.IsDirectory) // File
        {
            Console.WriteLine("Synchronizing file: " + source.Address);
            await CopyToAsync(source, replica.Address);
        }
        else // Directory
        {
            Console.WriteLine("Synchronizing directory: " + source.Address);
            await SynchronizeDirectory(source, replica);
            await DeleteExtraFiles(source, replica);
        }
    }

    private async Task SynchronizeDirectory(Node source, Node replica)
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
        Console.WriteLine("Deleting extra files in replica: " + replica.Address);
        var sourceNames = new HashSet<string>(source.Children.Select(c => Path.GetFileName(c.Address)));
        var replicaChildrenCopy = replica.Children.ToList();
        var deleteTasks = replicaChildrenCopy
            .Where(replicaChild => !sourceNames.Contains(Path.GetFileName(replicaChild.Address)))
            .Select(child =>
            {
                Console.WriteLine($"Replica has extra '{child.Address}'. Deleting.");
                return DeleteAsync(child);
            });

        await Task.WhenAll(deleteTasks);
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

        logger.Log(LogLevel.Information, "Copied file: {FilePath}", dstAddress);
        await _replicaTree.AddNodes([dstAddress]);
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
        // Edit the replica tree
        await _replicaTree.AddNodes([dstAddress]);
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
        await _replicaTree.RemoveNodes(new List<string> { nodeToBeDeleted.Address });
        logger.Log(LogLevel.Information, "Deleted file: {FilePath}", nodeToBeDeleted.Address);
    }

    private async Task DeleteDirectoryAsync(Node nodeToBeDeleted)
    {
        // Delete children in parallel
        var deleteTasks = nodeToBeDeleted.Children.Select(DeleteAsync);
        await Task.WhenAll(deleteTasks);
        // Delete the directory itself
        Directory.Delete(nodeToBeDeleted.Address);
        await _replicaTree.RemoveNodes([nodeToBeDeleted.Address]);
        logger.Log(LogLevel.Information, "Deleted directory (recursively): {DirPath}", nodeToBeDeleted.Address);
    }
    
    private static string NormalizePath(string path)
    {
        return Path.GetFullPath(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
    }
}