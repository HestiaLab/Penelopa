using Penelopa.Core.Primitives;
using Penelopa.Core.Simulation;

namespace Penelopa.Core.Tests.Simulation;

internal static class SimulationTestHelpers
{
    public static SimulationKeyframe Frame(
        double time,
        float value,
        InterpolationKind interpolation = InterpolationKind.Linear,
        CubicBezier? bezier = null)
        => new(time, value, interpolation, bezier);

    public static SimulationTrack Track(
        Primitive primitive,
        string propertyName,
        params SimulationKeyframe[] keyframes)
        => new(primitive.Id, propertyName, keyframes);

    public static SimulationConfig Config(
        bool loop = false,
        params SimulationTrack[] tracks)
        => new(loop, tracks);

    public static Rectangle CreateRectangle(
        float posX = 10f,
        float posY = 20f,
        float width = 30f,
        float height = 40f)
    {
        var rectangle = new Rectangle();
        rectangle.PosX.Value = posX;
        rectangle.PosY.Value = posY;
        rectangle.Width.Value = width;
        rectangle.Height.Value = height;
        return rectangle;
    }
}
