using DualBluetooth.Audio;
using DualBluetooth.Models;

namespace DualBluetooth.Tests;

public sealed class ReconnectPlannerTests
{
    private static AudioDeviceInfo Device(string id, string name, bool active = true, bool isDefault = false) =>
        new(id, name, isDefault, active, false, active ? "Active" : "Disconnected");

    [Fact]
    public void Ready_WhenBothSavedOutputsAreActive()
    {
        var plan = ReconnectPlanner.Plan("a", "b", [Device("a", "Left buds"), Device("b", "Right buds")]);
        Assert.Equal(ReconnectState.Ready, plan.State);
    }

    [Fact]
    public void Waiting_NamesTheOutputThatIsStillAway()
    {
        var plan = ReconnectPlanner.Plan("a", "b", [Device("a", "Left buds"), Device("b", "Right buds", active: false)]);
        Assert.Equal(ReconnectState.Waiting, plan.State);
        Assert.Contains("Right buds", plan.Message);
        Assert.DoesNotContain("Left buds", plan.Message);
    }

    [Fact]
    public void Waiting_WhenASavedOutputIsMissingFromTheList()
    {
        var plan = ReconnectPlanner.Plan("a", "b", [Device("a", "Left buds")]);
        Assert.Equal(ReconnectState.Waiting, plan.State);
        Assert.Contains("Output B", plan.Message);
    }

    [Fact]
    public void Waiting_NamesBothWhenBothAreAway()
    {
        var plan = ReconnectPlanner.Plan("a", "b", [Device("a", "Left buds", active: false), Device("b", "Right buds", active: false)]);
        Assert.Equal(ReconnectState.Waiting, plan.State);
        Assert.Contains("Left buds and Right buds", plan.Message);
    }

    [Fact]
    public void Ready_IgnoresWhichDeviceTheScreenHasFallenBackTo()
    {
        // The saved ids decide, not any default the lists might show: a third device being active changes nothing.
        var plan = ReconnectPlanner.Plan("a", "b", [Device("a", "Left buds"), Device("b", "Right buds"), Device("c", "Speakers", isDefault: true)]);
        Assert.Equal(ReconnectState.Ready, plan.State);
    }

    [Theory]
    [InlineData(null, "b")]
    [InlineData("a", null)]
    [InlineData("", "b")]
    [InlineData("a", " ")]
    [InlineData("same", "same")]
    public void NotConfigured_WhenAnOutputIsUnsetOrBothAreTheSame(string? a, string? b)
    {
        var plan = ReconnectPlanner.Plan(a, b, [Device("a", "Left buds"), Device("b", "Right buds"), Device("same", "Same")]);
        Assert.Equal(ReconnectState.NotConfigured, plan.State);
    }
}
