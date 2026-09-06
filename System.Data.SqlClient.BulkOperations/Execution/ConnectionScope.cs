using System.Data;
using Microsoft.Data.SqlClient;

namespace System.Data.SqlClient.BulkOperations.Execution;

/// <summary>Opens a connection only if the caller left it closed, and closes only what it opened.</summary>
internal sealed class ConnectionScope(SqlConnection connection, bool openedHere) : IAsyncDisposable
{
    public static async Task<ConnectionScope> OpenAsync(
        SqlConnection connection,
        CancellationToken cancellationToken
    )
    {
        if (connection.State is ConnectionState.Open)
        {
            return new ConnectionScope(connection, openedHere: false);
        }

        await connection.OpenAsync(cancellationToken);

        return new ConnectionScope(connection, openedHere: true);
    }

    public async ValueTask DisposeAsync()
    {
        if (openedHere)
        {
            await connection.CloseAsync();
        }
    }
}
