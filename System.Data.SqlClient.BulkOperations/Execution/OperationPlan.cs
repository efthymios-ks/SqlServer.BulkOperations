using System.Data.SqlClient.BulkOperations.Configuration;
using System.Data.SqlClient.BulkOperations.Metadata;

namespace System.Data.SqlClient.BulkOperations.Execution;

internal sealed record OperationPlan
{
    public required BulkOperationKind Kind { get; init; }

    public required string TargetTable { get; init; }

    public required IReadOnlyList<ColumnMetadata> InsertColumns { get; init; }

    public required IReadOnlyList<ColumnMetadata> UpdateColumns { get; init; }

    public required IReadOnlyList<ColumnMetadata> MatchColumns { get; init; }

    public required IReadOnlyList<ColumnMetadata> ConcurrencyColumns { get; init; }

    public required IReadOnlyList<ColumnMetadata> OutputColumns { get; init; }

    public required IReadOnlyList<ColumnMetadata> StagingColumns { get; init; }

    public required bool KeepIdentity { get; init; }

    public required bool InsertIfMissing { get; init; }

    public required bool DeleteWhenNotMatched { get; init; }

    /// <summary>
    /// Whether one staging row per match key is guaranteed, which decides between a primary key
    /// and a non-unique clustered index on the staging table.
    /// </summary>
    public required bool MatchIsUnique { get; init; }
}
