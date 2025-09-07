namespace veeam_fold_sync.DataSync;

public class MerkleTree(string rootPath)
{
    public Node Root { get; private set; } = new(rootPath, null, true);
    // Dictionary for fast node lookup by address
    public Dictionary<string, Node> NodeLookup { get; } = new();
    
    public async Task BuildTreeAsync(Node node)
    {
        NodeLookup.Add(NormalizePath(node.Address), node);
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
                buildTasks.Add(BuildTreeAsync(childNode));
            }
            else if (File.Exists(folderEntry))
            {
                var childNode = new Node(folderEntry, node, isDirectory: false);
                node.Children.Add(childNode);
                NodeLookup.Add(NormalizePath(folderEntry), childNode);
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
    
    public async Task AddNode(string addr)
    {
        // Find parent node
        var parentAddress = Path.GetDirectoryName(addr);

        // Ensure parent exists in the tree
        if (parentAddress != null && NodeLookup.TryGetValue(NormalizePath(parentAddress), out var parentNode))
        {
            if (NodeLookup.ContainsKey(NormalizePath(addr)))
                return; // Node already exists, no need to add
            
            // Create new node and add to parent's children
            var isDirectory = Directory.Exists(addr);
            var newNode = new Node(NormalizePath(addr), parentNode, isDirectory);
            parentNode.Children.Add(newNode);
            NodeLookup.Add(NormalizePath(addr), newNode);
            // Rebuild tree from new node upwards to propagate hash changes
            await RebuildTreeAsync(newNode);
        }
    }
    
    public async Task RemoveNode(string addr)
    {
        // Check if node exists in lookup dictionary
        if (!NodeLookup.TryGetValue(NormalizePath(addr), out var nodeToDelete))
            return;
        
        // Remove the node including all its descendants
        NodeLookup.Remove(NormalizePath(addr));
        RemoveNodeChildren(nodeToDelete);
        // If parent exists (not root), rebuild tree from parent upwards
        if (nodeToDelete.Parent != null)
        {
            await RebuildTreeAsync(nodeToDelete.Parent);
        }
    }
    
    private void RemoveNodeChildren(Node node)
    {
        // Recursively remove all children from lookup and parent's children list
        var childrenCopy = node.Children.ToList(); // Create a copy to avoid modification during iteration
        foreach (var child in childrenCopy)
        {
            RemoveNodeChildren(child);
        }
        node.Parent?.Children.Remove(node);
        NodeLookup.Remove(NormalizePath(node.Address));
    }

    public async Task UpdateNode(string addr)
    {
        if (!NodeLookup.TryGetValue(NormalizePath(addr), out var nodeToUpdate))
        {
            if (nodeToUpdate != null) await RebuildTreeAsync(nodeToUpdate);
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