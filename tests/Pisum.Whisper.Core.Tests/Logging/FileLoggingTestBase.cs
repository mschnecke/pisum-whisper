namespace Pisum.Whisper.Core.Tests.Logging;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Pisum.Whisper.Core.Logging;
using Pisum.Whisper.Core.Settings;

/// <summary>
/// A temporary home directory and a host built the way the application builds one, so these tests
/// exercise the registration itself rather than a copy of it.
/// </summary>
public abstract class FileLoggingTestBase : IDisposable
{
    private readonly string _home = string.Empty;

    protected LogDirectory Logs { get; private set; } = new();

    protected string SettingsPath => Path.Combine(_home, ".pisum-whisper.json");

    protected FileLoggingTestBase()
    {
        _home = Path.Combine(Path.GetTempPath(), "pisum-whisper-tests", Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(_home);
        Logs = new LogDirectory(Path.Combine(_home, "logs"));
    }

    public void Dispose()
    {
        Directory.Delete(_home, true);
    }

    protected string[] LogFiles()
    {
        return Directory.Exists(Logs.Path) ? Directory.GetFiles(Logs.Path, LogDirectory.LogFileSearchPattern) : [];
    }

    /// <summary>
    /// The application registers the settings store alongside file logging, and the hosted service
    /// that owns the level switch depends on it.
    /// </summary>
    protected IHost BuildHost(FileLoggingOptions options)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddFileLogging(options, out _);
        builder.Services.AddSingleton(provider =>
            new SettingsStore(provider.GetRequiredService<ILogger<SettingsStore>>(), SettingsPath));

        return builder.Build();
    }
}
