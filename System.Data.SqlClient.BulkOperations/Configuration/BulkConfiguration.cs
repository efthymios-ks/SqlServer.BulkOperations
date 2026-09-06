using System.Data;
using System.Linq.Expressions;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace System.Data.SqlClient.BulkOperations.Configuration;

internal sealed class BulkConfiguration<TEntity>(
    BulkOperationKind kind,
    IReadOnlyList<TEntity> items
    ) where TEntity : class
{
    public BulkOperationKind Kind { get; } = kind;

    public IReadOnlyList<TEntity> Items { get; } = items;

    public string? TableOverride { get; set; }

    public string? SchemaOverride { get; set; }

    public List<Expression<Func<TEntity, object?>>> ColumnSelectors { get; } = [];

    public List<Expression<Func<TEntity, object?>>> UpdateColumnSelectors { get; } = [];

    public List<Expression<Func<TEntity, object?>>> InsertColumnSelectors { get; } = [];

    public Dictionary<string, string> ColumnMappings { get; } = new(StringComparer.Ordinal);

    public List<Expression<Func<TEntity, object?>>> MatchSelectors { get; } = [];

    public Expression<Func<TEntity, object?>>? IdentityColumnOverride { get; set; }

    public List<Expression<Func<TEntity, object?>>> OutputSelectors { get; } = [];

    public bool OutputIdentity { get; set; }

    public bool KeepIdentity { get; set; }

    public bool InsertIfMissing { get; set; }

    public List<Expression<Func<TEntity, object?>>> InsertIfMissingSelectors { get; } = [];

    public bool RequireAllMatched { get; set; }

    public bool DeleteWhenNotMatched { get; set; }

    public DuplicateKeyBehavior DuplicateKeys { get; set; } = DuplicateKeyBehavior.Deduplicate;

    public bool? ConcurrencyCheckOverride { get; set; }

    public bool ThrowOnConcurrencyMismatch { get; set; }

    public int BatchSize { get; set; } = 5_000;

    public int BulkCopyTimeoutSeconds { get; set; } = 300;

    public int CommandTimeoutSeconds { get; set; } = 300;

    public SqlBulkCopyOptions? BulkCopyOptionsOverride { get; set; }

    public IsolationLevel IsolationLevel { get; set; } = IsolationLevel.ReadCommitted;

    public int MaxRetries { get; set; } = 3;

    public TimeSpan RetryBaseDelay { get; set; } = TimeSpan.FromMilliseconds(200);

    public bool OrderedKeyScan { get; set; } = true;

    public Action<long>? ProgressCallback { get; set; }

    public ILogger? Logger { get; set; }

    public string TempTablePrefix { get; set; } = "#bulk_";

    public bool WantsOutput
        => OutputIdentity || OutputSelectors.Count > 0;
}
