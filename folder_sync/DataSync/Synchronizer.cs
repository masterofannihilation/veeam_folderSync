using System.Diagnostics;
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

        await DeleteExtraFiles();
        
        // Initial folder synchronization
        await SyncTreesAsync(_sourceTree.Root, _replicaTree.Root);
    }

    public async Task PeriodicSync()
    {
        // Update source tree and replica tree based on watcher changes
        await ProcessWatcherListAsync(_replicaWatcher.RenamedAddresses, _replicaTree.UpdateNodeAddress);
        await HandleRenamedAsync(_sourceWatcher.RenamedAddresses);
        await DeleteExtraFiles(_replicaWatcher.RenamedAddresses);
        
        var updateTasks = new List<Task>
        {
            ProcessWatcherListAsync(_sourceWatcher.DeletedAddresses, _sourceTree.RemoveNode),
            ProcessWatcherListAsync(_sourceWatcher.AddedAddresses, _sourceTree.AddNode),
            ProcessWatcherListAsync(_sourceWatcher.UpdatedAddresses, _sourceTree.UpdateNode),
            
            ProcessWatcherListAsync(_replicaWatcher.DeletedAddresses, _replicaTree.RemoveNode),
            ProcessWatcherListAsync(_replicaWatcher.AddedAddresses, _replicaTree.AddNode),
            ProcessWatcherListAsync(_replicaWatcher.UpdatedAddresses, _replicaTree.UpdateNode)
        };

        await Task.WhenAll(updateTasks);
        
        // Sync source and replica trees
        await SyncTreesAsync(_sourceTree.Root, _replicaTree.Root);
        
        DebugPrint();
    }
    
    

    private async Task DeleteExtraFiles(Dictionary<string, string> addresses)
    {
        foreach (var addr in addresses)
        {
            var relativePath = Path.GetRelativePath(_replicaTree.Root.Address, addr.Value);
            var sourcePath = Path.Combine(_sourceTree.Root.Address, relativePath);
            if (File.Exists(addr.Value) && !File.Exists(sourcePath))
            {
                File.Delete(addr.Value);
                logger.Log(LogLevel.Information, "Deleted extra file: {FilePath}", addr.Value);
            }
            else if (Directory.Exists(addr.Value) && !Directory.Exists(sourcePath))
            {
                Directory.Delete(addr.Value, true); // true for recursive delete
                logger.Log(LogLevel.Information, "Deleted extra directory: {DirPath}", addr.Value);
            }
            
        }
    }

    private async Task DeleteExtraFiles()
    {
        var replicaRoot = _replicaTree.Root.Address;
        var sourceRoot = _sourceTree.Root.Address;

        // Delete extra files
        foreach (var file in Directory.EnumerateFiles(replicaRoot, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(replicaRoot, file);
            var sourcePath = Path.Combine(sourceRoot, relativePath);
            if (!File.Exists(sourcePath))
            {
                File.Delete(file);
                logger.Log(LogLevel.Information, "Deleted extra file: {FilePath}", file);
            }
        }

        // Delete extra directories (bottom-up)
        var directories = Directory.EnumerateDirectories(replicaRoot, "*", SearchOption.AllDirectories)
            .OrderByDescending(d => d.Length);
        foreach (var dir in directories)
        {
            var relativePath = Path.GetRelativePath(replicaRoot, dir);
            var sourcePath = Path.Combine(sourceRoot, relativePath);
            if (!Directory.Exists(sourcePath))
            {
                Directory.Delete(dir, true);
                logger.Log(LogLevel.Information, "Deleted extra directory: {DirPath}", dir);
            }
        }
    }


    private async Task HandleRenamedAsync(Dictionary<string, string> addressMap)
    {
        foreach (var oldAddr in addressMap.Keys)
        {
            var newAddr = addressMap[oldAddr];
            // Rename in source tree
            await _sourceTree.UpdateNodeAddress(oldAddr, newAddr);

            // Get relative path from source root
            var relativePath = Path.GetRelativePath(_sourceTree.Root.Address, newAddr);
            var replicaOldAddr = Path.Combine(_replicaTree.Root.Address, Path.GetRelativePath(_sourceTree.Root.Address, oldAddr));
            // Compute new path in replica
            var replicaNewAddr = Path.Combine(_replicaTree.Root.Address, relativePath);

            // Apply the rename in the replica tree
            await _replicaTree.UpdateNodeAddress(replicaOldAddr, replicaNewAddr);
            
            DebugPrint();
            // Physically rename in the filesystem
            if (Directory.Exists(replicaOldAddr))
            {
                Directory.Move(replicaOldAddr, replicaNewAddr);
                logger.Log(LogLevel.Information, "Renamed directory: {OldDirPath} to {NewDirPath}", replicaOldAddr, replicaNewAddr);
            }
            else if (File.Exists(replicaOldAddr))
            {
                File.Move(replicaOldAddr, replicaNewAddr);
                logger.Log(LogLevel.Information, "Renamed file: {OldFilePath} to {NewFilePath}", replicaOldAddr, replicaNewAddr);
            }
            Console.WriteLine("Finished renaming in both trees.");
        }
    }

    private static async Task ProcessWatcherListAsync(Dictionary<string, string> addresses, Func<string, string, Task> processFunc)
    {
        if (addresses.Count > 0)
        {
            // PrintAddresses(addresses);
            foreach (var addr in addresses)
            {
                await processFunc(addr.Key, addr.Value);
                
            }
        }
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