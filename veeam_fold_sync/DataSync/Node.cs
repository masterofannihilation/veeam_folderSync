using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;

namespace veeam_fold_sync.DataSync;

public class Node(string address, bool isDirectory)
{
    public string Hash { get; set; } = "";
    public string Address { get; set; } = address;
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

                while ((bytesRead = await fileStream.ReadAsync(buffer)) > 0)
                {
                    hasher.AppendData(buffer, 0, bytesRead);
                }
                var hash = hasher.GetHashAndReset();
                Hash = Convert.ToHexStringLower(hash);
            }
            Console.WriteLine("Calculated hash for file: " + Address + " Hash: " + Hash);
        }
    }
    
    public async Task CopyToAsync(string dstAddress, ILogger logger)
    {
        if (IsDirectory)
        {
            // Ensure destination directory exists
            if (!Directory.Exists(dstAddress))
            {
                Directory.CreateDirectory(dstAddress);
                logger.Log(LogLevel.Information, "Created directory: {DirPath}", dstAddress);
            }
            // Recursively copy children
            foreach (var child in Children)
            {
                var childDestPath = Path.Combine(dstAddress, Path.GetFileName(child.Address));
                await child.CopyToAsync(childDestPath, logger);
            }
        }
        else
        {
            // Use async file copy
            await using var sourceStream = new FileStream(Address, FileMode.Open, FileAccess.Read, 
                FileShare.Read, 4096, FileOptions.Asynchronous);
            await using var destStream = new FileStream(dstAddress, FileMode.Create, FileAccess.Write, 
                FileShare.None, 4096, FileOptions.Asynchronous);
        
            await sourceStream.CopyToAsync(destStream);
            logger.Log(LogLevel.Information, "Copied file: {FilePath}", dstAddress);
        }
    }

    public async Task DeleteAsync(ILogger logger)
    {
        if (IsDirectory)
        {
            // Delete children in parallel
            var deleteTasks = Children.Select(child => child.DeleteAsync(logger));
            await Task.WhenAll(deleteTasks);
            // Delete the directory itself
            Directory.Delete(Address);
            logger.Log(LogLevel.Information, "Deleted directory (recursively): {DirPath}", Address);
        }
        else
        {   
            File.Delete(Address);
            logger.Log(LogLevel.Information, "Deleted file: {FilePath}", Address);
        }
    }
}