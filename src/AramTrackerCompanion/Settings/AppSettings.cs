namespace AramTrackerCompanion.Settings;

internal sealed class AppSettings
{
    public string ApiBaseUrl { get; set; } = AppInfo.DefaultApiBaseUrl;

    public bool SetupCompleted { get; set; }

    public bool TrackingEnabled { get; set; } = true;

    public bool CloseToTray { get; set; } = true;

    public long? InstallationId { get; set; }

    public string? InstallationName { get; set; }

    public List<string> LinkedAccounts { get; set; } = [];

    public List<string> ProcessedMatchIds { get; set; } = [];

    public string? LastTrackedMatchId { get; set; }

    public DateTimeOffset? LastTrackedAt { get; set; }

    public DateTimeOffset? LastUploadAt { get; set; }

    public string? LastUploadProblem { get; set; }
}
