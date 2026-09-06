using System.Data;
using Microsoft.Data.SqlClient;

namespace System.Data.SqlClient.BulkOperations.Execution;

/// <summary>
/// Uses the caller's transaction when given one and never commits or rolls that back; otherwise
/// owns a transaction of its own, committing on success and rolling back on any escape.
/// </summary>
internal sealed class TransactionScope : IAsyncDisposable
{
    private readonly SqlTransaction? _owned;
    private bool _committed;

    private TransactionScope(SqlTransaction transaction, bool owned)
    {
        Transaction = transaction;
        _owned = owned ? transaction : null;
    }

    public SqlTransaction Transaction { get; }

    public static async Task<TransactionScope> AcquireAsync(
        SqlConnection connection,
        SqlTransaction? externalTransaction,
        IsolationLevel isolationLevel,
        CancellationToken cancellationToken
    )
    {
        if (externalTransaction is not null)
        {
            return new TransactionScope(externalTransaction, owned: false);
        }

        var transaction = (SqlTransaction)await connection.BeginTransactionAsync(
            isolationLevel,
            cancellationToken
        );

        return new TransactionScope(transaction, owned: true);
    }

    public async Task CommitAsync(CancellationToken cancellationToken)
    {
        if (_owned is null)
        {
            return;
        }

        await _owned.CommitAsync(cancellationToken);
        _committed = true;
    }

    public async ValueTask DisposeAsync()
    {
        if (_owned is null)
        {
            return;
        }

        if (!_committed)
        {
            try
            {
                await _owned.RollbackAsync();
            }
            catch
            {
                // The connection may already be broken, in which case the server has rolled back for us.
            }
        }

        await _owned.DisposeAsync();
    }
}
