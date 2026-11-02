using Penelopa.Core.Primitives;
using Penelopa.Core.Services;
using Xunit;

namespace Penelopa.Core.Tests;

public class HistoryInvariantTests
{
    private static readonly Guid DocumentId =
        Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    [Fact]
    public void RandomDocumentOperations_UndoAllRestoresInitialAndRedoAllRestoresFinal()
    {
        var service = new PrimitiveService();
        var first = new Rectangle { Name = { Value = "Root A" } };
        var second = new Circle { Name = { Value = "Root B" } };
        service.ReplaceAll(new Primitive[] { first, second });
        var initial = Snapshot(service);
        var live = new List<Primitive> { first, second };
        var random = new Random(20261030);

        for (var step = 0; step < 40; step++)
        {
            switch (random.Next(3))
            {
                case 0:
                    {
                        var added = new Rectangle
                        {
                            Name = { Value = $"Added {step}" },
                            PosX = { Value = step },
                        };
                        service.Add(added);
                        live.Add(added);
                        break;
                    }

                case 1 when live.Count > 1:
                    {
                        var index = random.Next(live.Count);
                        service.Remove(live[index]);
                        live.RemoveAt(index);
                        break;
                    }

                case 2:
                    {
                        var target = live[random.Next(live.Count)];
                        var value = random.Next(0, 100).ToString(
                            System.Globalization.CultureInfo.InvariantCulture);
                        var property = target switch
                        {
                            Rectangle rectangle => rectangle.PosX,
                            Circle circle => circle.CenterX,
                            _ => null,
                        };
                        if (property is not null)
                        {
                            Assert.True(service.TrySetProperty(target, property, value, out var error), error);
                        }

                        break;
                    }
            }
        }

        var final = Snapshot(service);

        while (service.CanUndo)
        {
            service.Undo();
        }

        Assert.Equal(initial, Snapshot(service));

        while (service.CanRedo)
        {
            service.Redo();
        }

        Assert.Equal(final, Snapshot(service));
    }

    private static string Snapshot(PrimitiveService service)
        => DocumentSerializer.Serialize(
            service.GetAll(),
            Array.Empty<Primitive>(),
            documentId: DocumentId,
            revision: 0);
}
