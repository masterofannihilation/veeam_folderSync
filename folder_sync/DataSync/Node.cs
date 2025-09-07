using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;

namespace veeam_fold_sync.DataSync;

public class Node(string address, Node? parent, bool isDirectory)
{
    public string Hash { get; private set; } = "";
    public string Address { get; set; } = address;
    public Node? Parent { get; set; } = parent;
    public List<Node> Children { get; set; } = [];
    public bool IsDirectory { get; set; } = isDirectory;
    
    public async Task CalculateHashAsync()
    {
        if (IsDirectory) // If a directory, hash is based on children's hashes
        {
            // Sort for consistency in case of enumerating in different orders
            var childHashes = Children.Select(child => child.Hash).OrderBy(h => h);
            var combined = string.Concat(childHashes);
            var bytes = Encoding.UTF8.GetBytes(combined);
            var hash = SHA256.HashData(bytes);
            Hash = Convert.ToHexStringLower(hash);
        }
        else // If a file, hash is based on file content
        {
            // Use filestream to prevent loading entire file into memory, instead read in chunks
            const int bufferSize = 4096 * 100; // 400KB
            using (var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            await using (
                var fileStream = new FileStream(
                    Address, 
                    FileMode.Open, 
                    FileAccess.Read, 
                    FileShare.Read, 
                    bufferSize, 
                    FileOptions.SequentialScan | FileOptions.Asynchronous))
            {
                var buffer = new byte[bufferSize];
                int bytesRead;
                
                // Read file in chunks and append to hasher
                while ((bytesRead = await fileStream.ReadAsync(buffer)) > 0)
                {
                    hasher.AppendData(buffer, 0, bytesRead);
                }
                var hash = hasher.GetHashAndReset();
                Hash = Convert.ToHexStringLower(hash);
            }
        }
    }
}