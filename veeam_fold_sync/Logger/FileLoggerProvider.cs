using Microsoft.Extensions.Logging;

namespace veeam_fold_sync.Logger;

public class FileLoggerProvider : ILoggerProvider
{
    private readonly StreamWriter _logFileWriter;

    public FileLoggerProvider(StreamWriter logFileWriter)
    {
        _logFileWriter = logFileWriter ?? throw new ArgumentNullException(nameof(logFileWriter));
    }

    public ILogger CreateLogger(string categoryName)
    {
        return new FileLogger(_logFileWriter);
    }

    public void Dispose()
    {
        _logFileWriter.Dispose();
    }
}