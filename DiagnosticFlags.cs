namespace OrbitWeave;

internal static class DiagnosticFlags
{
    private static readonly HashSet<string> Arguments = new(
        Environment.GetCommandLineArgs().Skip(1), StringComparer.OrdinalIgnoreCase);
    // --data-dir=<dossier> : données et diagnostics isolés (Windows ignore APPDATA/LOCALAPPDATA pour ces dossiers).
    public static string? DataDirectory => Arguments.FirstOrDefault(argument =>
        argument.StartsWith("--data-dir=", StringComparison.OrdinalIgnoreCase))?.Split('=', 2)[1];
    public static string DirectoryPath => DataDirectory is { } data ? Path.Combine(data, "Diagnostics") : Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OrbitWeave", "Diagnostics");
    public static bool Enabled => Arguments.Any(argument =>
        argument.StartsWith("--measure-", StringComparison.OrdinalIgnoreCase) ||
        argument.StartsWith("--preview", StringComparison.OrdinalIgnoreCase) ||
        argument is "--trace-frames" or "--test-escape" or "--dump-ui-ids" ||
        argument == "--open-settings" && DataDirectory is not null);
    private static readonly string? ReopenCountArgument = Arguments.FirstOrDefault(argument =>
        argument.StartsWith("--measure-reopen-count=", StringComparison.OrdinalIgnoreCase));
    private static readonly string? PreviewGroupIndexArgument = Arguments.FirstOrDefault(argument =>
        argument.StartsWith("--preview-group-index=", StringComparison.OrdinalIgnoreCase));
    public static int ReopenCount => ReopenCountArgument is not null &&
        int.TryParse(ReopenCountArgument.Split('=', 2)[1], out var count) ? Math.Clamp(count, 1, 20) : 4;

    public static bool MeasureFront => Arguments.Contains("--measure-front");
    // Journal des entrées de la roue (test de clic, capture, premier plan) pour enquêter sur les gels.
    public static bool TraceInput => Arguments.Contains("--trace-input");
    public static bool MeasureLayoutDrag => Arguments.Contains("--measure-layout-drag");
    public static bool LayoutDragCaptures => Arguments.Contains("--preview-layout-drag-captures");
    public static bool LayoutDragHub => Arguments.Contains("--preview-layout-drag-hub");
    public static bool LayoutDragWholeWheel => Arguments.Contains("--preview-layout-drag-whole");
    public static bool ViewportChecks => Arguments.Contains("--preview-viewport-checks");
    public static bool LayoutChecks => Arguments.Contains("--preview-layout-checks") || ViewportChecks;
    public static bool MeasureBaseline => Arguments.Contains("--measure-baseline");
    public static bool MeasureWheel => Arguments.Contains("--measure-wheel");
    public static bool MeasureNoShadows => Arguments.Contains("--measure-no-shadows");
    public static bool MeasureReopen => Arguments.Contains("--measure-reopen") || ReopenCountArgument is not null;
    public static bool MeasureIdle => Arguments.Contains("--measure-idle");
    // --pretend-version=0.9.0 : l'appli se croit dans cette version (essai de la mise à jour).
    public static string? PretendVersion => Arguments.FirstOrDefault(argument =>
        argument.StartsWith("--pretend-version=", StringComparison.OrdinalIgnoreCase))?.Split('=', 2)[1];
    public static bool TraceFrames => Arguments.Contains("--trace-frames");
    public static bool TestEscape => Arguments.Contains("--test-escape");
    public static bool Preview => Arguments.Contains("--preview");
    public static bool PreviewGroup => Arguments.Contains("--preview-group") || PreviewGroupIndexArgument is not null;
    public static int PreviewGroupIndex => PreviewGroupIndexArgument is not null &&
        int.TryParse(PreviewGroupIndexArgument.Split('=', 2)[1], out var index) ? index : 0;
    public static bool PreviewGroupLeft => Arguments.Contains("--preview-group-left");
    public static bool PreviewFolded => Arguments.Contains("--preview-folded");
    public static bool MeasureGlass => Arguments.Contains("--measure-glass");
    public static bool DumpUiIds => Arguments.Contains("--dump-ui-ids");
}
