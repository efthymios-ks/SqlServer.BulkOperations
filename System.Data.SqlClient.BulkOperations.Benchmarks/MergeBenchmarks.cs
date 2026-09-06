using BenchmarkDotNet.Attributes;
using System.Data.SqlClient.BulkOperations.Benchmarks.Entities;

namespace System.Data.SqlClient.BulkOperations.Benchmarks;

public class MergeBenchmarks : DatabaseBenchmark
{
    private const string PerRowSql = """
        MERGE INTO [dbo].[Books] WITH (HOLDLOCK) AS tgt
        USING (SELECT @isbn AS [Isbn]) AS src
            ON tgt.[Isbn] = src.[Isbn]
        WHEN MATCHED THEN
            UPDATE SET tgt.[Title] = @title, tgt.[Price] = @price
        WHEN NOT MATCHED BY TARGET THEN
            INSERT ([Isbn], [Title], [Price], [Edition]) VALUES (@isbn, @title, @price, @edition);
        """;

    private const string TvpSql = """
        MERGE INTO [dbo].[Books] WITH (HOLDLOCK) AS tgt
        USING @rows AS src ON tgt.[Isbn] = src.[Isbn]
        WHEN MATCHED THEN
            UPDATE SET tgt.[Title] = src.[Title], tgt.[Price] = src.[Price]
        WHEN NOT MATCHED BY TARGET THEN
            INSERT ([Isbn], [Title], [Price], [Edition])
            VALUES (src.[Isbn], src.[Title], src.[Price], src.[Edition]);
        """;

    private const string TvpWithOutputSql = """
        MERGE INTO [dbo].[Books] WITH (HOLDLOCK) AS tgt
        USING @rows AS src ON tgt.[Isbn] = src.[Isbn]
        WHEN MATCHED THEN
            UPDATE SET tgt.[Title] = src.[Title], tgt.[Price] = src.[Price]
        WHEN NOT MATCHED BY TARGET THEN
            INSERT ([Isbn], [Title], [Price], [Edition])
            VALUES (src.[Isbn], src.[Title], src.[Price], src.[Edition])
        OUTPUT INSERTED.[Id], INSERTED.[RowVersion], src.[Ordinal];
        """;

    private Book[] _payload = [];

    /// <summary>Half the payload matches a seeded row and half is new, so both merge branches run.</summary>
    protected override void OnIterationSetup()
    {
        Reseed("MRG");

        var existing = Generate(RowCount / 2, "MRG");
        var fresh = Generate(RowCount - RowCount / 2, "NEW");
        _payload = [.. existing, .. fresh];

        foreach (var book in _payload)
        {
            book.Title += " (merged)";
        }
    }

    [Benchmark(Baseline = true, Description = "Hand-written table-valued parameter")]
    public Task TableValuedParameter()
        => Baselines.TableValuedAsync(Connection, _payload, TvpSql);

    [Benchmark(Description = "Hand-written one MERGE per row")]
    public Task PerRow()
        => RunPerRowAsync(_payload, PerRowSql, InsertBenchmarks.Bind);

    [Benchmark(Description = "Library bulk merge")]
    public Task Bulk()
        => BulkOperation.Merge(_payload)
            .WithMatchOn(book => book.Isbn)
            .WithUpdateColumns(book => new { book.Title, book.Price })
            .WithoutConcurrencyCheck()
            .ExecuteAsync(Connection);

    [Benchmark(Description = "Hand-written TVP, identity written back")]
    public Task TableValuedParameterWithWriteBack()
        => Baselines.TableValuedWithWriteBackAsync(Connection, _payload, TvpWithOutputSql);

    [Benchmark(Description = "Library bulk merge, identity written back")]
    public Task BulkWithOutputIdentity()
        => BulkOperation.Merge(_payload)
            .WithMatchOn(book => book.Isbn)
            .WithUpdateColumns(book => new { book.Title, book.Price })
            .WithoutConcurrencyCheck()
            .WithOutputIdentity()
            .ExecuteAsync(Connection);
}
