using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace IPTVPlayer.Services;

public static class DiagnosticLog
{
    private const long MaximumLogBytes = 25 * 1024 * 1024;
    private static readonly object Sync = new();
    private static global::System.IO.FileStream? _stream;
    private static bool _limitReached;
    private static bool _isShutdown;

    public static string LogFolder { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "LiveTV",
        "Logs");

    public static string? CurrentLogPath { get; private set; }

    public static void Initialize()
    {
        lock (Sync)
        {
            if (_stream != null)
            {
                return;
            }

            if (_isShutdown)
            {
                return;
            }

            try
            {
                Directory.CreateDirectory(LogFolder);
                DeleteOldLogs();

                var architecture = RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant();
                var fileName = $"LiveTV-{DateTime.Now:yyyyMMdd-HHmmss}-{architecture}-{Environment.ProcessId}.log";
                CurrentLogPath = Path.Combine(LogFolder, fileName);
                _stream = new global::System.IO.FileStream(
                    CurrentLogPath,
                    FileMode.Create,
                    FileAccess.Write,
                    FileShare.ReadWrite);

                WriteUnsafe("INFO", "Session", "Diagnostic log started");
                WriteUnsafe("INFO", "Environment", $"AppVersion={GetAppVersion()}");
                WriteUnsafe("INFO", "Environment", $"OS={RuntimeInformation.OSDescription}");
                WriteUnsafe("INFO", "Environment", $"OSArchitecture={RuntimeInformation.OSArchitecture}; ProcessArchitecture={RuntimeInformation.ProcessArchitecture}");
                WriteUnsafe("INFO", "Environment", $"Framework={RuntimeInformation.FrameworkDescription}; RuntimeIdentifier={RuntimeInformation.RuntimeIdentifier}");
                WriteUnsafe("INFO", "Environment", $"ProcessPath={Environment.ProcessPath}; BaseDirectory={AppContext.BaseDirectory}");
                WriteUnsafe("INFO", "Environment", $"PreferIpv4Override={Environment.GetEnvironmentVariable("LIVETV_PREFER_IPV4") ?? "<unset>"}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[IPTV] Could not initialize diagnostic log: {ex}");
                _stream?.Dispose();
                _stream = null;
                CurrentLogPath = null;
            }
        }
    }

    public static void Info(string source, string message) => Write("INFO", source, message);

    public static void Warning(string source, string message) => Write("WARN", source, message);

    public static void Error(string source, string message) => Write("ERROR", source, message);

    public static void Error(string source, Exception exception) =>
        Write("ERROR", source, exception.ToString());

    public static void Shutdown()
    {
        lock (Sync)
        {
            if (_stream == null)
            {
                return;
            }

            WriteUnsafe("INFO", "Session", "Diagnostic log stopped");
            _stream.Dispose();
            _stream = null;
            _isShutdown = true;
        }
    }

    public static string FormatUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return url;
        }

        var queryKeys = uri.Query
            .TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(pair => pair.Split('=', 2)[0])
            .Where(key => !string.IsNullOrWhiteSpace(key));
        var querySummary = string.Join(',', queryKeys);

        return querySummary.Length == 0
            ? uri.GetLeftPart(UriPartial.Path)
            : $"{uri.GetLeftPart(UriPartial.Path)}?<{querySummary}>";
    }

    private static void Write(string level, string source, string message)
    {
        lock (Sync)
        {
            if (_isShutdown)
            {
                return;
            }

            if (_stream == null)
            {
                Initialize();
            }

            WriteUnsafe(level, source, message);
        }
    }

    private static void WriteUnsafe(string level, string source, string message)
    {
        if (_stream == null || _limitReached)
        {
            return;
        }

        try
        {
            if (_stream.Length >= MaximumLogBytes)
            {
                WriteLine("WARN", "Session", "Log size limit reached; further messages omitted");
                _limitReached = true;
                return;
            }

            var normalizedMessage = message.Replace("\r", " ").Replace("\n", " ");
            WriteLine(level, source, normalizedMessage);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[IPTV] Could not write diagnostic log: {ex.Message}");
        }
    }

    private static void WriteLine(string level, string source, string message)
    {
        var line = $"{DateTimeOffset.Now:O} [T{Environment.CurrentManagedThreadId:D2}] [{level}] [{source}] {message}{Environment.NewLine}";
        var bytes = Encoding.UTF8.GetBytes(line);
        _stream!.Write(bytes);
        _stream.Flush();
    }

    private static string GetAppVersion()
    {
        var assembly = Assembly.GetExecutingAssembly();
        return assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? assembly.GetName().Version?.ToString()
            ?? "unknown";
    }

    private static void DeleteOldLogs()
    {
        foreach (var file in new DirectoryInfo(LogFolder)
            .EnumerateFiles("LiveTV-*.log")
            .OrderByDescending(file => file.CreationTimeUtc)
            .Skip(9))
        {
            try
            {
                file.Delete();
            }
            catch
            {
            }
        }
    }
}