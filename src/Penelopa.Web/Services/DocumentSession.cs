using Penelopa.Core.Primitives;
using Penelopa.Core.Services;
using Penelopa.Core.Simulation;

namespace Penelopa.Web.Services;

/// <summary>一个打开中的文档会话：独立模型/历史、配置、身份与存储键。</summary>
public sealed class DocumentSession
{
    public DocumentSession(
        Guid id,
        string name,
        string storageKey,
        string journalKey,
        PrimitiveService primitives,
        SimulationConfig? config = null)
    {
        Id = id;
        Name = name;
        StorageKey = storageKey;
        JournalKey = journalKey;
        Primitives = primitives;
        Config = config ?? SimulationConfig.Empty;
    }

    public Guid Id { get; }

    public string Name { get; }

    public string StorageKey { get; }

    public string JournalKey { get; }

    public PrimitiveService Primitives { get; }

    /// <summary>切换会话时由管理器保存/恢复的当前仿真配置。</summary>
    public SimulationConfig Config { get; set; }

    /// <summary>最近一次显式保存/加载的文档身份；用于关闭会话时判断未保存状态。</summary>
    public Guid SavedDocumentId { get; internal set; }

    /// <summary>最近一次显式保存/加载的 revision；-1 表示从未保存。</summary>
    public long SavedRevision { get; internal set; } = -1;
}
