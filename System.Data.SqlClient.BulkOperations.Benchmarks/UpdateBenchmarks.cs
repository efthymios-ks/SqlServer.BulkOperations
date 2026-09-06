using BenchmarkDotNet.Attributes;
using System.Data.SqlClient.BulkOperations.Benchmarks.Entities;

namespace System.Data.SqlClient.BulkOperations.Benchmarks;

public class UpdateBenchmarks : DatabaseBenchmark
{
    private const string PerRowSql =
        "UPDATE [dbo].[Books] SET [Title] = @title, [Price] = @price WHERE [Isbn] = @isbn;";

    private const string TvpSql = """
        UPDATE tgt
        SET tgt.[Title] = src.[Title], tgt.[Price] = src.[Price]
        FROM [dbo].[Books] AS tgt
        INNER JOIN @rows AS src ON tgt.[Isbn] = src.[Isbn];
        """;

    private Book[] _books = [];

    protected override void OnIterationSetup()
    {
        _books = Reseed("UPD");

        foreach (var book in _books)
        {
            book.Title += " (edited)";
            book.Price += 1m;
        }
    }

    [Benchmark(Baseline = true, Description = "Hand-written table-valued parameter")]
    public Task TableValuedParameter()
        => Baselines.TableValuedAsync(Connection, _books, TvpSql);

    [Benchmark(Description = "Hand-written one UPDATE per row")]
    public Task PerRow()
        => RunPerRowAsync(_books, PerRowSql, (parameters, book) =>
        {
            parameters.AddWithValue("@title", book.Title);
            parameters.AddWithValue("@price", book.Price);
            parameters.AddWithValue("@isbn", book.Isbn);
        });

    [Benchmark(Description = "Library bulk update")]
    public Task Bulk()
        => BulkOperation.Update(_books)
            .WithMatchOn(book => book.Isbn)
            .WithUpdateColumns(book => new { book.Title, book.Price })
            .WithoutConcurrencyCheck()
            .ExecuteAsync(Connection);

    [Benchmark(Description = "Library bulk update, rowversion checked")]
    public Task BulkWithConcurrencyCheck()
        => BulkOperation.Update(_books)
            .WithMatchOn(book => book.Isbn)
            .WithUpdateColumns(book => new { book.Title, book.Price })
            .WithConcurrencyCheck()
            .ExecuteAsync(Connection);
}
