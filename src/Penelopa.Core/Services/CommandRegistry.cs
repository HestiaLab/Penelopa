using Penelopa.Core.Primitives;

namespace Penelopa.Core.Services;

/// <summary>稳定命令 id 与可读名称的注册表，供 UI、宏与审计共享。</summary>
public sealed class CommandRegistry
{
    private readonly Dictionary<string, string> _displayNames = new(StringComparer.Ordinal);

    public static CommandRegistry Default { get; } = CreateDefault();

    public void Register(string commandId, string displayName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(commandId);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        _displayNames[commandId] = displayName;
    }

    public bool TryGetDisplayName(string commandId, out string displayName)
        => _displayNames.TryGetValue(commandId, out displayName!);

    private static CommandRegistry CreateDefault()
    {
        var registry = new CommandRegistry();
        registry.Register(DocumentCommands.Edit, "Edit");
        registry.Register(DocumentCommands.Add, "Add primitive");
        registry.Register(DocumentCommands.MoveIntoContainer, "Move into container");
        registry.Register(DocumentCommands.Group, "Group");
        registry.Register(DocumentCommands.Ungroup, "Ungroup");
        registry.Register(DocumentCommands.Delete, "Delete");
        registry.Register(DocumentCommands.Align, "Align");
        registry.Register(DocumentCommands.SetProperty, "Set property");
        registry.Register(DocumentCommands.MoveSelection, "Move selection");
        registry.Register(DocumentCommands.Gesture, "Edit gesture");
        registry.Register(DocumentCommands.SetKeyframe, "Set keyframe");
        registry.Register(DocumentCommands.AddTrack, "Add track");
        registry.Register(DocumentCommands.RemoveTrack, "Remove track");
        registry.Register(DocumentCommands.UpdateKeyframe, "Update keyframe");
        registry.Register(DocumentCommands.RemoveKeyframe, "Remove keyframe");
        registry.Register(DocumentCommands.RemoveDormantTracks, "Remove inactive tracks");
        return registry;
    }
}
