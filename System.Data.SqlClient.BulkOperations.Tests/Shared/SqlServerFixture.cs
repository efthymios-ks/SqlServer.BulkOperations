using Microsoft.Data.SqlClient;
using Testcontainers.MsSql;
using Xunit;

namespace System.Data.SqlClient.BulkOperations.Tests.Shared;

/// <summary>
/// One SQL Server container for the whole integration suite. Tests share it and call
/// <see cref="ResetAsync"/> first, so they must stay in the single collection below.
/// </summary>
public sealed class SqlServerFixture : IAsyncLifetime
{
    private const string SchemaSql = """
        IF SCHEMA_ID('shop') IS NULL EXEC('CREATE SCHEMA [shop]');

        IF OBJECT_ID('dbo.Books') IS NULL
        BEGIN
            CREATE TABLE [dbo].[Books] (
                [Id] INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                [Isbn] NVARCHAR(20) NOT NULL,
                [Title] NVARCHAR(200) NOT NULL,
                [Price] DECIMAL(18,2) NOT NULL,
                [Edition] INT NOT NULL DEFAULT 1,
                [CreatedUtc] AS SYSUTCDATETIME(),
                [RowVersion] ROWVERSION NOT NULL
            );
            CREATE UNIQUE INDEX UX_Books_Isbn ON [dbo].[Books]([Isbn]);
        END

        IF OBJECT_ID('dbo.Widgets') IS NULL
        BEGIN
            CREATE TABLE [dbo].[Widgets] (
                [WidgetId] INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                [Name] NVARCHAR(100) NOT NULL,
                [Quantity] INT NOT NULL,
                [Status] INT NOT NULL DEFAULT 0
            );
        END

        IF OBJECT_ID('shop.Items') IS NULL
        BEGIN
            CREATE TABLE [shop].[Items] (
                [Id] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
                [Sku] NVARCHAR(50) NOT NULL,
                [Price] DECIMAL(18,2) NOT NULL,
                [Active] BIT NOT NULL
            );
        END
        """;

    private const string ResetSql = """
        DELETE FROM [dbo].[Books];
        DELETE FROM [dbo].[Widgets];
        DELETE FROM [shop].[Items];
        DBCC CHECKIDENT ('[dbo].[Books]', RESEED, 0) WITH NO_INFOMSGS;
        DBCC CHECKIDENT ('[dbo].[Widgets]', RESEED, 0) WITH NO_INFOMSGS;
        """;

    private readonly MsSqlContainer _container =
        new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();

    public string ConnectionString
        => _container.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        await ExecuteAsync(SchemaSql);
    }

    public async Task DisposeAsync()
        => await _container.DisposeAsync();

    public async Task<SqlConnection> OpenConnectionAsync()
    {
        var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();

        return connection;
    }

    public Task ResetAsync()
        => ExecuteAsync(ResetSql);

    public async Task ExecuteAsync(string sql)
    {
        await using var connection = await OpenConnectionAsync();
        await using var command = new SqlCommand(sql, connection);

        await command.ExecuteNonQueryAsync();
    }

    public async Task<TEntity> ScalarAsync<TEntity>(string sql)
    {
        await using var connection = await OpenConnectionAsync();

        return await ScalarAsync<TEntity>(connection, sql);
    }

    public static async Task<TEntity> ScalarAsync<TEntity>(SqlConnection connection, string sql)
    {
        await using var command = new SqlCommand(sql, connection);
        var value = await command.ExecuteScalarAsync();

        return (TEntity)Convert.ChangeType(value!, typeof(TEntity))!;
    }

    public static async Task<IReadOnlyList<TEntity>> QueryAsync<TEntity>(
        SqlConnection connection,
        string sql,
        Func<SqlDataReader, TEntity> project
    )
    {
        await using var command = new SqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync();

        var rows = new List<TEntity>();

        while (await reader.ReadAsync())
        {
            rows.Add(project(reader));
        }

        return rows;
    }
}

[CollectionDefinition(SqlServerCollection.Name)]
public sealed class SqlServerCollection : ICollectionFixture<SqlServerFixture>
{
    public const string Name = "SqlServer";
}
