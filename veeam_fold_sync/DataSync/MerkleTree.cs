namespace veeam_fold_sync.DataSync;

public class MerkleTree(string rootPath)
{
    public Node Root { get; private set; } = new(rootPath, null, true);
    // Dictionary for fast node lookup by path when rebuilding tree
    public Dictionary<string, Node> NodeLookup { get; } = new();
    
    public async Task BuildTreeAsync(Node node)
    {
        var folderEntries = Directory.GetFileSystemEntries(node.Address);
        var buildTasks = new List<Task>();
        
        // Build Merkel Tree bottom-up recursively
        foreach (var folderEntry in folderEntries)
        {
            if (Directory.Exists(folderEntry))
            {
                var childNode = new Node(folderEntry, node, isDirectory: true);
                node.Children.Add(childNode);
                NodeLookup.Add(NormalizePath(folderEntry), childNode);
                buildTasks.Add(BuildTreeAsync(childNode));
            }
            else if (File.Exists(folderEntry))
            {
                var childNode = new Node(folderEntry, node, isDirectory: false);
                NodeLookup.Add(NormalizePath(folderEntry), childNode);
                // For files, calculate hash immediately
                node.Children.Add(childNode);
                buildTasks.Add(childNode.CalculateHashAsync());
            }
        }

        // Wait for all child nodes to be processed
        await Task.WhenAll(buildTasks);
        
        if (node.IsDirectory)
        {
            // After processing all children, calculate directory hash
            await node.CalculateHashAsync();
        }
    }

    private async Task RebuildTreeAsync(Node node)
    {
        // Recalculate hashes up to the root
        while (node != null)
        {
            await node.CalculateHashAsync();
            node = node.Parent!;
        }

    }
    
    public async Task RemoveNodes(List<string> deletedAddresses)
    {
        // Loop through deleted addresses and remove corresponding nodes from source tree and lookup dictionary
        foreach (var address in deletedAddresses)
        {
            Console.WriteLine($"Removing node: {address}");
            // Check if node exists in lookup dictionary
            if (!NodeLookup.TryGetValue(address, out var nodeToDelete)) continue;
            
            // Remove node from parent's children list and from lookup dictionary
            nodeToDelete.Parent?.Children.Remove(nodeToDelete);
            NodeLookup.Remove(NormalizePath(address));
            
            // Mark parent node for hash update
            if (nodeToDelete.Parent != null)
            {
                Console.WriteLine("Rebuilding tree from parent: " + nodeToDelete.Parent.Address);
                await RebuildTreeAsync(nodeToDelete.Parent);
            }
        }
    }
    
    public async Task UpdateNodes(List<string> updatedAddresses)
    {
        // For each updated address, recalculate hash and propagate changes up the tree
        foreach (var address in updatedAddresses)
        {
            var currentNode = NodeLookup[address];
            await RebuildTreeAsync(currentNode);
        }
    }
    
    public async Task AddNodes(List<string> addedAddresses)
    {
        foreach (var address in addedAddresses)
        {
            // Find parent node
            var parentAddress = Path.GetDirectoryName(address);
            Console.WriteLine("Finding parent for: " + address + " Parent: " + parentAddress);
            Console.WriteLine("adding node: " + address);

            // Ensure parent exists in the tree
            if (parentAddress != null && NodeLookup.TryGetValue(parentAddress, out var parentNode))
            {
                if (NodeLookup.TryGetValue(address, out var existingNode))
                {
                    // Node already exists, just recalculate its hash
                    Console.WriteLine("Node already exists: " + address + ". Recalculating hash.");
                    await RebuildTreeAsync(existingNode);
                    continue;
                }
                Console.WriteLine("Adding node: " + address);
                var isDirectory = Directory.Exists(address);
                var newNode = new Node(address, parentNode, isDirectory);
                parentNode.Children.Add(newNode);
                NodeLookup.Add(NormalizePath(address), newNode);
                await RebuildTreeAsync(newNode);
            }
        }
    }
    
    private static string NormalizePath(string path)
    {
        return Path.GetFullPath(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
    }

    public void PrintTree(Node node, string indent = "")
    {
        Console.WriteLine($"{indent}- {Path.GetFileName(node.Address)} ");
        foreach (var child in node.Children)
        {
            PrintTree(child, indent + "  ");
        }
    }
}