using CommandLine;
using veeam_fold_sync.CliParser;

class Program
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
        Console.WriteLine("Hello world!");
    }
    
    private static void HandleErrors(IEnumerable<Error> errors)
    {
        foreach (var error in errors)
        {
            Console.WriteLine(error.ToString());
        }
    }
}
