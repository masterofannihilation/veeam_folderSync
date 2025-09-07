using Microsoft.Extensions.Logging;

namespace veeam_fold_sync.Logger;

public class FileLogger( StreamWriter logFileWriter) : ILogger
{
    public static ILoggerFactory SetupLogger(String filePath)
    {
        var logFileWriter = new StreamWriter(filePath, append: true);
        var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddSimpleConsole(options =>
            {
                options.IncludeScopes = true;
                options.SingleLine = true;
                options.TimestampFormat = "yyyy-MM-dd HH:mm:ss ";
            });
            builder.AddProvider(new FileLoggerProvider(logFileWriter));
        });
        return loggerFactory;
    }

    public IDisposable BeginScope<TState>(TState state) where TState : notnull
    {
        throw new NotImplementedException();
    }

    public bool IsEnabled(LogLevel logLevel)
    {
        // Only log Information level and above
        return logLevel >= LogLevel.Information;
    }

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        // Ensure that only information level and higher logs are recorded
        if (!IsEnabled(logLevel))
        {
            return;
        }

        // Get the formatted log message
        var message = formatter(state, exception);
        var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

        logFileWriter.WriteLine($"[{timestamp}] [{logLevel}] {message}");
        logFileWriter.Flush();
    }
}