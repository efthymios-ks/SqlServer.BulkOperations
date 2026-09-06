using BenchmarkDotNet.Attributes;
using Microsoft.Data.SqlClient;
using System.Data.SqlClient.BulkOperations.Benchmarks.Entities;

namespace System.Data.SqlClient.BulkOperations.Benchmarks;

public class InsertBenchmarks : DatabaseBenchmark
{
    private const string PerRowSql =
        "INSERT INTO [dbo].[Books] ([Isbn], [Title], [Price], [Edition]) VALUES (@isbn, @title, @price, @edition);";

    private const string TvpSql = """
        INSERT INTO [dbo].[Books] ([Isbn], [Title], [Price], [Edition])
        SELECT [Isbn], [Title], [Price], [Edition] FROM @rows ORDER BY [Ordinal];
        """;

    private const string TvpWithOutputSql = """
        MERGE INTO [dbo].[Books] WITH (HOLDLOCK) AS tgt
        USING @rows AS src ON 1 = 0
        WHEN NOT MATCHED BY TARGET THEN
            INSERT ([Isbn], [Title], [Price], [Edition])
            VALUES (src.[Isbn], src.[Title], src.[Price], src.[Edition])
        OUTPUT INSERTED.[Id], INSERTED.[RowVersion], src.[Ordinal];
        """;

    private Book[] _books = [];

    protected override void OnIterationSetup()
    {
        Truncate();
        _books = Generate(RowCount, "INS");
    }

    [Benchmark(Baseline = true, Description = "Hand-written SqlBulkCopy")]
    public Task SqlBulkCopy()
        => Baselines.SqlBulkCopyAsync(Connection, _books);

    [Benchmark(Description = "Hand-written table-valued parameter")]
    public Task TableValuedParameter()
        => Baselines.TableValuedAsync(Connection, _books, TvpSql);

    [Benchmark(Description = "Hand-written batched VALUES")]
    public Task BatchedValues()
        => Baselines.BatchedValuesAsync(Connection, _books);

    [Benchmark(Description = "Hand-written one INSERT per row")]
    public Task PerRow()
        => RunPerRowAsync(_books, PerRowSql, Bind);

    [Benchmark(Description = "Library bulk insert")]
    public Task Bulk()
        => BulkOperation.Insert(_books).ExecuteAsync(Connection);

    [Benchmark(Description = "Library bulk insert if missing")]
    public Task BulkInsertIfMissing()
        => BulkOperation.Insert(_books)
            .WithInsertIfMissing(book => book.Isbn)
            .ExecuteAsync(Connection);

    [Benchmark(Description = "Hand-written TVP, identity written back")]
    public Task TableValuedParameterWithWriteBack()
        => Baselines.TableValuedWithWriteBackAsync(Connection, _books, TvpWithOutputSql);

    [Benchmark(Description = "Library bulk insert, identity written back")]
    public Task BulkWithOutputIdentity()
        => BulkOperation.Insert(_books)
            .WithOutputIdentity()
            .ExecuteAsync(Connection);

    internal static void Bind(SqlParameterCollection parameters, Book book)
    {
        parameters.AddWithValue("@isbn", book.Isbn);
        parameters.AddWithValue("@title", book.Title);
        parameters.AddWithValue("@price", book.Price);
        parameters.AddWithValue("@edition", book.Edition);
    }
}
