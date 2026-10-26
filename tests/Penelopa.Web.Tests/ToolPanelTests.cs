using System.Text.Json;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Penelopa.Web.Components;
using Penelopa.Core.Primitives;
using Penelopa.Core.Services;
using Penelopa.Core.Simulation;
using Penelopa.Web.Services;
using Xunit;

namespace Penelopa.Web.Tests;

public class ToolPanelTests : TestContext
{
    private readonly PrimitiveService _service;

    public ToolPanelTests()
    {
        _service = new PrimitiveService();
        Services.AddSingleton<IPrimitiveService>(_service);
        Services.AddSingleton<SimulationConfigStore>();
        Services.AddSingleton<DocumentStorage>();
        JSInterop.Mode = JSRuntimeMode.Loose;
        JSInterop.Setup<StorageReadResult>("penelopa.readStorage", _ => true)
            .SetResult(new StorageReadResult(true, null, null));
        JSInterop.Setup<StorageWriteResult>("penelopa.writeStorage", _ => true)
            .SetResult(new StorageWriteResult(true, null));
    }

    [Fact]
    public void Load_InvalidDocument_ShowsErrorAndKeepsCurrentDocument()
    {
        var circle = new Circle();
        _service.Add(circle);
        JSInterop.Setup<StorageReadResult>(
                "penelopa.readStorage",
                DocumentStorage.StorageKey)
            .SetResult(new StorageReadResult(true, "not json", null));
        var cut = RenderComponent<ToolPanel>();

        FindButton(cut, "Load").Click();

        cut.WaitForAssertion(() => Assert.Contains("invalid or unsupported", cut.Markup));
        Assert.Contains(circle, _service.GetAll());
    }

    [Fact]
    public void Load_ValidDocument_RequiresConfirmationAndResolvesEmptySelection()
    {
        var existing = new Circle();
        _service.Add(existing);
        var loaded = new Rectangle { Name = { Value = "Loaded" } };
        var json = DocumentSerializer.Serialize(new Primitive[] { loaded }, Array.Empty<Primitive>());
        JSInterop.Setup<StorageReadResult>(
                "penelopa.readStorage",
                DocumentStorage.StorageKey)
            .SetResult(new StorageReadResult(true, json, null));
        var cut = RenderComponent<ToolPanel>();

        FindButton(cut, "Load").Click();

        cut.WaitForAssertion(() => Assert.Contains(cut.FindAll("button"), b => b.TextContent.Trim() == "Replace"));
        Assert.Contains(existing, _service.GetAll());
        Assert.Contains("simulation configuration", cut.Markup);

        FindButton(cut, "Replace").Click();

        var primitive = Assert.Single(_service.GetAll());
        Assert.Equal("Loaded", primitive.Name.Value);
        Assert.Empty(_service.GetSelection());
        Assert.Contains("Document loaded.", cut.Markup);
    }

    [Fact]
    public void Load_ConfirmationCanBeCancelled()
    {
        var existing = new Circle();
        _service.Add(existing);
        var json = DocumentSerializer.Serialize(new Primitive[] { new Rectangle() }, Array.Empty<Primitive>());
        JSInterop.Setup<StorageReadResult>(
                "penelopa.readStorage",
                DocumentStorage.StorageKey)
            .SetResult(new StorageReadResult(true, json, null));
        var cut = RenderComponent<ToolPanel>();

        FindButton(cut, "Load").Click();
        cut.WaitForAssertion(() => Assert.Contains(cut.FindAll("button"), b => b.TextContent.Trim() == "Cancel"));

        FindButton(cut, "Cancel").Click();

        Assert.DoesNotContain(cut.FindAll("button"), b => b.TextContent.Trim() == "Replace");
        Assert.Contains(existing, _service.GetAll());
    }

    [Fact]
    public async Task DeleteButton_RemovesSelectionAndUndoRestoresIt()
    {
        var circle = new Circle();
        _service.Add(circle);
        _service.SetSelected(circle);
        var cut = RenderComponent<ToolPanel>();

        var delete = FindButton(cut, "Delete");
        Assert.False(delete.HasAttribute("disabled"));
        delete.Click();

        cut.WaitForAssertion(() => Assert.Empty(_service.GetAll()));
        cut.WaitForAssertion(() => Assert.True(FindButton(cut, "Delete").HasAttribute("disabled")));

        await cut.InvokeAsync(() => _service.Undo());
        cut.WaitForAssertion(() => Assert.Single(_service.GetAll()));
    }

    [Fact]
    public void DeleteButton_IsDisabledWithoutSelection()
    {
        _service.Add(new Circle());
        var cut = RenderComponent<ToolPanel>();

        Assert.True(FindButton(cut, "Delete").HasAttribute("disabled"));
    }

    [Fact]
    public async Task GroupAndUngroupButtons_ReflectSelectionEligibility()
    {
        var first = new Circle();
        var second = new Rectangle();
        _service.Add(first);
        _service.Add(second);
        var cut = RenderComponent<ToolPanel>();

        Assert.True(FindButton(cut, "Group").HasAttribute("disabled"));
        Assert.True(FindButton(cut, "Ungroup").HasAttribute("disabled"));
        Assert.Contains("identity", cut.Find("#penelopa-group-hint").TextContent);

        await cut.InvokeAsync(() => _service.SetSelected(first));
        cut.WaitForAssertion(() => Assert.True(FindButton(cut, "Group").HasAttribute("disabled")));

        await cut.InvokeAsync(() => _service.SetSelectedRange(new Primitive[] { first, second }));
        cut.WaitForAssertion(() => Assert.False(FindButton(cut, "Group").HasAttribute("disabled")));
        Assert.True(FindButton(cut, "Ungroup").HasAttribute("disabled"));
    }

    [Fact]
    public void GroupButton_GroupsRootsAndUngroupRestoresThem()
    {
        var first = new Circle { Name = { Value = "First" } };
        var second = new Rectangle { Name = { Value = "Second" } };
        _service.Add(first);
        _service.Add(second);
        _service.SetSelectedRange(new Primitive[] { first, second });
        var cut = RenderComponent<ToolPanel>();

        var group = FindButton(cut, "Group");
        Assert.False(group.HasAttribute("disabled"));
        group.Click();

        cut.WaitForAssertion(() =>
        {
            var container = Assert.IsType<Container>(Assert.Single(_service.GetAll()));
            Assert.Equal(new Primitive[] { first, second }, container.Children);
            Assert.Same(container, Assert.Single(_service.GetSelection()));
            Assert.False(FindButton(cut, "Ungroup").HasAttribute("disabled"));
        });

        FindButton(cut, "Ungroup").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Equal(new Primitive[] { first, second }, _service.GetAll());
            Assert.Equal(new Primitive[] { first, second }, _service.GetSelection());
            Assert.Null(first.Parent);
            Assert.Null(second.Parent);
            Assert.True(FindButton(cut, "Ungroup").HasAttribute("disabled"));
        });
    }

    [Fact]
    public async Task GroupAndUngroup_AreDisabledForDrilledChildAndNonIdentityContainer()
    {
        var container = Container.CreateOffset("Offset", 20f, 20f);
        var child = new Circle();
        container.AddChild(child);
        _service.Add(container);
        _service.SetSelected(child);
        var cut = RenderComponent<ToolPanel>();

        Assert.True(FindButton(cut, "Group").HasAttribute("disabled"));
        Assert.True(FindButton(cut, "Ungroup").HasAttribute("disabled"));

        await cut.InvokeAsync(() => _service.SetSelected(container));
        cut.WaitForAssertion(() =>
        {
            Assert.True(FindButton(cut, "Group").HasAttribute("disabled"));
            Assert.True(FindButton(cut, "Ungroup").HasAttribute("disabled"));
        });

        var identity = Container.CreateOffset("Identity", 0f, 0f);
        await cut.InvokeAsync(() =>
        {
            _service.Add(identity);
            _service.SetSelected(identity);
        });
        cut.WaitForAssertion(() => Assert.False(FindButton(cut, "Ungroup").HasAttribute("disabled")));
    }

    [Fact]
    public void Save_BacksUpPreviousDocument()
    {
        _service.Add(new Circle());
        JSInterop.Setup<StorageReadResult>(
                "penelopa.readStorage",
                DocumentStorage.StorageKey)
            .SetResult(new StorageReadResult(true, "previous-document", null));
        var cut = RenderComponent<ToolPanel>();

        FindButton(cut, "Save").Click();

        cut.WaitForAssertion(() => Assert.Contains(
            JSInterop.Invocations,
            invocation => invocation.Identifier == "penelopa.writeStorage"
                && invocation.Arguments.Count == 2
                && Equals(invocation.Arguments[0], "penelopa.document.backup")
                && Equals(invocation.Arguments[1], "previous-document")));
        Assert.Contains(
            JSInterop.Invocations,
            invocation => invocation.Identifier == "penelopa.writeStorage"
                && Equals(invocation.Arguments[0], "penelopa.document"));
    }

    [Fact]
    public void Save_IncludesSimulationConfiguration()
    {
        var rectangle = new Rectangle();
        _service.Add(rectangle);
        var store = Services.GetRequiredService<SimulationConfigStore>();
        Assert.True(
            store.TryAddTrack(rectangle.Id, "PosX", 0d, 10f, out var trackError),
            trackError);
        var cut = RenderComponent<ToolPanel>();

        FindButton(cut, "Save").Click();

        cut.WaitForAssertion(() =>
        {
            var invocation = Assert.Single(
                JSInterop.Invocations,
                invocation => invocation.Identifier == "penelopa.writeStorage"
                    && Equals(invocation.Arguments[0], DocumentStorage.StorageKey));
            var json = Assert.IsType<string>(invocation.Arguments[1]);
            using var document = JsonDocument.Parse(json);
            var simulation = document.RootElement.GetProperty("Simulation");
            var tracks = simulation.GetProperty("Tracks");
            Assert.Equal(1, tracks.GetArrayLength());
            Assert.Equal("PosX", tracks[0].GetProperty("Property").GetString());
        });
    }

    [Fact]
    public void Save_WithDormantTracks_RequiresConfirmationAndSavesOnlyActiveTracks()
    {
        var rectangle = new Rectangle();
        _service.Add(rectangle);
        var store = Services.GetRequiredService<SimulationConfigStore>();
        Assert.True(
            store.TryAddTrack(rectangle.Id, "PosX", 0d, 10f, out var trackError),
            trackError);
        _service.Remove(rectangle);
        var cut = RenderComponent<ToolPanel>();

        FindButton(cut, "Save").Click();

        cut.WaitForAssertion(() => Assert.Contains(
            cut.FindAll("button"),
            button => button.TextContent.Trim() == "Save active only"));
        Assert.DoesNotContain(JSInterop.Invocations, invocation =>
            invocation.Identifier == "penelopa.writeStorage"
            && Equals(invocation.Arguments[0], DocumentStorage.StorageKey));

        FindButton(cut, "Cancel save").Click();
        Assert.DoesNotContain(
            cut.FindAll("button"),
            button => button.TextContent.Trim() == "Save active only");

        FindButton(cut, "Save").Click();
        cut.WaitForAssertion(() => Assert.Contains(
            cut.FindAll("button"),
            button => button.TextContent.Trim() == "Save active only"));
        FindButton(cut, "Save active only").Click();

        cut.WaitForAssertion(() =>
        {
            var invocation = Assert.Single(
                JSInterop.Invocations,
                invocation => invocation.Identifier == "penelopa.writeStorage"
                    && Equals(invocation.Arguments[0], DocumentStorage.StorageKey));
            var json = Assert.IsType<string>(invocation.Arguments[1]);
            using var document = JsonDocument.Parse(json);
            var simulation = document.RootElement.GetProperty("Simulation");
            Assert.Equal(0, simulation.GetProperty("Tracks").GetArrayLength());
        });
    }

    [Fact]
    public void Load_VersionOneDocument_ClearsSimulationConfiguration()
    {
        var current = new Rectangle();
        _service.Add(current);
        var store = Services.GetRequiredService<SimulationConfigStore>();
        Assert.True(
            store.TryAddTrack(current.Id, "PosX", 0d, 10f, out var trackError),
            trackError);
        const string json = @"{
          ""Version"": 1,
          ""Primitives"": [
            {
              ""Id"": ""00000000-0000-0000-0000-000000000001"",
              ""Type"": ""Rectangle"",
              ""Props"": { ""Name"": ""Loaded"" }
            }
          ],
          ""SelectionIds"": []
        }";
        JSInterop.Setup<StorageReadResult>(
                "penelopa.readStorage",
                DocumentStorage.StorageKey)
            .SetResult(new StorageReadResult(true, json, null));
        var cut = RenderComponent<ToolPanel>();

        FindButton(cut, "Load").Click();
        cut.WaitForAssertion(() => Assert.Contains(
            cut.FindAll("button"),
            button => button.TextContent.Trim() == "Replace"));
        FindButton(cut, "Replace").Click();

        cut.WaitForAssertion(() => Assert.Empty(store.Config.Tracks));
        Assert.Equal("Loaded", Assert.Single(_service.GetAll()).Name.Value);
    }

    [Fact]
    public void Load_OrphanSimulationTrack_ShowsWarningAndPrunes()
    {
        var current = new Rectangle();
        _service.Add(current);
        var store = Services.GetRequiredService<SimulationConfigStore>();
        Assert.True(
            store.TryAddTrack(current.Id, "PosX", 0d, 10f, out var trackError),
            trackError);

        var loaded = new Rectangle { Name = { Value = "Loaded" } };
        var orphan = new SimulationTrack(
            Guid.NewGuid(),
            "PosX",
            new[]
            {
                new SimulationKeyframe(0d, 10f, InterpolationKind.Linear),
                new SimulationKeyframe(1d, 20f, InterpolationKind.Linear),
            });
        var json = DocumentSerializer.Serialize(
            new Primitive[] { loaded },
            Array.Empty<Primitive>(),
            new SimulationConfig(false, new[] { orphan }));
        JSInterop.Setup<StorageReadResult>(
                "penelopa.readStorage",
                DocumentStorage.StorageKey)
            .SetResult(new StorageReadResult(true, json, null));
        var cut = RenderComponent<ToolPanel>();

        FindButton(cut, "Load").Click();
        cut.WaitForAssertion(() => Assert.Contains(
            cut.FindAll("button"),
            button => button.TextContent.Trim() == "Replace"));
        FindButton(cut, "Replace").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Empty(store.Config.Tracks);
            Assert.Contains("Removed 1", cut.Markup);
        });
    }

    [Fact]
    public void Save_WithDormantTracks_ClearsPendingConfirmationWhenLoadStarts()
    {
        var rectangle = new Rectangle();
        _service.Add(rectangle);
        var store = Services.GetRequiredService<SimulationConfigStore>();
        Assert.True(
            store.TryAddTrack(rectangle.Id, "PosX", 0d, 10f, out var trackError),
            trackError);
        _service.Remove(rectangle);

        var loaded = new Rectangle { Name = { Value = "Loaded" } };
        var loadedTrack = new SimulationTrack(
            loaded.Id,
            "PosX",
            new[]
            {
                new SimulationKeyframe(0d, 10f, InterpolationKind.Linear),
                new SimulationKeyframe(1d, 20f, InterpolationKind.Linear),
            });
        var json = DocumentSerializer.Serialize(
            new Primitive[] { loaded },
            Array.Empty<Primitive>(),
            new SimulationConfig(false, new[] { loadedTrack }));
        JSInterop.Setup<StorageReadResult>(
                "penelopa.readStorage",
                DocumentStorage.StorageKey)
            .SetResult(new StorageReadResult(true, json, null));
        var cut = RenderComponent<ToolPanel>();

        FindButton(cut, "Save").Click();
        cut.WaitForAssertion(() => Assert.Contains(
            cut.FindAll("button"),
            button => button.TextContent.Trim() == "Save active only"));

        FindButton(cut, "Load").Click();

        cut.WaitForAssertion(() => Assert.Contains(
            cut.FindAll("button"),
            button => button.TextContent.Trim() == "Replace"));
        Assert.DoesNotContain(
            cut.FindAll("button"),
            button => button.TextContent.Trim() == "Save active only");
        Assert.False(FindButton(cut, "Save").HasAttribute("disabled"));

        FindButton(cut, "Replace").Click();
        cut.WaitForAssertion(() => Assert.Contains("Document loaded.", cut.Markup));

        FindButton(cut, "Save").Click();
        cut.WaitForAssertion(() =>
        {
            var invocation = Assert.Single(
                JSInterop.Invocations,
                invocation => invocation.Identifier == "penelopa.writeStorage"
                    && Equals(invocation.Arguments[0], DocumentStorage.StorageKey));
            var savedJson = Assert.IsType<string>(invocation.Arguments[1]);
            using var document = JsonDocument.Parse(savedJson);
            var tracks = document.RootElement.GetProperty("Simulation").GetProperty("Tracks");
            Assert.Equal(1, tracks.GetArrayLength());
            Assert.Equal(
                loaded.Id,
                tracks[0].GetProperty("PrimitiveId").GetGuid());
        });
    }

    [Fact]
    public void Save_WithDormantTracks_RecomputesActiveConfigWhenConfirming()
    {
        var dormantPrimitive = new Rectangle();
        var activePrimitive = new Rectangle();
        _service.Add(dormantPrimitive);
        _service.Add(activePrimitive);
        var store = Services.GetRequiredService<SimulationConfigStore>();
        Assert.True(
            store.TryAddTrack(dormantPrimitive.Id, "PosX", 0d, 10f, out var firstError),
            firstError);
        Assert.True(
            store.TryAddTrack(activePrimitive.Id, "PosY", 0d, 20f, out var secondError),
            secondError);
        _service.Remove(dormantPrimitive);
        var cut = RenderComponent<ToolPanel>();

        FindButton(cut, "Save").Click();
        cut.WaitForAssertion(() => Assert.Contains(
            cut.FindAll("button"),
            button => button.TextContent.Trim() == "Save active only"));

        Assert.True(
            store.TryAddTrack(activePrimitive.Id, "PosX", 0d, 30f, out var addedError),
            addedError);
        FindButton(cut, "Save active only").Click();

        cut.WaitForAssertion(() =>
        {
            var invocation = Assert.Single(
                JSInterop.Invocations,
                invocation => invocation.Identifier == "penelopa.writeStorage"
                    && Equals(invocation.Arguments[0], DocumentStorage.StorageKey));
            var savedJson = Assert.IsType<string>(invocation.Arguments[1]);
            using var document = JsonDocument.Parse(savedJson);
            var tracks = document.RootElement.GetProperty("Simulation").GetProperty("Tracks");
            Assert.Equal(2, tracks.GetArrayLength());
        });
    }

    [Fact]
    public async Task Save_WithDormantTracks_CancelsPendingConfirmationWhenDocumentChanges()
    {
        var first = new Rectangle();
        var second = new Rectangle();
        _service.Add(first);
        _service.Add(second);
        var store = Services.GetRequiredService<SimulationConfigStore>();
        Assert.True(
            store.TryAddTrack(first.Id, "PosX", 0d, 10f, out var firstError),
            firstError);
        Assert.True(
            store.TryAddTrack(second.Id, "PosY", 0d, 20f, out var secondError),
            secondError);
        _service.Remove(first);
        var cut = RenderComponent<ToolPanel>();

        FindButton(cut, "Save").Click();
        cut.WaitForAssertion(() => Assert.Contains(
            cut.FindAll("button"),
            button => button.TextContent.Trim() == "Save active only"));

        await cut.InvokeAsync(() => _service.Undo());

        cut.WaitForAssertion(() => Assert.DoesNotContain(
            cut.FindAll("button"),
            button => button.TextContent.Trim() == "Save active only"));

        FindButton(cut, "Save").Click();
        cut.WaitForAssertion(() =>
        {
            var invocation = Assert.Single(
                JSInterop.Invocations,
                invocation => invocation.Identifier == "penelopa.writeStorage"
                    && Equals(invocation.Arguments[0], DocumentStorage.StorageKey));
            var savedJson = Assert.IsType<string>(invocation.Arguments[1]);
            using var document = JsonDocument.Parse(savedJson);
            var tracks = document.RootElement.GetProperty("Simulation").GetProperty("Tracks");
            Assert.Equal(2, tracks.GetArrayLength());
        });
    }

    private static AngleSharp.Dom.IElement FindButton(IRenderedFragment cut, string text)
        => cut.FindAll("button").Single(b => b.TextContent.Trim() == text);
}
