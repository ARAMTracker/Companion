using System.Globalization;
using System.Text.Json;
using AramTrackerCompanion.Api;

namespace AramTrackerCompanion.Tracking;

internal enum MapOutcome
{
    Mapped,
    NotMayhem,
    Incomplete,
    Unusable,
}

internal readonly record struct MapResult(MapOutcome Outcome, MatchSubmission? Match = null, string? Reason = null);

internal static class MatchMapper
{
    public const int MayhemQueueId = 2400;

    private const string CompletedResult = "GameComplete";

    // Website limits.
    private const int AugmentSlots = 8;
    private const int ItemSlots = 7;
    public const int MaxExtraStats = 64;

    private static readonly (string Source, string Target)[] ExtraStats =
    [
        // Website columns.
        ("doubleKills", "double_kills"),
        ("tripleKills", "triple_kills"),
        ("quadraKills", "quadra_kills"),
        ("pentaKills", "penta_kills"),
        ("largestKillingSpree", "largest_killing_spree"),
        ("killingSprees", "killing_sprees"),
        ("longestTimeSpentLiving", "longest_time_spent_living"),
        ("totalHeal", "total_heal"),
        ("timeCCingOthers", "time_ccing_others"),
        ("totalTimeCrowdControlDealt", "total_time_crowd_control_dealt"),
        ("totalDamageDealt", "total_damage_dealt"),
        ("physicalDamageDealtToChampions", "physical_damage_to_champions"),
        ("magicDamageDealtToChampions", "magic_damage_to_champions"),
        ("trueDamageDealtToChampions", "true_damage_to_champions"),

        // Shown on the match page.
        ("largestMultiKill", "largest_multi_kill"),
        ("unrealKills", "unreal_kills"),
        ("largestCriticalStrike", "largest_critical_strike"),
        ("totalMinionsKilled", "minions_killed"),
        ("neutralMinionsKilled", "neutral_minions_killed"),
        ("goldSpent", "gold_spent"),
        ("totalUnitsHealed", "units_healed"),
        ("damageSelfMitigated", "damage_self_mitigated"),
        ("physicalDamageDealt", "physical_damage_dealt"),
        ("magicDamageDealt", "magic_damage_dealt"),
        ("trueDamageDealt", "true_damage_dealt"),
        ("physicalDamageTaken", "physical_damage_taken"),
        ("magicalDamageTaken", "magic_damage_taken"),
        ("trueDamageTaken", "true_damage_taken"),
        ("damageDealtToTurrets", "damage_to_turrets"),
        ("damageDealtToObjectives", "damage_to_objectives"),
        ("turretKills", "turret_kills"),
        ("inhibitorKills", "inhibitor_kills"),
        ("visionScore", "vision_score"),
        ("wardsPlaced", "wards_placed"),
        ("wardsKilled", "wards_killed"),
        ("visionWardsBoughtInGame", "control_wards_bought"),
    ];

    private static readonly (string Source, string Target)[] ExtraFlags =
    [
        ("firstBloodKill", "first_blood"),
        ("firstTowerKill", "first_tower"),
        ("firstBloodAssist", "first_blood_assist"),
        ("firstTowerAssist", "first_tower_assist"),
        ("firstInhibitorKill", "first_inhibitor"),
        ("firstInhibitorAssist", "first_inhibitor_assist"),
    ];

    public static bool IsMayhem(JsonElement game) => Long(game, "queueId") == MayhemQueueId;

    public static string? MatchId(JsonElement game)
    {
        var platform = String(game, "platformId");
        var gameId = Long(game, "gameId");

        return string.IsNullOrEmpty(platform) || gameId is null or <= 0
            ? null
            : $"{platform.ToUpperInvariant()}_{gameId.Value.ToString(CultureInfo.InvariantCulture)}";
    }

    public static MapResult Map(JsonElement game)
    {
        if (game.ValueKind != JsonValueKind.Object)
        {
            return new(MapOutcome.Incomplete, Reason: "the game is not an object");
        }

        if (!IsMayhem(game))
        {
            return new(MapOutcome.NotMayhem);
        }

        if (String(game, "endOfGameResult") is { } result && result != CompletedResult)
        {
            return new(MapOutcome.Unusable, Reason: $"the game ended as {result}");
        }

        var matchId = MatchId(game);
        var start = Long(game, "gameCreation");
        var duration = Long(game, "gameDuration");

        if (matchId is null || start is null or <= 0 || duration is null or <= 0)
        {
            return new(MapOutcome.Incomplete, Reason: "the id, start or duration is missing");
        }

        if (duration > 7200)
        {
            return new(MapOutcome.Unusable, Reason: "the game is longer than two hours");
        }

        var winningTeam = WinningTeam(game);
        var identities = Identities(game);
        var participants = new List<ParticipantSubmission>();

        foreach (var participant in Array(game, "participants"))
        {
            var id = Long(participant, "participantId");
            if (id is null || !identities.TryGetValue(id.Value, out var player))
            {
                return new(MapOutcome.Incomplete, Reason: "a player has no identity");
            }

            var mapped = MapParticipant(participant, player, winningTeam, out var reason, out var unusable);
            if (mapped is null)
            {
                return new(unusable ? MapOutcome.Unusable : MapOutcome.Incomplete, Reason: reason);
            }

            participants.Add(mapped);
        }

        if (participants.Count is < 2 or > 10)
        {
            return new(MapOutcome.Incomplete, Reason: $"the game has {participants.Count} players");
        }

        return new(MapOutcome.Mapped, new MatchSubmission
        {
            MatchId = matchId,
            QueueId = MayhemQueueId,
            GameVersion = String(game, "gameVersion"),
            GameStart = start.Value,
            GameDuration = (int)duration.Value,
            WinningTeam = winningTeam,
            Participants = participants,
        });
    }

    private static ParticipantSubmission? MapParticipant(JsonElement participant, JsonElement player, int? winningTeam, out string? reason, out bool unusable)
    {
        reason = null;
        unusable = false;

        if (!participant.TryGetProperty("stats", out var stats) || stats.ValueKind != JsonValueKind.Object)
        {
            reason = "a player has no stats";
            return null;
        }

        var puuid = String(player, "puuid");
        if (string.IsNullOrEmpty(puuid) || (Guid.TryParse(puuid, out var guid) && guid == Guid.Empty))
        {
            reason = "a player has no player id, which is how bots appear";
            unusable = true;
            return null;
        }

        var gameName = String(player, "gameName");
        if (string.IsNullOrEmpty(gameName))
        {
            gameName = String(player, "summonerName");
        }

        var teamId = Long(participant, "teamId");
        var championId = Long(participant, "championId");
        var kills = Long(stats, "kills");
        var deaths = Long(stats, "deaths");
        var assists = Long(stats, "assists");
        var win = Bool(stats, "win") ?? (winningTeam is null ? (bool?)null : teamId == winningTeam);

        if (string.IsNullOrEmpty(gameName) || teamId is not (100 or 200) || championId is null or <= 0
            || kills is null || deaths is null || assists is null || win is null)
        {
            reason = "a player is missing a required field";
            return null;
        }

        var items = Slots(stats, "item", 0, ItemSlots, keepEmpty: true);
        var augments = Slots(stats, "playerAugment", 1, AugmentSlots, keepEmpty: false);
        var extras = new Dictionary<string, long>();

        foreach (var (source, target) in ExtraStats)
        {
            if (Long(stats, source) is { } value)
            {
                extras[target] = value;
            }
        }

        foreach (var (source, target) in ExtraFlags)
        {
            if (Bool(stats, source) is { } flag)
            {
                extras[target] = flag ? 1 : 0;
            }
        }

        var tagLine = String(player, "tagLine");

        return new ParticipantSubmission
        {
            Puuid = puuid,
            GameName = gameName,
            TagLine = string.IsNullOrEmpty(tagLine) ? null : tagLine,
            TeamId = (int)teamId.Value,
            ChampionId = (int)championId.Value,
            Win = win.Value,
            Kills = (int)kills.Value,
            Deaths = (int)deaths.Value,
            Assists = (int)assists.Value,
            ChampionLevel = (int?)Long(stats, "champLevel"),
            GoldEarned = Long(stats, "goldEarned"),
            DamageToChampions = Long(stats, "totalDamageDealtToChampions"),
            DamageTaken = Long(stats, "totalDamageTaken"),
            Items = items.Count == 0 ? null : items,
            Spell1Id = (int?)Long(participant, "spell1Id"),
            Spell2Id = (int?)Long(participant, "spell2Id"),
            Augments = augments.Count == 0 ? null : augments,
            Stats = extras.Count == 0 ? null : extras,
        };
    }

    private static int? WinningTeam(JsonElement game)
    {
        foreach (var team in Array(game, "teams"))
        {
            if (String(team, "win") == "Win" && Long(team, "teamId") is 100 or 200)
            {
                return (int)Long(team, "teamId")!.Value;
            }
        }

        return null;
    }

    private static Dictionary<long, JsonElement> Identities(JsonElement game)
    {
        var identities = new Dictionary<long, JsonElement>();

        foreach (var identity in Array(game, "participantIdentities"))
        {
            if (Long(identity, "participantId") is { } id
                && identity.TryGetProperty("player", out var player)
                && player.ValueKind == JsonValueKind.Object)
            {
                identities[id] = player;
            }
        }

        return identities;
    }

    private static List<long> Slots(JsonElement stats, string prefix, int first, int count, bool keepEmpty)
    {
        var values = new List<long>();

        for (var slot = first; slot < first + count; slot++)
        {
            var value = Long(stats, prefix + slot.ToString(CultureInfo.InvariantCulture));

            if (value is > 0 || (keepEmpty && value == 0))
            {
                values.Add(value.Value);
            }
        }

        return values.Any(v => v > 0) ? values : [];
    }

    private static IEnumerable<JsonElement> Array(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray()
            : [];

    private static string? String(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static long? Long(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number) && number >= 0
            ? number
            : null;

    private static bool? Bool(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : null;
}
