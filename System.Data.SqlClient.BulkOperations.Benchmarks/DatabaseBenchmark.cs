using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Engines;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Loggers;
using BenchmarkDotNet.Order;
using BenchmarkDotNet.Toolchains.InProcess.Emit;
using Microsoft.Data.SqlClient;
using System.Data.SqlClient.BulkOperations.Benchmarks.Entities;

namespace System.Data.SqlClient.BulkOperations.Benchmarks;

/// <summary>
/// Round trips to SQL Server dominate every measurement here, so the usual micro-benchmark
/// machinery is turned down: run the body once per iteration, in this process, and just watch the
/// clock. One invocation per iteration is what keeps the per-iteration reseed out of the timing.
/// </summary>
public sealed class DatabaseConfig : ManualConfig
{
    public DatabaseConfig()
    {
        AddJob(Job.Default
            .WithStrategy(RunStrategy.Monitoring)
            .WithLaunchCount(1)
            .WithWarmupCount(1)
            .WithIterationCount(5)
            .WithInvocationCount(1)
            .WithUnrollFactor(1)

            // The out-of-process toolchain resolves the project by assembly name, which does not
            // match this csproj, and every child process would start a container of its own.
            .WithToolchain(InProcessEmitToolchain.Instance));

        // No MemoryDiagnoser: each iteration reseeds the table from managed code, so the allocation
        // it reports is the setup as much as the operation. Wall time is the honest measurement here.
        AddColumnProvider(DefaultColumnProviders.Instance);
        AddLogger(ConsoleLogger.Default);
        WithOrderer(new DefaultOrderer(SummaryOrderPolicy.FastestToSlowest));
        WithOptions(ConfigOptions.DisableOptimizationsValidator);
    }
}

[Config(typeof(DatabaseConfig))]
public abstract class DatabaseBenchmark
{
    private readonly SqlServerHarness _harness = new();

    protected SqlConnection Connection { get; private set; } = null!;

    [Params(1_000, 10_000)]
    public int RowCount { get; set; }

    [GlobalSetup]
    public void GlobalSetup()
    {
        _harness.StartAsync().GetAwaiter().GetResult();
        Connection = _harness.OpenConnectionAsync().GetAwaiter().GetResult();
    }

    [GlobalCleanup]
    public void GlobalCleanup()
    {
        Connection.Dispose();
        _harness.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    [IterationSetup]
    public void IterationSetup()
        => OnIterationSetup();

    protected abstract void OnIterationSetup();

    protected void Truncate()
        => _harness.TruncateAsync().GetAwaiter().GetResult();

    protected Book[] Reseed(string prefix)
        => _harness.ReseedAsync(RowCount, prefix).GetAwaiter().GetResult();

    protected static Book[] Generate(int rowCount, string prefix)
        => SqlServerHarness.Generate(rowCount, prefix);

    /// <summary>
    /// The naive alternative every bulk operation is measured against: one round trip per row,
    /// wrapped in a single transaction so the comparison is not just commit overhead.
    /// </summary>
    protected async Task RunPerRowAsync<TEntity>(
        IReadOnlyList<TEntity> items,
        string sql,
        Action<SqlParameterCollection, TEntity> bind
    )
    {
        await using var transaction = (SqlTransaction)await Connection.BeginTransactionAsync();
        await using var command = new SqlCommand(sql, Connection, transaction) { CommandTimeout = 0 };

        foreach (var item in items)
        {
            command.Parameters.Clear();
            bind(command.Parameters, item);
            await command.ExecuteNonQueryAsync();
        }

        await transaction.CommitAsync();
    }
}
