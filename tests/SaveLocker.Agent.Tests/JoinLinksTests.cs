using SaveLocker.Shared;
using Xunit;

namespace SaveLocker.Agent.Tests;

/// <summary>
/// The Linux side doors of a join link: the <c>savelocker://</c> link an installed agent opens, and
/// the <c>.desktop</c> launcher that runs the one-liner. Any web page can fire the link, so the parser
/// must let nothing but a join link through; the launcher is pasted into a shell, so it must refuse a
/// URL that would need quoting rather than quote it wrong.
/// </summary>
public sealed class JoinLinksTests
{
    private const string Join = "http://192.168.1.20:5080/join/K7QM4H2X";

    [Fact]
    public void TheAgentLink_RoundTrips()
    {
        var link = JoinLinks.AgentLink(Join);
        Assert.Equal("savelocker://enroll?link=http%3A%2F%2F192.168.1.20%3A5080%2Fjoin%2FK7QM4H2X", link);
        Assert.Equal(Join, JoinLinks.JoinUrlFromAgentLink(link));
    }

    [Fact]
    public void ACodeTypedLoosely_ComesBackCanonical_AndAPathPrefixSurvives()
    {
        var link = JoinLinks.AgentLink("https://saves.example.com/sl/join/k7qm-4h2x");
        Assert.Equal("https://saves.example.com/sl/join/K7QM4H2X", JoinLinks.JoinUrlFromAgentLink(link));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("savelocker://enroll")]
    [InlineData("savelocker://settings?link=http%3A%2F%2Fh%2Fjoin%2FK7QM4H2X")]
    [InlineData("https://enroll?link=http%3A%2F%2Fh%2Fjoin%2FK7QM4H2X")]
    [InlineData("savelocker://enroll?link=file%3A%2F%2F%2Fjoin%2FK7QM4H2X")]
    [InlineData("savelocker://enroll?link=javascript%3Aalert(1)")]
    [InlineData("savelocker://enroll?link=http%3A%2F%2Fh%2Fpolicy%2FK7QM4H2X")]
    [InlineData("savelocker://enroll?link=http%3A%2F%2Fh%2Fjoin%2FUUUUUUUU")]
    [InlineData("savelocker://enroll?link=http%3A%2F%2Fh%2Fjoin%2FK7QM4H2X%3Fx%3D1")]
    [InlineData("savelocker://enroll?link=http%3A%2F%2Fh%2Fjoin%2FK7QM4H2X%23frag")]
    public void AnythingButAJoinLink_IsRefused(string? link) =>
        Assert.Null(JoinLinks.JoinUrlFromAgentLink(link));

    [Fact]
    public void TheLauncher_RunsTheOneLinerInATerminal()
    {
        var text = JoinLinks.InstallLauncher(Join, "192.168.1.20:5080")!;
        Assert.StartsWith("[Desktop Entry]\n", text);
        Assert.Contains("\nTerminal=true\n", text);
        Assert.Contains("\nType=Application\n", text);
        Assert.Contains($"curl -fsS -o install.sh {Join}/install.sh; then sh install.sh; else curl -sS {Join}/install.sh; fi", text);
        Assert.Contains("\nExec=sh -c '", text);
        Assert.EndsWith("read reply'\n", text);
        Assert.DoesNotContain("\r", text);
        // One single-quoted argument: a quote inside it would end the script early.
        var exec = text.Split('\n').Single(l => l.StartsWith("Exec="));
        Assert.Equal(2, exec.Count(c => c == '\''));
    }

    [Fact]
    public void APercentInTheUrl_IsDoubled_AsTheExecKeyRequires() =>
        Assert.Contains("http://h/a%%20b/join/K7QM4H2X/install.sh",
            JoinLinks.InstallLauncher("http://h/a%20b/join/K7QM4H2X", "h"));

    [Theory]
    [InlineData("http://h/join/K7QM4H2X'; rm -rf ~; '")]
    [InlineData("http://h/join/K7QM4H2X$(id)")]
    [InlineData("http://h/a b/join/K7QM4H2X")]
    [InlineData("http://h/join/K7QM4H2X\nExec=evil")]
    [InlineData("http://h/join/K7QM4H2X`id`")]
    public void AUrlThatWouldNeedQuoting_GetsNoLauncher(string joinUrl) =>
        Assert.Null(JoinLinks.InstallLauncher(joinUrl, "h"));

    [Fact]
    public void TheServerName_CannotAddALine() =>
        Assert.DoesNotContain("\nExec=evil",
            JoinLinks.InstallLauncher(Join, "h\nExec=evil"));
}
