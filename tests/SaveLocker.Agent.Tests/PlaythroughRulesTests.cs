using SaveLocker.Shared;
using Xunit;

namespace SaveLocker.Agent.Tests;

/// <summary>The pure rules of playthroughs (tasks/save-playthroughs): what a name may be, and which agents can run a switch.</summary>
public sealed class PlaythroughRulesTests
{
    [Theory]
    [InlineData("Main", "Main")]
    [InlineData("  Hard mode  ", "Hard mode")]
    [InlineData("Nuzlocke  run 2", "Nuzlocke  run 2")]
    public void A_name_is_trimmed_and_otherwise_kept(string given, string stored) =>
        Assert.Equal(stored, Playthroughs.CleanName(given));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("two\nlines")]
    public void An_empty_or_control_character_name_is_refused(string? given) =>
        Assert.Null(Playthroughs.CleanName(given));

    [Fact]
    public void A_name_is_at_most_sixty_characters()
    {
        Assert.NotNull(Playthroughs.CleanName(new string('x', Playthroughs.MaxNameLength)));
        Assert.Null(Playthroughs.CleanName(new string('x', Playthroughs.MaxNameLength + 1)));
        // Measured after trimming: the spaces at the edges are not part of the name.
        Assert.NotNull(Playthroughs.CleanName("  " + new string('x', Playthroughs.MaxNameLength) + "  "));
    }

    [Theory]
    [InlineData("0.9.0", true)]
    [InlineData("0.9.1", true)]
    [InlineData("1.0.0", true)]
    [InlineData("v0.9.0", true)]
    [InlineData("0.9.0+12.abc1234", true)]
    [InlineData("9.9.9-ci", true)]
    [InlineData("0.8.1", false)]
    [InlineData("0.8.99", false)]
    [InlineData("dev", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Only_an_agent_at_or_past_the_minimum_can_switch(string? reported, bool can) =>
        Assert.Equal(can, Playthroughs.AgentCanSwitch(reported));
}
