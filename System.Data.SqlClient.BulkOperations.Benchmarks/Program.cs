using BenchmarkDotNet.Running;
using System.Data.SqlClient.BulkOperations.Benchmarks;

BenchmarkSwitcher
    .FromAssembly(typeof(DatabaseBenchmark).Assembly)
    .Run(args);
