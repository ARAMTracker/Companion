using System.Text;
using AramTrackerCompanion.Lcu;

namespace AramTrackerCompanion.Tests;

public class LeagueClientTests
{
    [Fact]
    public void Reads_the_port_and_password_from_a_lockfile()
    {
        var connection = LeagueClientLocator.ParseLockfile("LeagueClient:4242:60182:not-a-real-password:https\n");

        Assert.Equal(new LeagueClientConnection(4242, 60182, "not-a-real-password"), connection);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("LeagueClient:4242:60182:password")]
    [InlineData("LeagueClient:4242:notaport:password:https")]
    [InlineData("LeagueClient:4242:0:password:https")]
    [InlineData("LeagueClient:4242:60182::https")]
    [InlineData("LeagueClient:4242:60182:password:http")]
    public void Rejects_a_lockfile_it_does_not_understand(string? contents)
    {
        Assert.Null(LeagueClientLocator.ParseLockfile(contents));
    }

    [Theory]
    [InlineData("""[8,"OnJsonApiEvent_lol-gameflow_v1_gameflow-phase",{"data":"EndOfGame","eventType":"Update","uri":"/lol-gameflow/v1/gameflow-phase"}]""", "EndOfGame")]
    [InlineData("""[8,"OnJsonApiEvent_lol-gameflow_v1_gameflow-phase",{"data":null,"eventType":"Delete","uri":"/lol-gameflow/v1/gameflow-phase"}]""", null)]
    [InlineData("""[3,"something else"]""", null)]
    [InlineData("""{"not":"an array"}""", null)]
    [InlineData("not json", null)]
    [InlineData("", null)]
    public void Reads_the_phase_from_an_event_frame(string frame, string? expected)
    {
        Assert.Equal(expected, LeagueClientService.ReadPhase(Encoding.UTF8.GetBytes(frame)));
    }
}
