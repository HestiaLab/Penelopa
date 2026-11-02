using System.Reflection;
using Penelopa.Core.Alignment;
using Penelopa.Core.Primitives;
using Penelopa.Core.Services;
using Xunit;

namespace Penelopa.Core.Tests;

public class DocumentEditorTests
{
    [Fact]
    public void Add_DelegatesToServiceAndCanBeUndone()
    {
        var service = new PrimitiveService();
        var editor = new DocumentEditor(service);
        var circle = new Circle();

        editor.Add(circle);

        Assert.Same(circle, Assert.Single(service.GetAll()));
        Assert.True(editor.CanUndo);

        editor.Undo();

        Assert.Empty(service.GetAll());
        Assert.True(editor.CanRedo);
    }

    [Fact]
    public void TrySetProperty_WritesThroughServiceHistory()
    {
        var service = new PrimitiveService();
        var editor = new DocumentEditor(service);
        var circle = new Circle();
        editor.Add(circle);

        Assert.True(
            editor.TrySetProperty(circle, circle.Radius, "20", out var error),
            error);
        Assert.Equal(20f, circle.Radius.Value);

        editor.Undo();

        Assert.Equal(5f, circle.Radius.Value);
    }

    [Fact]
    public void StructureWrites_UseTheSameHistoryBoundary()
    {
        var service = new PrimitiveService();
        var editor = new DocumentEditor(service);
        var container = Container.CreateOffset("Group", 0f, 0f);
        var child = new Rectangle();
        editor.Add(container);
        editor.Add(child);

        editor.AddToContainer(container, child);

        Assert.Same(container, child.Parent);
        Assert.DoesNotContain(child, service.GetAll());

        editor.Undo();

        Assert.Null(child.Parent);
        Assert.Contains(child, service.GetAll());
    }

    [Fact]
    public void PublicApi_DoesNotExposeModelSetters()
    {
        var valueSetter = typeof(FloatPropValue)
            .GetProperty(nameof(FloatPropValue.Value))!
            .GetSetMethod(nonPublic: true);
        Assert.NotNull(valueSetter);
        Assert.False(valueSetter!.IsPublic);

        var setBoxedValue = typeof(PropValue).GetMethod(
            nameof(PropValue.SetBoxedValue),
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.NotNull(setBoxedValue);
        Assert.False(setBoxedValue!.IsPublic);

        var trySetText = typeof(PropValue).GetMethod(
            nameof(PropValue.TrySetText),
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.NotNull(trySetText);
        Assert.False(trySetText!.IsPublic);
        Assert.Null(typeof(IAlignable).GetMethod(nameof(Primitive.SetWorldPosition)));

        Assert.Null(typeof(PropValue).GetProperty(nameof(PropValue.Name))!.SetMethod);

        foreach (var methodName in new[]
                 {
                     nameof(Container.AddChild),
                     nameof(Container.RemoveChild),
                 })
        {
            var method = typeof(Container).GetMethod(
                methodName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.NotNull(method);
            Assert.False(method!.IsPublic);
        }

        foreach (var methodName in new[]
                 {
                     nameof(Primitive.Translate),
                     nameof(Primitive.SetBounds),
                     nameof(Primitive.SetWorldPosition),
                 })
        {
            var method = typeof(Primitive).GetMethod(
                methodName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.NotNull(method);
            Assert.False(method!.IsPublic);
        }
    }
}
