using Penelopa.Core.Primitives;
using Penelopa.Core.Services;
using Xunit;

namespace Penelopa.Core.Tests;

public class CoalescingBoundaryTests
{
    [Fact]
    public void SelectionChange_EndsCoalescingWindow()
    {
        var clock = new ManualTimeProvider();
        var service = new PrimitiveService(clock);
        var circle = new Circle();
        var rectangle = new Rectangle();
        service.Add(circle);
        service.Add(rectangle);
        service.SetSelected(circle);

        Assert.True(service.TrySetProperty(circle, circle.Radius, "20", out var firstError), firstError);
        service.SetSelected(rectangle);
        Assert.True(service.TrySetProperty(circle, circle.Radius, "30", out var secondError), secondError);

        Assert.Equal(4, service.UndoDepth);
    }

    [Fact]
    public void EightHundredMillisecondBoundary_EndsCoalescingWindow()
    {
        var clock = new ManualTimeProvider();
        var service = new PrimitiveService(clock);
        var circle = new Circle();
        service.Add(circle);

        Assert.True(service.TrySetProperty(circle, circle.Radius, "20", out _));
        clock.Advance(TimeSpan.FromMilliseconds(801));
        Assert.True(service.TrySetProperty(circle, circle.Radius, "30", out _));

        Assert.Equal(3, service.UndoDepth);
    }

    [Fact]
    public void InsideWindow_StillMerges()
    {
        var clock = new ManualTimeProvider();
        var service = new PrimitiveService(clock);
        var circle = new Circle();
        service.Add(circle);

        Assert.True(service.TrySetProperty(circle, circle.Radius, "20", out _));
        clock.Advance(TimeSpan.FromMilliseconds(400));
        Assert.True(service.TrySetProperty(circle, circle.Radius, "30", out _));

        Assert.Equal(2, service.UndoDepth);
        service.Undo();
        Assert.Equal(5f, circle.Radius.Value);
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 10, 30, 0, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan delta) => _now += delta;
    }
}
