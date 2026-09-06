# System.Data.SqlClient.BulkOperations

Bulk insert / update / delete / merge for SQL Server over `Microsoft.Data.SqlClient`. Entities are mapped from `System.ComponentModel.DataAnnotations` attributes — no EF Core dependency.

```csharp
await BulkOperation.Insert(books).ExecuteAsync(connection);
```

Rows stream through `SqlBulkCopy` into a session-scoped staging table, then one set-based statement applies them. A plain insert skips staging entirely.

## When this earns its place

- **Replacing per-row loops — the big win.** 25–100x on 10 000 rows. That gain is the gain of bulk
  technique over one round trip per row; any correct bulk approach would get it.
- **Replacing hand-written `SqlBulkCopy` or table-valued parameters — a small loss.** 1.16–1.37x
  slower than SQL you tuned yourself. You are buying ergonomics, not throughput.
- **Writing generated values back onto your objects.** Identity, computed columns and rowversion,
  matched to the right source object. `SqlBulkCopy` cannot do this at all; by hand it needs a
  table-valued parameter, an `OUTPUT` clause and an ordinal column to map rows back.
- **Merge, insert-if-missing, delete-when-not-matched.** Upsert semantics without writing and
  maintaining the staging DDL and the `MERGE` yourself.
- **Optimistic concurrency across a whole batch.** One rowversion comparison per row, in the same
  statement, with the shortfall raised as an exception.
- **Versus EF Core: not measured here.** EF Core batches several statements per round trip, so it
  sits between per-row and bulk and drifts toward per-row as the batch grows. If you want that
  number for your workload, add it to the benchmark project rather than trusting an estimate.
- **Where it adds little:** a plain insert with no write-back, when you are already comfortable with
  `SqlBulkCopy`. The fast path here *is* `SqlBulkCopy`, plus attribute mapping.

## Options

`✓` supported · `–` not applicable.

### Shared

| Option | Default | Insert | Update | Delete | Merge |
| --- | --- | :-: | :-: | :-: | :-: |
| `WithTable(name)` | `[Table]` or type name | ✓ | ✓ | ✓ | ✓ |
| `WithSchema(name)` | `[Table]` or `dbo` | ✓ | ✓ | ✓ | ✓ |
| `WithColumns(selector)` | every mapped property | ✓ | ✓ | ✓ | ✓ |
| `WithColumnMappings(map)` | `[Column]` or property name | ✓ | ✓ | ✓ | ✓ |
| `WithBatchSize(n)` | `5000` | ✓ | ✓ | ✓ | ✓ |
| `WithBulkCopyTimeout(s)` | `300` (`0` = none) | ✓ | ✓ | ✓ | ✓ |
| `WithCommandTimeout(s)` | `300` (`0` = none) | ✓ | ✓ | ✓ | ✓ |
| `WithBulkCopyOptions(o)` | `TableLock \| KeepNulls` | ✓ | ✓ | ✓ | ✓ |
| `WithIsolationLevel(l)` | `ReadCommitted` | ✓ | ✓ | ✓ | ✓ |
| `WithRetry(n, delay)` | `3`, `200ms` | ✓ | ✓ | ✓ | ✓ |
| `WithOrderedKeyScan()` / `Without…` | on | ✓ | ✓ | ✓ | ✓ |
| `WithProgress(callback)` | none | ✓ | ✓ | ✓ | ✓ |
| `WithLogger(logger)` | none | ✓ | ✓ | ✓ | ✓ |
| `WithTempTablePrefix(p)` | `#bulk_` | ✓ | ✓ | ✓ | ✓ |

Retries apply only when the operation owns the transaction; a caller's transaction is never retried into.

### Per operation

| Option | Default | Insert | Update | Delete | Merge |
| --- | --- | :-: | :-: | :-: | :-: |
| `WithMatchOn(selector)` | `[Key]` columns | – | ✓ | ✓ | ✓ |
| `WithIdentityColumn(selector)` | `[DatabaseGenerated(Identity)]` | ✓ | ✓ | – | – |
| `WithInsertColumns(selector)` | writable, non-identity | – | – | – | ✓ |
| `WithUpdateColumns(selector)` | writable, non-key, non-token | – | ✓ | – | ✓ |
| `WithKeepIdentity()` | off | ✓ | – | – | – |
| `WithInsertIfMissing(selector?)` | off | ✓ | – | – | – |
| `WithDeleteWhenNotMatched()` | off | – | – | – | ✓ |
| `WithDuplicateKeys(behavior)` | `Deduplicate` | – | – | – | ✓ |
| `WithConcurrencyCheck()` / `Without…` | on when entity has a token | – | ✓ | ✓ | ✓ |
| `WithThrowOnConcurrencyMismatch()` | off | – | ✓ | ✓ | ✓ |
| `WithRequireAllMatched()` | off | – | ✓ | – | – |
| `WithOutput(selector)` | none | ✓ | ✓ | – | ✓ |
| `WithOutputIdentity()` | none | ✓ | ✓ | – | ✓ |

`WithKeepIdentity` cannot be combined with output. An insert never checks concurrency. On a merge
the shortfall is counted as inserts plus updates, so rows removed by `WithDeleteWhenNotMatched` are
not mistaken for rows that applied.

### Execute

| Overload | Transaction |
| --- | --- |
| `ExecuteAsync(connection, ct)` | opened and committed here |
| `ExecuteAsync(connection, transaction, ct)` | the caller's — never committed here |
| `ExecuteAsync(transaction, ct)` | the caller's, connection taken from it |

Returns `BulkResult(Inserted, Updated, Deleted, TotalAffected, Elapsed, Retries)`.

## Mapping

| Attribute | Effect |
| --- | --- |
| `[Table("Books", Schema = "dbo")]` | target table; defaults to type name in `dbo` |
| `[Column("actual_name", TypeName = "varchar(50)")]` | column name and declared type |
| `[Key]` | match key; falls back to `Id` / `<Type>Id` |
| `[DatabaseGenerated(Identity \| Computed)]` | excluded from writes, available to output |
| `[Timestamp]` | rowversion, used as the concurrency token |
| `[ConcurrencyCheck]` | concurrency token |
| `[Required]`, `[MaxLength]`, `[StringLength]` | nullability and length in the staging table |
| `[NotMapped]` | ignored |

## Examples

Insert, writing identity, computed and rowversion values back onto the objects:

```csharp
var result = await BulkOperation.Insert(books)
    .WithBatchSize(10_000)
    .WithProgress(rows => Console.WriteLine($"{rows} copied"))
    .WithLogger(logger)
    .WithOutputIdentity()
    .ExecuteAsync(connection, cancellationToken);

Console.WriteLine($"{result.Inserted} in {result.Elapsed.TotalMilliseconds:F0}ms");
Console.WriteLine(books[0].Id);
```

Update on a natural key, restricted columns, optimistic concurrency, refreshed tokens:

```csharp
await BulkOperation.Update(books)
    .WithMatchOn(book => book.Isbn)
    .WithUpdateColumns(book => new { book.Title, book.Price })
    .WithConcurrencyCheck()
    .WithThrowOnConcurrencyMismatch()
    .WithRequireAllMatched()
    .WithOutputIdentity()
    .WithIsolationLevel(IsolationLevel.Serializable)
    .WithRetry(maxRetries: 5, baseDelay: TimeSpan.FromMilliseconds(100))
    .ExecuteAsync(connection);
```

Merge mirroring a list into the table, on a DTO whose property names differ from the columns:

```csharp
await BulkOperation.Merge(rows)
    .WithTable("Widgets")
    .WithSchema("dbo")
    .WithColumnMappings(new Dictionary<string, string>
    {
        ["Id"] = "WidgetId",
        ["Label"] = "Name",
        ["Count"] = "Quantity"
    })
    .WithMatchOn(row => row.Id)
    .WithInsertColumns(row => new { row.Label, row.Count })
    .WithUpdateColumns(row => row.Label)
    .WithDeleteWhenNotMatched()
    .WithDuplicateKeys(DuplicateKeyBehavior.Throw)
    .WithoutConcurrencyCheck()
    .WithOutputIdentity()
    .ExecuteAsync(connection);
```

Several operations in one caller-owned transaction, with a slim DTO for the delete:

```csharp
await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync();

await BulkOperation.Insert(newBooks)
    .WithKeepIdentity()
    .WithColumns(book => new { book.Id, book.Isbn, book.Title, book.Price })
    .ExecuteAsync(connection, transaction);

await BulkOperation.Insert(maybeNew)
    .WithInsertIfMissing(book => book.Isbn)
    .ExecuteAsync(connection, transaction);

await BulkOperation.Delete(isbns)
    .WithTable("Books")
    .WithMatchOn(row => row.Isbn)
    .WithoutOrderedKeyScan()
    .ExecuteAsync(connection, transaction);

await transaction.CommitAsync();
```

## Errors

| Exception | Raised when |
| --- | --- |
| `BulkConfigurationException` | the operation cannot be planned — bad selector, missing match keys, impossible option pair |
| `BulkConcurrencyException` | `WithThrowOnConcurrencyMismatch` and fewer rows changed than were sent |
| `BulkNotMatchedException` | `WithRequireAllMatched` and an item matched no row |
| `BulkExecutionException` | a SQL Server error that survived the retry policy |

All derive from `BulkOperationException`.

## Benchmarks

```
dotnet run -c Release --project System.Data.SqlClient.BulkOperations.Benchmarks -- --filter '*'
```

Runs against a real database. With no configuration a SQL Server 2022 container is started for the
run (Docker must be running); set `SQLBULKOPS_BENCHMARK_CONNECTION_STRING` to measure against your
own server instead. Filter to one suite with `--filter '*InsertBenchmarks*'`.

Every operation is measured against the best hand-written alternative, not just against the naive
one — `SqlBulkCopy` for insert, a table-valued parameter for update, delete and merge. One round
trip per row is included as the floor.

**10 000 rows**, local container (i5-10400, SQL Server 2022 in Docker), lower is better:

| | Insert | Update | Delete | Merge |
| --- | ---: | ---: | ---: | ---: |
| Hand-written `SqlBulkCopy` | **65 ms** | – | – | – |
| Hand-written table-valued parameter | 103 ms | **110 ms** | **191 ms** | **144 ms** |
| **This library** | 77 ms | 144 ms | 221 ms | 196 ms |
| …with rowversion checked | – | 178 ms | 288 ms | – |
| …with generated values written back | 140 ms | – | – | 207 ms |
| Hand-written batched statements | 1 275 ms | – | 606 ms | – |
| Hand-written one round trip per row | 6 544 ms | 6 935 ms | 7 225 ms | 8 311 ms |

Read it as two separate facts:

- **25–100x faster than one round trip per row.** That is the win, and it is entirely the win of
  bulk technique over per-row — not of this library specifically.
- **1.16–1.37x the cost of hand-optimised SQL.** Against a `SqlBulkCopy` or a table-valued parameter
  you wrote yourself, this library is slightly slower. What it gives back is that you write none of
  it, plus merge semantics, optimistic concurrency and write-back of generated values.

Writing generated values back costs an insert its direct-copy path — it has to stage and MERGE, so
it roughly doubles. `SqlBulkCopy` cannot do this at all; the hand-written equivalent is a TVP plus
`OUTPUT` plus a reader loop, which comes out at 123 ms.

Absolute times are dominated by the container and will not match yours; the ratios are the point.
