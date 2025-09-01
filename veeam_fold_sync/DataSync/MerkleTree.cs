using Microsoft.Extensions.Logging;

namespace veeam_fold_sync.DataSync;

public class MerkleTree(string rootPath, ILogger logger)
{
    public Node Root { get; private set; } = new(rootPath, true);

    public static async Task BuildTreeAsync(Node node)
    {
        var folderEntries = Directory.GetFileSystemEntries(node.Address);
        var buildTasks = new List<Task>();
        
        // Build Merkel Tree bottom-up recursively
        foreach (var folderEntry in folderEntries)
        {
            if (Directory.Exists(folderEntry))
            {
                var childNode = new Node(folderEntry, isDirectory: true);
                node.Children.Add(childNode);
                buildTasks.Add(BuildTreeAsync(childNode));
            }
            else if (File.Exists(folderEntry))
            {
                var childNode = new Node(folderEntry, isDirectory: false);
                // For files, calculate hash immediately
                node.Children.Add(childNode);
                buildTasks.Add(childNode.CalculateHashAsync());
            }
        }

        await Task.WhenAll(buildTasks);
        
        if (node.IsDirectory)
        {
            // After processing all children, calculate directory hash
            await node.CalculateHashAsync();
        }
    }
    
    public async Task SyncTreesAsync(Node source, Node replica)
    {
        // If root hashes match, trees are identical
        if (source.Hash == replica.Hash)
        {
            return;
        }

        if (!source.IsDirectory) // File
        {
            await source.CopyToAsync(replica.Address, logger);
        }
        else // Directory
        {
            var syncTasks = new List<Task>();
            
            foreach (var sourceChild in source.Children)
            {
                var replicaChild = replica.Children.FirstOrDefault(c => 
                    Path.GetFileName(c.Address) == Path.GetFileName(sourceChild.Address));
            
                // If child doesn't exist in replica, copy it
                if (replicaChild == null)
                {
                    syncTasks.Add(CopyEntireNodeAsync(sourceChild, replica.Address));
                }
                else // If it exists, recursion
                {
                    syncTasks.Add(SyncTreesAsync(sourceChild, replicaChild));
                }
            }
            
            await Task.WhenAll(syncTasks);

            // Hashset of source filenames for fast lookup
            var sourceNames = new HashSet<string>(source.Children.Select(c => Path.GetFileName(c.Address)));
            // Delete extra files or folders in replica that don't exist in source
            var deleteTasks = replica.Children
                .Where(replicaChild => !sourceNames.Contains(Path.GetFileName(replicaChild.Address)))
                .Select(replicaChild => replicaChild.DeleteAsync(logger));
            
            await Task.WhenAll(deleteTasks);
        }
    }

    // Copies an entire node (file or directory) to the replica path
    private async Task CopyEntireNodeAsync(Node sourceChild, string replicaPath)
    {
        var destPath = Path.Combine(replicaPath, Path.GetFileName(sourceChild.Address));
        await sourceChild.CopyToAsync(destPath, logger);
    }
    
    public static void PrintTree(Node node, string indent = "")
    {
        Console.WriteLine($"{indent}- {Path.GetFileName(node.Address)}");
        foreach (var child in node.Children)
        {
            PrintTree(child, indent + "  ");
        }
    }
}