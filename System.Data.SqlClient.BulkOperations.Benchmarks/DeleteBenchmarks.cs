using BenchmarkDotNet.Attributes;
using System.Data.SqlClient.BulkOperations.Benchmarks.Entities;

namespace System.Data.SqlClient.BulkOperations.Benchmarks;

public class DeleteBenchmarks : DatabaseBenchmark
{
    private const string PerRowSql = "DELETE FROM [dbo].[Books] WHERE [Isbn] = @isbn;";

    private const string TvpSql = """
        DELETE tgt
        FROM [dbo].[Books] AS tgt
        INNER JOIN @rows AS src ON tgt.[Isbn] = src.[Isbn];
        """;

    private Book[] _books = [];

    protected override void OnIterationSetup()
        => _books = Reseed("DEL");

    [Benchmark(Baseline = true, Description = "Hand-written table-valued parameter")]
    public Task TableValuedParameter()
        => Baselines.TableValuedAsync(Connection, _books, TvpSql);

    [Benchmark(Description = "Hand-written batched WHERE IN")]
    public Task BatchedIn()
        => Baselines.BatchedDeleteAsync(Connection, _books);

    [Benchmark(Description = "Hand-written one DELETE per row")]
    public Task PerRow()
        => RunPerRowAsync(_books, PerRowSql, (parameters, book) => parameters.AddWithValue("@isbn", book.Isbn));

    [Benchmark(Description = "Library bulk delete")]
    public Task Bulk()
        => BulkOperation.Delete(_books)
            .WithMatchOn(book => book.Isbn)
            .WithoutConcurrencyCheck()
            .ExecuteAsync(Connection);

    [Benchmark(Description = "Library bulk delete, rowversion checked")]
    public Task BulkWithConcurrencyCheck()
        => BulkOperation.Delete(_books)
            .WithMatchOn(book => book.Isbn)
            .WithConcurrencyCheck()
            .ExecuteAsync(Connection);
}
