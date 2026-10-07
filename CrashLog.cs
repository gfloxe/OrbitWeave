using System.Reflection;

namespace OrbitWeave;

public static class CrashLog
{
    private static readonly object Gate = new();
    public static string FilePath => Path.Combine(ActionStore.DirectoryPath, "crash.log");
    public static string Version => typeof(App).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown";

    public static string Format(string context, object? error, bool terminating) =>
        $"{DateTimeOffset.Now:O} | context={context} | terminating={terminating} | version={Version} | exe={Environment.ProcessPath} | pid={Environment.ProcessId} | thread={Environment.CurrentManagedThreadId}{Environment.NewLine}" +
        $"{error}{Environment.NewLine}{Environment.NewLine}";

    // Un journal ne doit jamais provoquer une seconde exception pendant la terminaison.
    public static bool Write(string context, object? error, bool terminating = false, string? path = null)
    {
        try
        {
            var text = Format(context, error, terminating);
            lock (Gate)
            {
                var target = path ?? FilePath;
                try { Append(target, text); return true; }
                catch { Append(Path.Combine(Path.GetTempPath(), $"OrbitWeave-crash-{Environment.ProcessId}.log"), text); return false; }
            }
        }
        catch { return false; }
    }

    private static void Append(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.AppendAllText(path, text);
    }
}
