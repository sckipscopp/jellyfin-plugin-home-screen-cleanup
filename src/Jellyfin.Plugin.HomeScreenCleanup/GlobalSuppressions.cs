using System.Diagnostics.CodeAnalysis;

[assembly: SuppressMessage(
    "Globalization",
    "CA1303:Do not pass literals as localized parameters",
    Justification = "Server-side plugin: exception, problem-detail and log text is not localized (same as Jellyfin itself).")]
