namespace veeam_fold_sync.DataSync;

public class Watcher
{
    public List<string> UpdatedAddresses { get; set; } = [];
    public List<string> DeletedAddresses { get; set; } = [];
    public List<string> AddedAddresses { get; set; } = [];
    public Dictionary<string, string> RenamedAddresses { get; } = new();
    private FileSystemWatcher _watcher = new ();
    
    public Task Watch(string address)
    {
        _watcher = new FileSystemWatcher(address);
        _watcher.NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite;
        _watcher.Filter = "*.*";
        _watcher.IncludeSubdirectories = true;
        _watcher.EnableRaisingEvents = true;

        _watcher.Changed += OnFileChanged;
        _watcher.Created += OnFileCreated;
        _watcher.Deleted += OnFileDeleted;
        _watcher.Renamed += OnFileRenamed;
        _watcher.Error += OnError;
        
        return Task.Delay(-1);
    }

    private void OnFileRenamed(object sender, RenamedEventArgs e)
    {
        Console.WriteLine("Renamed: " + e.OldFullPath + " to " + e.FullPath);
        RenamedAddresses.Add(e.OldFullPath, e.FullPath);
    }

    private void OnFileCreated(object sender, FileSystemEventArgs e)
    {
        // Console.WriteLine("Created: " + e.FullPath);
        if (!AddedAddresses.Contains(e.FullPath))
            AddedAddresses.Add(e.FullPath);
    }

    private void OnFileDeleted(object sender, FileSystemEventArgs e)
    {
        if (!DeletedAddresses.Contains(e.FullPath))
            DeletedAddresses.Add(e.FullPath);
    }

    private void OnFileChanged(object sender, FileSystemEventArgs e)
    {
        Console.WriteLine("Changed: " + e.FullPath);
        if (!UpdatedAddresses.Contains(e.FullPath))
            UpdatedAddresses.Add(e.FullPath);
    }
    private static void OnError(object sender, ErrorEventArgs e)
    {
        throw new NotImplementedException();
    }
}