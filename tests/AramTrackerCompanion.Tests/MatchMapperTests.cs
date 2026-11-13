using System.Text.Json;
using AramTrackerCompanion.Api;
using AramTrackerCompanion.Tracking;

namespace AramTrackerCompanion.Tests;

public class MatchMapperTests
{
    [Fact]
    public void Maps_a_finished_mayhem_game()
    {
        var result = MatchMapper.Map(Fixtures.MayhemGame());

        Assert.Equal(MapOutcome.Mapped, result.Outcome);
        var match = result.Match!;
        Assert.Equal("EUW1_7000000001", match.MatchId);
        Assert.Equal(2400, match.QueueId);
        Assert.Equal("16.22.834.8910", match.GameVersion);
        Assert.Equal(1794420915679, match.GameStart);
        Assert.Equal(1388, match.GameDuration);
        Assert.Equal(100, match.WinningTeam);
        Assert.Equal(10, match.Participants.Count);

        var first = match.Participants[0];
        Assert.Equal("00000000-0000-4000-8000-000000000001", first.Puuid);
        Assert.Equal("Player 1", first.GameName);
        Assert.Equal("TEST", first.TagLine);
        Assert.Equal(100, first.TeamId);
        Assert.Equal(777, first.ChampionId);
        Assert.True(first.Win);
        Assert.Equal((21, 19, 23), (first.Kills, first.Deaths, first.Assists));
        Assert.Equal(18, first.ChampionLevel);
        Assert.Equal(25136, first.GoldEarned);
        Assert.Equal(127146, first.DamageToChampions);
        Assert.Equal(57510, first.DamageTaken);
        Assert.Equal([3031, 3302, 3153, 6333, 6665, 3008, 220013], first.Items);
        Assert.Equal((6, 4), (first.Spell1Id, first.Spell2Id));
        Assert.Equal([2010, 1092, 1225, 1402, 1058], first.Augments);
        Assert.Equal(2, first.Stats!["double_kills"]);

        Assert.All(match.Participants, p => Assert.Equal(p.TeamId == 100, p.Win));
    }

    [Fact]
    public void Serialises_to_the_website_contract()
    {
        var match = MatchMapper.Map(Fixtures.MayhemGame()).Match!;

        using var document = JsonDocument.Parse(JsonSerializer.Serialize(new SubmitRequest([match]), AramTrackerApiService.Json));
        var sent = document.RootElement.GetProperty("matches")[0];
        var player = sent.GetProperty("participants")[0];

        Assert.Equal("EUW1_7000000001", sent.GetProperty("match_id").GetString());
        Assert.Equal(JsonValueKind.Number, sent.GetProperty("game_start").ValueKind);
        Assert.Equal(1388, sent.GetProperty("game_duration").GetInt32());
        Assert.Equal(100, sent.GetProperty("winning_team").GetInt32());
        Assert.Equal(777, player.GetProperty("champion_id").GetInt32());
        Assert.Equal(127146, player.GetProperty("damage_to_champions").GetInt64());
        Assert.Equal(6, player.GetProperty("spell1_id").GetInt32());
        Assert.Equal(2, player.GetProperty("stats").GetProperty("double_kills").GetInt32());
        Assert.False(player.TryGetProperty("heal_shield_allies", out _));
        Assert.True(player.GetProperty("stats").EnumerateObject().Count() <= MatchMapper.MaxExtraStats);
    }

    [Fact]
    public void Extra_stats_use_the_website_column_names()
    {
        var stats = MatchMapper.Map(Fixtures.MayhemGame()).Match!.Participants[0].Stats!;

        // Website columns.
        Assert.Equal(2, stats["double_kills"]);
        Assert.Equal(0, stats["penta_kills"]);
        Assert.Equal(15322, stats["total_heal"]);
        Assert.Equal(54, stats["time_ccing_others"]);
        Assert.Equal(113, stats["total_time_crowd_control_dealt"]);
        Assert.Equal(274670, stats["total_damage_dealt"]);
        Assert.Equal(103213, stats["physical_damage_to_champions"]);
        Assert.Equal(0, stats["first_blood"]);
        Assert.Equal(0, stats["first_tower"]);

        // Shown on the match page.
        Assert.Equal(1, stats["first_blood_assist"]);
        Assert.Equal(100, stats["minions_killed"]);
        Assert.Equal(231244, stats["physical_damage_dealt"]);
        Assert.Equal(38447, stats["magic_damage_dealt"]);
        Assert.Equal(0, stats["vision_score"]);

        Assert.False(stats.ContainsKey("crowd_control_time_dealt"));
        Assert.All(stats.Keys, key => Assert.Matches("^[a-z][a-z0-9_]{0,47}$", key));
    }

    [Fact]
    public void A_flag_the_client_did_not_send_is_left_out()
    {
        var game = Fixtures.MayhemGameNode();
        game["participants"]![0]!["stats"]!.AsObject().Remove("firstBloodKill");

        var stats = MatchMapper.Map(Fixtures.ToElement(game)).Match!.Participants[0].Stats!;

        Assert.False(stats.ContainsKey("first_blood"));
    }

    [Fact]
    public void Reads_up_to_eight_augments()
    {
        var game = Fixtures.MayhemGameNode();
        var stats = game["participants"]![0]!["stats"]!.AsObject();
        stats["playerAugment6"] = 1111;
        stats["playerAugment7"] = 2222;
        stats["playerAugment8"] = 3333;
        stats["playerAugment9"] = 4444;

        var player = MatchMapper.Map(Fixtures.ToElement(game)).Match!.Participants[0];

        Assert.Equal([2010, 1092, 1225, 1402, 1058, 1111, 2222, 3333], player.Augments);
    }

    [Theory]
    [InlineData(450)]
    [InlineData(2300)]
    [InlineData(420)]
    public void Other_queues_are_not_mayhem(int queueId)
    {
        var game = Fixtures.MayhemGameNode();
        game["queueId"] = queueId;

        Assert.Equal(MapOutcome.NotMayhem, MatchMapper.Map(Fixtures.ToElement(game)).Outcome);
    }

    [Fact]
    public void A_game_that_did_not_complete_is_skipped_for_good()
    {
        var game = Fixtures.MayhemGameNode();
        game["endOfGameResult"] = "Abort_AntiCheatExit";

        Assert.Equal(MapOutcome.Unusable, MatchMapper.Map(Fixtures.ToElement(game)).Outcome);
    }

    [Fact]
    public void A_game_without_player_stats_is_tried_again_later()
    {
        var game = Fixtures.MayhemGameNode();
        game["participants"]![3]!.AsObject().Remove("stats");

        Assert.Equal(MapOutcome.Incomplete, MatchMapper.Map(Fixtures.ToElement(game)).Outcome);
    }

    [Fact]
    public void A_game_without_identities_is_tried_again_later()
    {
        var game = Fixtures.MayhemGameNode();
        game["participantIdentities"]!.AsArray().Clear();

        Assert.Equal(MapOutcome.Incomplete, MatchMapper.Map(Fixtures.ToElement(game)).Outcome);
    }

    [Fact]
    public void A_game_with_a_bot_is_skipped_for_good()
    {
        var game = Fixtures.MayhemGameNode();
        game["participantIdentities"]![9]!["player"]!["puuid"] = "00000000-0000-0000-0000-000000000000";

        Assert.Equal(MapOutcome.Unusable, MatchMapper.Map(Fixtures.ToElement(game)).Outcome);
    }

    [Fact]
    public void Missing_optional_fields_are_left_out()
    {
        var game = Fixtures.MayhemGameNode();
        var stats = game["participants"]![0]!["stats"]!.AsObject();
        stats.Remove("goldEarned");
        stats.Remove("champLevel");
        for (var slot = 1; slot <= 6; slot++)
        {
            stats.Remove($"playerAugment{slot}");
        }

        var player = MatchMapper.Map(Fixtures.ToElement(game)).Match!.Participants[0];

        Assert.Null(player.GoldEarned);
        Assert.Null(player.ChampionLevel);
        Assert.Null(player.Augments);
    }

    [Fact]
    public void The_winner_falls_back_to_the_team_result()
    {
        var game = Fixtures.MayhemGameNode();
        foreach (var participant in game["participants"]!.AsArray())
        {
            participant!["stats"]!.AsObject().Remove("win");
        }

        var match = MatchMapper.Map(Fixtures.ToElement(game)).Match!;

        Assert.All(match.Participants, p => Assert.Equal(p.TeamId == 100, p.Win));
    }

    [Fact]
    public void An_unexpected_shape_does_not_throw()
    {
        using var document = JsonDocument.Parse("""{"queueId": 2400, "participants": "nope", "teams": 5, "gameId": "x"}""");

        Assert.Equal(MapOutcome.Incomplete, MatchMapper.Map(document.RootElement).Outcome);
    }
}
