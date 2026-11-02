using Penelopa.Core.Simulation;
using Xunit;

namespace Penelopa.Core.Tests.Simulation;

public class RecordSemanticsTests
{
    [Fact]
    public void SimulationTrack_ExposesIdentityAndValueSemantics()
    {
        var id = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var track = new SimulationTrack(
            id,
            "PosX",
            new[] { new SimulationKeyframe(0d, 1f, InterpolationKind.Linear) });
        var key = new SimulationTrackKey(id, "PosX");

        Assert.Equal(key, track.Key);
        Assert.Equal(id, track.PrimitiveId);
        Assert.Equal("PosX", track.PropertyName);
        Assert.True(track.Equals(track));
        Assert.Equal(track.GetHashCode(), track.GetHashCode());
        Assert.Contains("PosX", track.ToString());
        Assert.Equal(key, new SimulationTrackKey(id, "PosX"));
        Assert.NotEqual(key, new SimulationTrackKey(id, "PosY"));
    }

    [Fact]
    public void SimulationReadout_EqualityDeconstructionAndFormatting()
    {
        var values = new Dictionary<SimulationTrackKey, float>
        {
            [new SimulationTrackKey(Guid.Empty, "PosX")] = 3f,
        };
        var readout = new SimulationReadout(
            SimulationState.Paused,
            1.5d,
            4d,
            true,
            values);

        Assert.True(readout.Equals(readout));
        Assert.Equal(readout.GetHashCode(), readout.GetHashCode());
        Assert.Contains("Paused", readout.ToString());
        var (state, time, duration, loop, readoutValues) = readout;
        Assert.Equal(SimulationState.Paused, state);
        Assert.Equal(1.5d, time);
        Assert.Equal(4d, duration);
        Assert.True(loop);
        Assert.Same(values, readoutValues);
    }

    [Fact]
    public void SimulationValidationIssue_EqualityDeconstructionAndFormatting()
    {
        var id = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var issue = new SimulationValidationIssue(
            SimulationIssueKind.Value,
            id,
            "PosX",
            2,
            "Invalid");
        var same = new SimulationValidationIssue(
            SimulationIssueKind.Value,
            id,
            "PosX",
            2,
            "Invalid");

        Assert.Equal(same, issue);
        Assert.Equal(same.GetHashCode(), issue.GetHashCode());
        Assert.Contains("Invalid", issue.ToString());
        var (kind, primitiveId, propertyName, keyframeIndex, message) = issue;
        Assert.Equal(SimulationIssueKind.Value, kind);
        Assert.Equal(id, primitiveId);
        Assert.Equal("PosX", propertyName);
        Assert.Equal(2, keyframeIndex);
        Assert.Equal("Invalid", message);
    }
}
