using System.Text.Json;

namespace AramTrackerCompanion.Api;

internal sealed record PairRequest(string Code, string Name, string AppVersion);

internal sealed record PairResponse(string Token, InstallationInfo Installation, List<LinkedPlayer>? Players);

internal sealed record MeResponse(InstallationInfo Installation, List<LinkedPlayer>? Players);

internal sealed record InstallationInfo(long Id, string Name);

internal sealed record LinkedPlayer(string? GameName, string? TagLine, string? Region)
{
    public string RiotId => string.IsNullOrEmpty(TagLine) ? GameName ?? "" : $"{GameName}#{TagLine}";
}

internal sealed record SubmitRequest(IReadOnlyList<MatchSubmission> Matches);

internal sealed record SubmitResponse(List<MatchResult>? Results);

internal sealed record MatchResult(string? MatchId, string? Status, string? Message, JsonElement? Errors, string? Code = null)
{
    public const string Accepted = "accepted";
    public const string Duplicate = "duplicate";
    public const string Rejected = "rejected";
    public const string Retry = "retry";

    public const string NotYourGame = "not_your_game";
}

internal sealed class MatchSubmission
{
    public required string MatchId { get; init; }
    public int QueueId { get; init; }
    public string? GameVersion { get; init; }
    public long GameStart { get; init; }
    public int GameDuration { get; init; }
    public int? WinningTeam { get; init; }
    public required List<ParticipantSubmission> Participants { get; init; }
}

internal sealed class ParticipantSubmission
{
    public required string Puuid { get; init; }
    public required string GameName { get; init; }
    public string? TagLine { get; init; }
    public int TeamId { get; init; }
    public int ChampionId { get; init; }
    public bool Win { get; init; }
    public int Kills { get; init; }
    public int Deaths { get; init; }
    public int Assists { get; init; }
    public int? ChampionLevel { get; init; }
    public long? GoldEarned { get; init; }
    public long? DamageToChampions { get; init; }
    public long? DamageTaken { get; init; }
    public List<long>? Items { get; init; }
    public int? Spell1Id { get; init; }
    public int? Spell2Id { get; init; }
    public List<long>? Augments { get; init; }
    public Dictionary<string, long>? Stats { get; init; }
}
