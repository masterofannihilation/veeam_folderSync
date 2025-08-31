using Microsoft.Extensions.Logging;

namespace veeam_fold_sync.DataSync;

public class MerkleTree(string rootPath, ILogger logger)
{
    public Node Root { get; private set; } = new(rootPath, true);

    public static void BuildTree(Node node)
    {
        var folderEntries = Directory.GetFileSystemEntries(node.Path);
        
        // Build Merkel Tree bottom-up recursively
        foreach (var folderEntry in folderEntries)
        {
            if (Directory.Exists(folderEntry))
            {
                var childNode = new Node(folderEntry, isDirectory: true);
                node.Children.Add(childNode);
                BuildTree(childNode);
            }
            else if (File.Exists(folderEntry))
            {
                var childNode = new Node(folderEntry, isDirectory: false);
                // For files, calculate hash immediately
                childNode.CalculateHash();
                node.Children.Add(childNode);
            }
        }
        
        if (node.IsDirectory)
        {
            // After processing all children, calculate directory hash
            node.CalculateHash();
        }
    }
    
    public void SyncTrees(Node source, Node replica)
    {
        if (source.Hash == replica.Hash) return; // Nothing to do
    
        if (!source.IsDirectory) // File
        {
            File.Copy(source.Path, replica.Path, true); // Overwrite
            logger.Log(LogLevel.Information, "Updated file: {FilePath}", replica.Path);
        }
        else // Directory
        {
            foreach (var sourceChild in source.Children)
            {
                var replicaChild = replica.Children.FirstOrDefault(c => 
                    Path.GetFileName(c.Path) == Path.GetFileName(sourceChild.Path));
            
                // If child doesn't exist in replica, copy it
                if (replicaChild == null)
                {
                    CopyEntireNode(sourceChild, replica.Path);
                }
                else // If it exists, recursion
                {
                    SyncTrees(sourceChild, replicaChild);
                }
            }
        
            // Hashset of source filenames for fast lookup
            var sourceNames = new HashSet<string>(source.Children.Select(c => Path.GetFileName(c.Path)));
            
            // Delete extra files or folders in replica that don't exist in source
            foreach (var replicaChild in replica.Children)
            {
                if (!sourceNames.Contains(Path.GetFileName(replicaChild.Path)))
                {
                    DeleteNode(replicaChild);
                }
            }
        }
    }

    // Copies an entire node (file or directory) to the replica path
    private void CopyEntireNode(Node sourceChild, string replicaPath)
    {
        if (sourceChild.IsDirectory)
        {
            var newDirPath = Path.Combine(replicaPath, Path.GetFileName(sourceChild.Path));
            Directory.CreateDirectory(newDirPath);
            logger.Log(LogLevel.Information, "Created directory: {DirectoryPath}", newDirPath);
            
            foreach (var child in sourceChild.Children)
            {
                CopyEntireNode(child, newDirPath);
            }
        }
        else
        {
            var newFilePath = Path.Combine(replicaPath, Path.GetFileName(sourceChild.Path));
            File.Copy(sourceChild.Path, newFilePath);
            logger.Log(LogLevel.Information, "Copied file: {FilePath}", newFilePath);
        }
    }

    // Deletes a file or directory (recursively)
    private void DeleteNode(Node replicaChild)
    {
        if (replicaChild.IsDirectory)
        {
            Directory.Delete(replicaChild.Path, true);
            logger.Log(LogLevel.Information, "Deleted directory: {DirectoryPath}", replicaChild.Path);
        }
        else
        {
            File.Delete(replicaChild.Path);
            logger.Log(LogLevel.Information, "Deleted file: {FilePath}", replicaChild.Path);
        }
    }
    
    public static void PrintTree(Node node, string indent = "")
    {
        Console.WriteLine($"{indent}- {Path.GetFileName(node.Path)}");
        foreach (var child in node.Children)
        {
            PrintTree(child, indent + "  ");
        }
    }
}