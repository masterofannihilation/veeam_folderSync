using System.Text;

namespace veeam_fold_sync.DataSync;

public class Node(string path, bool isDirectory)
{
    public string Hash { get; set; } = "";
    public string Path { get; set; } = path;
    public List<Node> Children { get; set; } = [];
    public bool IsDirectory { get; set; } = isDirectory;
    
    public void CalculateHash()
    {
        if (IsDirectory) // If directory, hash is based on children's hashes
        {
            var childHashes = Children.Select(child => child.Hash).ToList();
            // Sort for consistency in case of enumerating in different orders
            var combined = string.Concat(childHashes.OrderBy(h => h));
            var bytes = Encoding.UTF8.GetBytes(combined);
            var hash = System.Security.Cryptography.SHA256.HashData(bytes);
            Hash = Convert.ToHexStringLower(hash);
        }
        else // If file, hash is based on file content
        {
            var fileBytes = File.ReadAllBytes(Path);
            var hash = System.Security.Cryptography.SHA256.HashData(fileBytes);
            Hash = Convert.ToHexStringLower(hash);
        }
    }
}