using System.Text.Json;
using Bunit;
using Microsoft.AspNetCore.Components.Forms;
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
        Services.AddSingleton<DocumentImportRollbackService>();
        Services.AddSingleton<DocumentFileService>(sp => new DocumentFileService(
            new HttpClient(),
            _service,
            sp.GetRequiredService<SimulationConfigStore>()));
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

    [Fact]
    public void Import_ValidFile_RequiresConfirmationAndAppliesWithoutStorageWrite()
    {
        var existing = new Circle();
        _service.Add(existing);
        var loaded = new Rectangle { Name = { Value = "Imported" } };
        var json = DocumentSerializer.Serialize(
            new Primitive[] { loaded },
            new Primitive[] { loaded });
        var cut = RenderComponent<ToolPanel>();

        cut.FindComponent<InputFile>()
            .UploadFiles(InputFileContent.CreateFromText(json, "scene.penelopa.json"));

        cut.WaitForAssertion(() => Assert.Contains(
            cut.FindAll("button"),
            button => button.TextContent.Trim() == "Replace document"));
        Assert.Contains(existing, _service.GetAll());
        Assert.Contains("1 primitive(s), 0 active track(s).", cut.Markup);

        FindButton(cut, "Replace document").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("Imported", Assert.Single(_service.GetAll()).Name.Value);
            Assert.Single(_service.GetSelection());
            Assert.Contains("scene.penelopa.json", cut.Markup);
            Assert.Contains(
                cut.FindAll("button"),
                button => button.TextContent.Trim() == "Undo import");
        });
        Assert.DoesNotContain(
            JSInterop.Invocations,
            invocation => invocation.Identifier == "penelopa.writeStorage");
    }

    [Fact]
    public async Task Import_UndoRestoresPreviousDocumentSelectionAndHistory()
    {
        var circle = new Circle();
        _service.Add(circle);
        _service.SetSelected(circle);
        Assert.True(
            _service.TrySetProperty(circle, circle.Radius, "20", out var radiusError),
            radiusError);
        _service.Undo();
        Assert.Equal(5f, circle.Radius.Value);

        var imported = new Rectangle { Name = { Value = "Imported" } };
        var json = DocumentSerializer.Serialize(
            new Primitive[] { imported },
            new Primitive[] { imported });
        var cut = RenderComponent<ToolPanel>();
        cut.FindComponent<InputFile>()
            .UploadFiles(InputFileContent.CreateFromText(json, "scene.penelopa.json"));
        cut.WaitForAssertion(() => Assert.Contains(
            cut.FindAll("button"),
            button => button.TextContent.Trim() == "Replace document"));
        FindButton(cut, "Replace document").Click();
        cut.WaitForAssertion(() => Assert.Contains(
            cut.FindAll("button"),
            button => button.TextContent.Trim() == "Undo import"));

        FindButton(cut, "Undo import").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Same(circle, Assert.Single(_service.GetAll()));
            Assert.Same(circle, Assert.Single(_service.GetSelection()));
            Assert.Equal(5f, circle.Radius.Value);
            Assert.Contains("Import undone.", cut.Markup);
            Assert.DoesNotContain(
                cut.FindAll("button"),
                button => button.TextContent.Trim() == "Undo import");
        });

        await cut.InvokeAsync(() => _service.Redo());
        Assert.Equal(20f, circle.Radius.Value);
    }

    [Fact]
    public void Import_UndoBannerClearsAfterSuccessfulSave()
    {
        var imported = new Rectangle { Name = { Value = "Imported" } };
        var json = DocumentSerializer.Serialize(
            new Primitive[] { imported },
            new Primitive[] { imported });
        var cut = RenderComponent<ToolPanel>();
        cut.FindComponent<InputFile>()
            .UploadFiles(InputFileContent.CreateFromText(json, "scene.penelopa.json"));
        cut.WaitForAssertion(() => Assert.Contains(
            cut.FindAll("button"),
            button => button.TextContent.Trim() == "Replace document"));
        FindButton(cut, "Replace document").Click();
        cut.WaitForAssertion(() => Assert.Contains(
            cut.FindAll("button"),
            button => button.TextContent.Trim() == "Undo import"));

        FindButton(cut, "Save").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Document saved.", cut.Markup);
            Assert.DoesNotContain(
                cut.FindAll("button"),
                button => button.TextContent.Trim() == "Undo import");
        });
    }

    [Fact]
    public async Task Import_UndoBannerSurvivesSelectionAndTransientNotifications()
    {
        var imported = new Rectangle { Name = { Value = "Imported" } };
        var json = DocumentSerializer.Serialize(
            new Primitive[] { imported },
            new Primitive[] { imported });
        var cut = RenderComponent<ToolPanel>();
        cut.FindComponent<InputFile>()
            .UploadFiles(InputFileContent.CreateFromText(json, "scene.penelopa.json"));
        cut.WaitForAssertion(() => Assert.Contains(
            cut.FindAll("button"),
            button => button.TextContent.Trim() == "Replace document"));
        FindButton(cut, "Replace document").Click();
        cut.WaitForAssertion(() => Assert.Contains(
            cut.FindAll("button"),
            button => button.TextContent.Trim() == "Undo import"));

        await cut.InvokeAsync(() => _service.ClearSelection());
        await cut.InvokeAsync(() => _service.NotifyTransientValuesChanged());

        cut.WaitForAssertion(() => Assert.Contains(
            cut.FindAll("button"),
            button => button.TextContent.Trim() == "Undo import"));
    }

    [Fact]
    public void Import_InvalidFile_ShowsErrorAndKeepsCurrentDocument()
    {
        var existing = new Circle();
        _service.Add(existing);
        var cut = RenderComponent<ToolPanel>();

        cut.FindComponent<InputFile>()
            .UploadFiles(InputFileContent.CreateFromText("not json", "scene.penelopa.json"));

        cut.WaitForAssertion(() => Assert.Contains("valid or supported", cut.Markup));
        Assert.Contains(existing, _service.GetAll());
        Assert.DoesNotContain(
            cut.FindAll("button"),
            button => button.TextContent.Trim() == "Replace document");
    }

    [Fact]
    public void Import_RejectsMultipleFiles()
    {
        var cut = RenderComponent<ToolPanel>();
        var json = DocumentSerializer.Serialize(
            new Primitive[] { new Circle() },
            Array.Empty<Primitive>());

        cut.FindComponent<InputFile>().UploadFiles(
            InputFileContent.CreateFromText(json, "one.penelopa.json"),
            InputFileContent.CreateFromText(json, "two.penelopa.json"));

        cut.WaitForAssertion(() => Assert.Contains("Choose one file at a time.", cut.Markup));
        Assert.DoesNotContain(
            cut.FindAll("button"),
            button => button.TextContent.Trim() == "Replace document");
    }

    [Fact]
    public async Task Import_PendingConfirmationIsCanceledWhenDocumentChanges()
    {
        var existing = new Circle();
        _service.Add(existing);
        var json = DocumentSerializer.Serialize(
            new Primitive[] { new Rectangle() },
            Array.Empty<Primitive>());
        var cut = RenderComponent<ToolPanel>();
        cut.FindComponent<InputFile>()
            .UploadFiles(InputFileContent.CreateFromText(json, "scene.penelopa.json"));
        cut.WaitForAssertion(() => Assert.Contains(
            cut.FindAll("button"),
            button => button.TextContent.Trim() == "Replace document"));

        await cut.InvokeAsync(() => _service.Add(new Rectangle()));

        cut.WaitForAssertion(() =>
        {
            Assert.DoesNotContain(
                cut.FindAll("button"),
                button => button.TextContent.Trim() == "Replace document");
            Assert.Contains("Import canceled because the document changed.", cut.Markup);
        });
        Assert.Contains(existing, _service.GetAll());
    }

    [Fact]
    public async Task Import_PendingConfirmationIsCanceledWhenConfigurationChanges()
    {
        _service.Add(new Circle());
        var json = DocumentSerializer.Serialize(
            new Primitive[] { new Rectangle() },
            Array.Empty<Primitive>());
        var store = Services.GetRequiredService<SimulationConfigStore>();
        var cut = RenderComponent<ToolPanel>();
        cut.FindComponent<InputFile>()
            .UploadFiles(InputFileContent.CreateFromText(json, "scene.penelopa.json"));
        cut.WaitForAssertion(() => Assert.Contains(
            cut.FindAll("button"),
            button => button.TextContent.Trim() == "Replace document"));

        await cut.InvokeAsync(() => store.SetLoop(true));

        cut.WaitForAssertion(() =>
        {
            Assert.DoesNotContain(
                cut.FindAll("button"),
                button => button.TextContent.Trim() == "Replace document");
            Assert.Contains(
                "Import canceled because the simulation configuration changed.",
                cut.Markup);
        });
    }

    [Fact]
    public async Task Import_PendingConfirmationSurvivesSelectionChanges()
    {
        var first = new Circle();
        var second = new Rectangle();
        _service.Add(first);
        _service.Add(second);
        _service.SetSelected(first);
        var json = DocumentSerializer.Serialize(
            new Primitive[] { new Rectangle() },
            Array.Empty<Primitive>());
        var cut = RenderComponent<ToolPanel>();
        cut.FindComponent<InputFile>()
            .UploadFiles(InputFileContent.CreateFromText(json, "scene.penelopa.json"));
        cut.WaitForAssertion(() => Assert.Contains(
            cut.FindAll("button"),
            button => button.TextContent.Trim() == "Replace document"));

        await cut.InvokeAsync(() => _service.SetSelected(second));

        cut.WaitForAssertion(() => Assert.Contains(
            cut.FindAll("button"),
            button => button.TextContent.Trim() == "Replace document"));
        Assert.DoesNotContain("Import canceled because the document changed.", cut.Markup);
    }

    [Fact]
    public void Import_ConfirmationCanBeCancelled()
    {
        var existing = new Circle();
        _service.Add(existing);
        var json = DocumentSerializer.Serialize(
            new Primitive[] { new Rectangle() },
            Array.Empty<Primitive>());
        var cut = RenderComponent<ToolPanel>();
        cut.FindComponent<InputFile>()
            .UploadFiles(InputFileContent.CreateFromText(json, "scene.penelopa.json"));
        cut.WaitForAssertion(() => Assert.Contains(
            cut.FindAll("button"),
            button => button.TextContent.Trim() == "Replace document"));

        FindButton(cut, "Cancel import").Click();

        Assert.DoesNotContain(
            cut.FindAll("button"),
            button => button.TextContent.Trim() == "Replace document");
        Assert.Contains(existing, _service.GetAll());
        Assert.Contains("Import canceled.", cut.Markup);
    }

    [Fact]
    public void Export_DownloadsCurrentDocumentWithoutWritingStorage()
    {
        var rectangle = new Rectangle { Name = { Value = "Exported" } };
        _service.Add(rectangle);
        _service.SetSelected(rectangle);
        var store = Services.GetRequiredService<SimulationConfigStore>();
        Assert.True(
            store.TryAddTrack(rectangle.Id, "PosX", 0d, 10f, out var trackError),
            trackError);
        var cut = RenderComponent<ToolPanel>();

        FindButton(cut, "Export file…").Click();

        cut.WaitForAssertion(() => Assert.Contains("Document exported.", cut.Markup));
        var invocation = Assert.Single(
            JSInterop.Invocations,
            candidate => candidate.Identifier == "penelopa.downloadTextFile");
        var fileName = Assert.IsType<string>(invocation.Arguments[0]);
        var json = Assert.IsType<string>(invocation.Arguments[1]);
        Assert.StartsWith("penelopa-", fileName);
        Assert.EndsWith(".penelopa.json", fileName);
        using var document = JsonDocument.Parse(json);
        Assert.Equal(2, document.RootElement.GetProperty("Version").GetInt32());
        var track = Assert.Single(
            document.RootElement.GetProperty("Simulation").GetProperty("Tracks")
                .EnumerateArray());
        Assert.Equal(rectangle.Id, track.GetProperty("PrimitiveId").GetGuid());
        Assert.DoesNotContain(
            JSInterop.Invocations,
            candidate => candidate.Identifier == "penelopa.writeStorage");
    }

    [Fact]
    public void Export_WithDormantTracks_RequiresConfirmationAndExportsActiveOnly()
    {
        var active = new Rectangle { Name = { Value = "Active" } };
        var dormant = new Rectangle { Name = { Value = "Dormant" } };
        _service.Add(active);
        _service.Add(dormant);
        var store = Services.GetRequiredService<SimulationConfigStore>();
        Assert.True(store.TryAddTrack(active.Id, "PosX", 0d, 10f, out var firstError), firstError);
        Assert.True(store.TryAddTrack(dormant.Id, "PosX", 0d, 20f, out var secondError), secondError);
        _service.Remove(dormant);
        var cut = RenderComponent<ToolPanel>();

        FindButton(cut, "Export file…").Click();

        cut.WaitForAssertion(() => Assert.Contains(
            cut.FindAll("button"),
            button => button.TextContent.Trim() == "Export active only"));
        Assert.DoesNotContain(
            JSInterop.Invocations,
            invocation => invocation.Identifier == "penelopa.downloadTextFile");

        FindButton(cut, "Cancel export").Click();
        Assert.DoesNotContain(
            cut.FindAll("button"),
            button => button.TextContent.Trim() == "Export active only");

        FindButton(cut, "Export file…").Click();
        cut.WaitForAssertion(() => Assert.Contains(
            cut.FindAll("button"),
            button => button.TextContent.Trim() == "Export active only"));
        FindButton(cut, "Export active only").Click();

        cut.WaitForAssertion(() =>
        {
            var invocation = Assert.Single(
                JSInterop.Invocations,
                candidate => candidate.Identifier == "penelopa.downloadTextFile");
            var json = Assert.IsType<string>(invocation.Arguments[1]);
            using var document = JsonDocument.Parse(json);
            var track = Assert.Single(
                document.RootElement.GetProperty("Simulation").GetProperty("Tracks")
                    .EnumerateArray());
            Assert.Equal(active.Id, track.GetProperty("PrimitiveId").GetGuid());
        });
    }

    [Fact]
    public void Export_IsDisabledWithoutPrimitives()
    {
        var cut = RenderComponent<ToolPanel>();

        Assert.True(FindButton(cut, "Export file…").HasAttribute("disabled"));
    }

    private static AngleSharp.Dom.IElement FindButton(IRenderedFragment cut, string text)
        => cut.FindAll("button").Single(b => b.TextContent.Trim() == text);
}
