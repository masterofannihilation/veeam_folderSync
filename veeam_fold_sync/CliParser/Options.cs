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

        if (!srcValid || !repValid || !logValid)
        {
            Console.WriteLine("Folder path or file path nonexistent");
            Console.WriteLine($"Source folder: {srcValid}");
            Console.WriteLine($"Rep folder: {repValid}");
            Console.WriteLine($"Log file: {logValid}");
            return false;
        }

        var srcFull = Path.GetFullPath(SrcFolder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var repFull = Path.GetFullPath(RepFolder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        
        if (string.Equals(srcFull, repFull, StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine("Source and replica folders must not be the same folder.");
            return false;
        }

        if ((srcFull.Length > repFull.Length && srcFull.StartsWith(repFull + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) ||
            (repFull.Length > srcFull.Length && repFull.StartsWith(srcFull + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)))
        {
            Console.WriteLine("Source and replica folders must not be subdirectories of one another.");
            return false;
        }

        return true;
    }
}