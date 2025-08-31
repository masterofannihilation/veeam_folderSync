using System.Security.AccessControl;
using CommandLine;

namespace veeam_fold_sync.CliParser;

public class Options
{
    [Option('s', "srcFolder", Required = true, HelpText = "Path to the source folder to be replicated") ]
    public required String SrcFolder { get; set; }
    
    [Option('r', "repFolder", Required = true, HelpText = "Path to the replica folder") ]
    public required String RepFolder { get; set; }
    
    [Option('i', "syncInterval", Required = true, HelpText = "Synchronization interval in milliseconds") ]
    public required int SyncInterval { get; set; }
    
    [Option('l', "logFile", Required = true, HelpText = "Path to the log file") ]
    public required String LogFile { get; set; }

    public bool Validate()
    {
        bool srcValid = Directory.Exists(SrcFolder);
        bool repValid = Directory.Exists(RepFolder);
        bool logValid = File.Exists(LogFile);

        if (srcValid && repValid && logValid)
        {
            return true;
        }

        Console.WriteLine("Folder path or file path nonexistent");
        Console.WriteLine($"Source folder: {srcValid}");
        Console.WriteLine($"Rep folder: {repValid}");
        Console.WriteLine($"Log file: {logValid}");
        return false;
    }
}