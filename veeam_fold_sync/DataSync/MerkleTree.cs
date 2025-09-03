namespace veeam_fold_sync.DataSync;

public class MerkleTree(string rootPath)
{
    public Node Root { get; private set; } = new(rootPath, null, true);
    // Dictionary for fast node lookup by address
    public Dictionary<string, Node> NodeLookup { get; } = new();
    
    public async Task BuildTreeAsync(Node node)
    {
        // Get all files and directories in the current node's address
        var folderEntries = Directory.GetFileSystemEntries(node.Address);
        var buildTasks = new List<Task>();
        
        // Process each entry, start building the tree recursively from leaf nodes
        foreach (var folderEntry in folderEntries)
        {
            // When we reach a file (leaf node), calculate its hash and propagate changes up the tree
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
                node.Children.Add(childNode);
                // Calculate hash immediately
                buildTasks.Add(childNode.CalculateHashAsync());
            }
        }

        // Wait for all child nodes to be processed
        await Task.WhenAll(buildTasks);
        
        if (node.IsDirectory)
        {
            // After processing all children, calculate directory hash based on children's hashes
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
    
    public async Task AddNodes(List<string> addedAddresses)
    {
        foreach (var address in addedAddresses.Distinct())
        {
            Console.WriteLine("Adding node " + address);
            // Find parent node
            var parentAddress = Path.GetDirectoryName(address);

            // Ensure parent exists in the tree
            if (parentAddress != null && NodeLookup.TryGetValue(NormalizePath(parentAddress), out var parentNode))
            {
                if (NodeLookup.TryGetValue(NormalizePath(address), out var existingNode))
                {
                    // Node already exists, just recalculate its hash
                    Console.WriteLine($"Rebuilding node {address}");
                    await RebuildTreeAsync(existingNode);
                    continue;
                }
                var isDirectory = Directory.Exists(address);
                var newNode = new Node(NormalizePath(address), parentNode, isDirectory);
                parentNode.Children.Add(newNode);
                NodeLookup.Add(NormalizePath(address), newNode);
                Console.WriteLine("Added node " + address);
                await RebuildTreeAsync(newNode);
            }
        }
    }
    
    public async Task RemoveNodes(List<string> deletedAddresses)
    {
        // Loop through deleted addresses and remove corresponding nodes from source tree and lookup dictionary
        foreach (var address in deletedAddresses)
        {
            Console.WriteLine("Trying to delete node " + address);
            // // Check if node exists in lookup dictionary
            if (!NodeLookup.TryGetValue(NormalizePath(address), out var nodeToDelete)) continue;
            Console.WriteLine("Deleting node " + address);
            // Recursively remove all descendants
            RemoveNodeAndDescendants(nodeToDelete);
            
            // Rebuild tree from parent node upwards
            if (nodeToDelete.Parent != null)
            {
                await RebuildTreeAsync(nodeToDelete.Parent);
            }
        }
    }
    
    private void RemoveNodeAndDescendants(Node node)
    {
        foreach (var child in node.Children.ToList())
        {
            RemoveNodeAndDescendants(child);
        }
        node.Parent?.Children.Remove(node);
        NodeLookup.Remove(NormalizePath(node.Address));
    }
    
    public async Task UpdateNodes(List<string> updatedAddresses)
    {
        // For each updated address, recalculate hash and propagate changes up the tree
        foreach (var address in updatedAddresses)
        {
            var currentNode = NodeLookup[NormalizePath(address)];
            await RebuildTreeAsync(currentNode);
        }
    }
    
    private static string NormalizePath(string path)
    {
        return Path.GetFullPath(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
    }

    public void PrintTree(Node node, string indent = "")
    {
        Console.WriteLine($"{indent}- {Path.GetFileName(node.Address) } (Hash: {node.Hash})");
        foreach (var child in node.Children)
        {
            PrintTree(child, indent + "  ");
        }
    }
}