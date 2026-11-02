using Microsoft.JSInterop;
using Penelopa.Web.Services;

namespace Penelopa.Web.Tests;

/// <summary>Deterministic in-memory replacement for the browser storage/journal interop.</summary>
internal sealed class FakeBrowserRuntime : IJSRuntime
{
    public Dictionary<string, string> Storage { get; } = new(StringComparer.Ordinal);

    public Dictionary<string, JournalEntryDto> Journals { get; } = new(StringComparer.Ordinal);

    public List<string> StorageWrites { get; } = new();

    public List<string> JournalReads { get; } = new();

    public List<string> JournalClears { get; } = new();

    public Action<string>? BeforeStorageWrite { get; set; }

    public Action<string>? BeforeJournalWrite { get; set; }

    public ValueTask<TValue> InvokeAsync<TValue>(
        string identifier,
        object?[]? args)
        => new((TValue)Handle(identifier, args));

    public ValueTask<TValue> InvokeAsync<TValue>(
        string identifier,
        CancellationToken cancellationToken,
        object?[]? args)
        => new((TValue)Handle(identifier, args));

    private object Handle(string identifier, object?[]? args)
    {
        args ??= Array.Empty<object?>();
        switch (identifier)
        {
            case "penelopa.readStorage":
                {
                    var key = (string)args[0]!;
                    Storage.TryGetValue(key, out var value);
                    return new StorageReadResult(true, value, null);
                }

            case "penelopa.writeStorage":
                {
                    var key = (string)args[0]!;
                    var value = (string)args[1]!;
                    BeforeStorageWrite?.Invoke(key);
                    StorageWrites.Add(key);
                    Storage[key] = value;
                    return new StorageWriteResult(true, null);
                }

            case "penelopa.readJournal":
                {
                    var key = (string)args[0]!;
                    JournalReads.Add(key);
                    Journals.TryGetValue(key, out var entry);
                    return new JournalReadResult(true, entry, null);
                }

            case "penelopa.writeJournal":
                {
                    var entry = (JournalEntryDto)args[0]!;
                    var key = (string)args[1]!;
                    BeforeJournalWrite?.Invoke(key);
                    Journals[key] = entry;
                    return new JournalWriteResult(true, null);
                }

            case "penelopa.clearJournal":
                {
                    var key = (string)args[0]!;
                    JournalClears.Add(key);
                    Journals.Remove(key);
                    return new JournalWriteResult(true, null);
                }

            default:
                throw new NotSupportedException(identifier);
        }
    }
}
