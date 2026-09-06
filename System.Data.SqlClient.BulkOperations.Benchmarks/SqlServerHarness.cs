using Microsoft.Data.SqlClient;
using System.Data.SqlClient.BulkOperations.Benchmarks.Entities;
using Testcontainers.MsSql;

namespace System.Data.SqlClient.BulkOperations.Benchmarks;

/// <summary>
/// The database every benchmark runs against. Point <c>SQLBULKOPS_BENCHMARK_CONNECTION_STRING</c> at a real server to
/// measure against it; otherwise a SQL Server 2022 container is started for the run.
/// </summary>
public sealed class SqlServerHarness : IAsyncDisposable
{
    public const string ConnectionStringVariable = "SQLBULKOPS_BENCHMARK_CONNECTION_STRING";

    private const string SchemaSql = """
        IF OBJECT_ID('dbo.Books') IS NULL
        BEGIN
            CREATE TABLE [dbo].[Books] (
                [Id] INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                [Isbn] NVARCHAR(20) NOT NULL,
                [Title] NVARCHAR(200) NOT NULL,
                [Price] DECIMAL(18,2) NOT NULL,
                [Edition] INT NOT NULL DEFAULT 1,
                [RowVersion] ROWVERSION NOT NULL
            );
            CREATE UNIQUE INDEX UX_Books_Isbn ON [dbo].[Books]([Isbn]);
        END

        IF TYPE_ID('dbo.BookTableType') IS NULL
        EXEC('
            CREATE TYPE [dbo].[BookTableType] AS TABLE (
                [Ordinal] INT NOT NULL PRIMARY KEY,
                [Isbn] NVARCHAR(20) NOT NULL,
                [Title] NVARCHAR(200) NOT NULL,
                [Price] DECIMAL(18,2) NOT NULL,
                [Edition] INT NOT NULL,
                UNIQUE ([Isbn])
            );');
        """;

    private MsSqlContainer? _container;

    public string ConnectionString { get; private set; } = string.Empty;

    public async Task StartAsync()
    {
        var configured = Environment.GetEnvironmentVariable(ConnectionStringVariable);

        if (!string.IsNullOrWhiteSpace(configured))
        {
            ConnectionString = configured;
        }
        else
        {
            _container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();
            await _container.StartAsync();
            ConnectionString = _container.GetConnectionString();
        }

        await ExecuteAsync(SchemaSql);
    }

    public async ValueTask DisposeAsync()
    {
        if (_container is not null)
        {
            await _container.DisposeAsync();
        }
    }

    public async Task<SqlConnection> OpenConnectionAsync()
    {
        var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();

        return connection;
    }

    public async Task ExecuteAsync(string sql)
    {
        await using var connection = await OpenConnectionAsync();
        await using var command = new SqlCommand(sql, connection) { CommandTimeout = 0 };

        await command.ExecuteNonQueryAsync();
    }

    public Task TruncateAsync()
        => ExecuteAsync("TRUNCATE TABLE [dbo].[Books];");

    /// <summary>Empties the table and refills it with <paramref name="rowCount"/> rows, ids and tokens loaded.</summary>
    public async Task<Book[]> ReseedAsync(int rowCount, string prefix)
    {
        await TruncateAsync();

        var books = Generate(rowCount, prefix);

        await using var connection = await OpenConnectionAsync();
        await BulkOperation.Insert(books)
            .WithOutputIdentity()
            .ExecuteAsync(connection);

        return books;
    }

    public static Book[] Generate(int rowCount, string prefix)
        =>
        [
            .. Enumerable.Range(1, rowCount).Select(index => new Book
            {
                Isbn = $"{prefix}-{index:D8}",
                Title = $"Title {index}",
                Price = index % 100 + 0.99m,
                Edition = index % 5 + 1
            })
        ];
}
