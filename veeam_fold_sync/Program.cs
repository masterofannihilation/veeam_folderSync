using CommandLine;
using Microsoft.Extensions.Logging;
using veeam_fold_sync.CliParser;
using veeam_fold_sync.Logger;

namespace veeam_fold_sync;

internal class Program
{
    private static void Main(string[] args)
    {
        Parser.Default.ParseArguments<Options>(args)
            .WithParsed(options =>
            {
                if (options.Validate())
                {
                    Run(options);
                }
            })
            .WithNotParsed(HandleErrors);
    }

    static void Run(Options options)
    {
        // Setup logger
        using var loggerFactory = FileLogger.SetupLogger(options.LogFile);
        ILogger<Program> logger = loggerFactory.CreateLogger<Program>();
        logger.Log(LogLevel.Information, "Logger initialized.");
        
        
    }
    
    private static void HandleErrors(IEnumerable<Error> errors)
    {
        foreach (var error in errors)
        {
            Console.WriteLine(error.ToString());
        }
    }
}