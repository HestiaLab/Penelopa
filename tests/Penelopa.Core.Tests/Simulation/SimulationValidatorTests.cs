using Penelopa.Core.Primitives;
using Penelopa.Core.Simulation;
using Xunit;
using static Penelopa.Core.Tests.Simulation.SimulationTestHelpers;

namespace Penelopa.Core.Tests.Simulation;

public class SimulationValidatorTests
{
    [Fact]
    public void ValidateStructure_ValidConfig_HasNoIssues()
    {
        var rectangle = CreateRectangle();
        var config = Config(
            false,
            Track(rectangle, "PosX", Frame(0, 10), Frame(1, 20)));

        Assert.Empty(SimulationValidator.ValidateStructure(config));
    }

    [Fact]
    public void ValidateStructure_DuplicateTrackKey_ReportsIssue()
    {
        var rectangle = CreateRectangle();
        var first = Track(rectangle, "PosX", Frame(0, 10), Frame(1, 20));
        var second = Track(rectangle, "PosX", Frame(0, 30), Frame(1, 40));

        var issues = SimulationValidator.ValidateStructure(Config(false, first, second));

        Assert.Contains(issues, issue => issue.Message.Contains("already exists"));
    }

    [Theory]
    [InlineData(-1d)]
    [InlineData(3601d)]
    public void ValidateStructure_OutOfRangeTime_ReportsIssue(double time)
    {
        var track = TrackWith(Frame(time, 0));

        var issues = SimulationValidator.ValidateStructure(Config(false, track));

        Assert.Contains(issues, issue => issue.Message.Contains("between 0 and 3600"));
    }

    [Fact]
    public void ValidateStructure_NonFiniteTime_ReportsIssue()
    {
        var track = TrackWith(Frame(double.NaN, 0));

        var issues = SimulationValidator.ValidateStructure(Config(false, track));

        Assert.Contains(issues, issue => issue.Message.Contains("between 0 and 3600"));
    }

    [Fact]
    public void ValidateStructure_NonIncreasingTimes_ReportsIssue()
    {
        var tooClose = TrackWith(Frame(0, 0), Frame(0.0005, 1));
        var reversed = TrackWith(Frame(1, 0), Frame(0, 1));

        var closeIssues = SimulationValidator.ValidateStructure(Config(false, tooClose));
        var reversedIssues = SimulationValidator.ValidateStructure(Config(false, reversed));

        Assert.Contains(closeIssues, issue => issue.Message.Contains("increase by at least 1 ms"));
        Assert.Contains(reversedIssues, issue => issue.Message.Contains("increase by at least 1 ms"));
    }

    [Fact]
    public void ValidateStructure_InvalidValues_ReportIssues()
    {
        var nonFinite = TrackWith(Frame(0, float.NaN));
        var tooLarge = TrackWith(Frame(0, 2e7f));

        var nonFiniteIssues = SimulationValidator.ValidateStructure(Config(false, nonFinite));
        var largeIssues = SimulationValidator.ValidateStructure(Config(false, tooLarge));

        Assert.Contains(nonFiniteIssues, issue => issue.Message.Contains("finite number"));
        Assert.Contains(largeIssues, issue => issue.Message.Contains("magnitude"));
    }

    [Fact]
    public void ValidateStructure_InvalidBezier_ReportsIssue()
    {
        var outside = TrackWith(
            Frame(
                0,
                0,
                InterpolationKind.CubicBezier,
                new CubicBezier(1.1d, 0d, 0d, 1d)));
        var nonFinite = TrackWith(
            Frame(
                0,
                0,
                InterpolationKind.CubicBezier,
                new CubicBezier(0d, double.NaN, 0d, 1d)));

        var outsideIssues = SimulationValidator.ValidateStructure(Config(false, outside));
        var nonFiniteIssues = SimulationValidator.ValidateStructure(Config(false, nonFinite));

        Assert.Contains(outsideIssues, issue => issue.Message.Contains("control points"));
        Assert.Contains(nonFiniteIssues, issue => issue.Message.Contains("control points"));
    }

    [Fact]
    public void ValidateStructure_UnknownInterpolation_ReportsIssue()
    {
        var track = TrackWith(Frame(0, 0, (InterpolationKind)999));

        var issues = SimulationValidator.ValidateStructure(Config(false, track));

        Assert.Contains(issues, issue => issue.Message.Contains("Unknown interpolation"));
    }

    [Fact]
    public void ValidateStructure_EmptyMetadata_ReportsIssues()
    {
        var track = new SimulationTrack(Guid.Empty, " ", new[] { Frame(0, 0) });

        var issues = SimulationValidator.ValidateStructure(Config(false, track));

        Assert.Contains(issues, issue => issue.Message.Contains("primitive id"));
        Assert.Contains(issues, issue => issue.Message.Contains("property name"));
    }

    [Fact]
    public void ValidateStructure_TrackCountLimit_ReportsIssue()
    {
        var tracks = Enumerable.Range(0, SimulationLimits.MaxTracks + 1)
            .Select(_ => new SimulationTrack(
                Guid.NewGuid(),
                "PosX",
                new[] { Frame(0, 0) }))
            .ToArray();

        var issues = SimulationValidator.ValidateStructure(Config(false, tracks));

        Assert.Contains(
            issues,
            issue => issue.Message.Contains("at most")
                && issue.Message.Contains("tracks"));
    }

    [Fact]
    public void ValidateStructure_KeyframeLimitPerTrack_ReportsIssue()
    {
        var keyframes = Enumerable.Range(0, SimulationLimits.MaxKeyframesPerTrack + 1)
            .Select(index => Frame(index, index))
            .ToArray();
        var track = new SimulationTrack(Guid.NewGuid(), "PosX", keyframes);

        var issues = SimulationValidator.ValidateStructure(Config(false, track));

        Assert.Contains(
            issues,
            issue => issue.Message.Contains("at most")
                && issue.Message.Contains("keyframes"));
    }

    [Fact]
    public void ValidateStructure_TotalKeyframeLimit_ReportsIssue()
    {
        var tracks = Enumerable.Range(0, 51)
            .Select(_ => new SimulationTrack(
                Guid.NewGuid(),
                "PosX",
                Enumerable.Range(0, SimulationLimits.MaxKeyframesPerTrack)
                    .Select(index => Frame(index, index))
                    .ToArray()))
            .ToArray();

        var issues = SimulationValidator.ValidateStructure(Config(false, tracks));

        Assert.Contains(issues, issue => issue.Message.Contains("10000"));
    }

    [Fact]
    public void ValidateValues_ReportsPropertyConstraintViolation()
    {
        var circle = new Circle();
        var track = Track(circle, "Radius", Frame(0, 5), Frame(1, -1));
        var index = SimulationPrimitiveIndex.Create(new Primitive[] { circle });

        var issues = SimulationValidator.Validate(Config(false, track), index);

        var issue = Assert.Single(issues);
        Assert.Equal(SimulationIssueKind.Value, issue.Kind);
        Assert.Equal(1, issue.KeyframeIndex!.Value);
        Assert.Contains("greater than", issue.Message);
    }

    [Fact]
    public void ValidateValues_AcceptsValidValues()
    {
        var circle = new Circle();
        var track = Track(circle, "Radius", Frame(0, 5), Frame(1, 10));
        var index = SimulationPrimitiveIndex.Create(new Primitive[] { circle });

        Assert.Empty(SimulationValidator.Validate(Config(false, track), index));
    }

    [Fact]
    public void Validate_DormantTrackDoesNotProduceErrors()
    {
        var rectangle = CreateRectangle();
        var dormant = new SimulationTrack(
            Guid.NewGuid(),
            "PosX",
            new[] { Frame(0, 0), Frame(1, 1) });
        var index = SimulationPrimitiveIndex.Create(new Primitive[] { rectangle });

        Assert.Empty(SimulationValidator.Validate(Config(false, dormant), index));
    }

    [Fact]
    public void Validate_RejectsNullArguments()
    {
        var rectangle = CreateRectangle();
        var index = SimulationPrimitiveIndex.Create(new Primitive[] { rectangle });

        Assert.Throws<ArgumentNullException>(() =>
            SimulationValidator.ValidateStructure(null!));
        Assert.Throws<ArgumentNullException>(() =>
            SimulationValidator.Validate(null!, index));
        Assert.Throws<ArgumentNullException>(() =>
            SimulationValidator.Validate(Config(), null!));
        Assert.Throws<ArgumentNullException>(() =>
            SimulationValidator.ValidateValues(null!));
    }

    [Fact]
    public void TryValidateKeyframeValue_ReportsConstraints()
    {
        var rectangle = CreateRectangle();

        Assert.True(SimulationValidator.TryValidateKeyframeValue(
            rectangle.PosX,
            10f,
            out var error));
        Assert.Null(error);

        Assert.False(SimulationValidator.TryValidateKeyframeValue(
            rectangle.Width,
            0f,
            out error));
        Assert.NotNull(error);

        Assert.False(SimulationValidator.TryValidateKeyframeValue(
            rectangle.PosX,
            float.NaN,
            out error));
        Assert.NotNull(error);

        Assert.False(SimulationValidator.TryValidateKeyframeValue(
            rectangle.PosX,
            2e7f,
            out error));
        Assert.NotNull(error);

        Assert.Throws<ArgumentNullException>(() =>
            SimulationValidator.TryValidateKeyframeValue(null!, 0f, out _));
    }

    private static SimulationTrack TrackWith(params SimulationKeyframe[] keyframes)
        => new(Guid.NewGuid(), "Value", keyframes);
}
