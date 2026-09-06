using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Data.SqlClient.Server;
using System.Data.SqlClient.BulkOperations.Benchmarks.Entities;

namespace System.Data.SqlClient.BulkOperations.Benchmarks;

/// <summary>
/// What you would write by hand instead of using this library. These are the comparisons that
/// decide whether it earns its place; one round trip per row only shows what everyone already knows.
/// </summary>
public static class Baselines
{
    public const string TableTypeName = "dbo.BookTableType";

    /// <summary>SQL Server caps INSERT ... VALUES at 1000 rows and a statement at 2100 parameters.</summary>
    private const int MaxRowsPerStatement = 500;

    private static readonly SqlMetaData[] _tableTypeColumns =
    [
        new("Ordinal", SqlDbType.Int),
        new("Isbn", SqlDbType.NVarChar, 20),
        new("Title", SqlDbType.NVarChar, 200),
        new("Price", SqlDbType.Decimal, precision: 18, scale: 2),
        new("Edition", SqlDbType.Int)
    ];

    /// <summary>
    /// The hand-written equivalent of the library's insert fast path. Most code reaches for a
    /// DataTable here, which is why the allocation column is worth reading next to the timing.
    /// </summary>
    public static async Task SqlBulkCopyAsync(SqlConnection connection, IReadOnlyList<Book> books)
    {
        var table = new DataTable();
        table.Columns.Add("Isbn", typeof(string));
        table.Columns.Add("Title", typeof(string));
        table.Columns.Add("Price", typeof(decimal));
        table.Columns.Add("Edition", typeof(int));

        foreach (var book in books)
        {
            table.Rows.Add(book.Isbn, book.Title, book.Price, book.Edition);
        }

        using var bulkCopy = new SqlBulkCopy(connection)
        {
            DestinationTableName = "[dbo].[Books]",
            BulkCopyTimeout = 0
        };

        foreach (DataColumn column in table.Columns)
        {
            bulkCopy.ColumnMappings.Add(column.ColumnName, column.ColumnName);
        }

        await bulkCopy.WriteToServerAsync(table);
    }

    /// <summary>One round trip carrying every row as a table-valued parameter.</summary>
    public static async Task TableValuedAsync(
        SqlConnection connection,
        IReadOnlyList<Book> books,
        string sql
    )
    {
        await using var command = new SqlCommand(sql, connection) { CommandTimeout = 0 };
        AddRowsParameter(command, books);

        await command.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// A table-valued parameter plus OUTPUT, then reading the generated ids back onto the source
    /// objects — the closest hand-written equivalent of WithOutputIdentity.
    /// </summary>
    public static async Task TableValuedWithWriteBackAsync(
        SqlConnection connection,
        IReadOnlyList<Book> books,
        string sql
    )
    {
        await using var command = new SqlCommand(sql, connection) { CommandTimeout = 0 };
        AddRowsParameter(command, books);

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            var book = books[reader.GetInt32(2)];
            book.Id = reader.GetInt32(0);
            book.RowVersion = (byte[])reader.GetValue(1);
        }
    }

    /// <summary>Chunked multi-row statements: no staging table, but one round trip per chunk.</summary>
    public static async Task BatchedValuesAsync(SqlConnection connection, IReadOnlyList<Book> books)
    {
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync();

        for (var offset = 0; offset < books.Count; offset += MaxRowsPerStatement)
        {
            var count = Math.Min(MaxRowsPerStatement, books.Count - offset);
            await using var command = new SqlCommand(string.Empty, connection, transaction) { CommandTimeout = 0 };

            var sql = new System.Text.StringBuilder("INSERT INTO [dbo].[Books] ([Isbn], [Title], [Price], [Edition]) VALUES ");

            for (var index = 0; index < count; index++)
            {
                var book = books[offset + index];

                if (index > 0)
                {
                    sql.Append(", ");
                }

                sql.Append($"(@i{index}, @t{index}, @p{index}, @e{index})");
                command.Parameters.AddWithValue($"@i{index}", book.Isbn);
                command.Parameters.AddWithValue($"@t{index}", book.Title);
                command.Parameters.AddWithValue($"@p{index}", book.Price);
                command.Parameters.AddWithValue($"@e{index}", book.Edition);
            }

            command.CommandText = sql.Append(';').ToString();
            await command.ExecuteNonQueryAsync();
        }

        await transaction.CommitAsync();
    }

    /// <summary>Chunked DELETE ... WHERE [Isbn] IN (…), the usual alternative when there is no staging table.</summary>
    public static async Task BatchedDeleteAsync(SqlConnection connection, IReadOnlyList<Book> books)
    {
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync();

        for (var offset = 0; offset < books.Count; offset += MaxRowsPerStatement)
        {
            var count = Math.Min(MaxRowsPerStatement, books.Count - offset);
            await using var command = new SqlCommand(string.Empty, connection, transaction) { CommandTimeout = 0 };

            var names = new string[count];

            for (var index = 0; index < count; index++)
            {
                names[index] = $"@i{index}";
                command.Parameters.AddWithValue(names[index], books[offset + index].Isbn);
            }

            command.CommandText = $"DELETE FROM [dbo].[Books] WHERE [Isbn] IN ({string.Join(", ", names)});";
            await command.ExecuteNonQueryAsync();
        }

        await transaction.CommitAsync();
    }

    private static void AddRowsParameter(SqlCommand command, IReadOnlyList<Book> books)
    {
        var parameter = command.Parameters.AddWithValue("@rows", ToRecords(books));
        parameter.SqlDbType = SqlDbType.Structured;
        parameter.TypeName = TableTypeName;
    }

    /// <summary>
    /// Streams the rows to the server. One record instance is reused throughout, which is the
    /// documented pattern and what keeps a table-valued parameter competitive with a bulk copy.
    /// </summary>
    private static IEnumerable<SqlDataRecord> ToRecords(IReadOnlyList<Book> books)
    {
        var record = new SqlDataRecord(_tableTypeColumns);

        for (var index = 0; index < books.Count; index++)
        {
            var book = books[index];
            record.SetInt32(0, index);
            record.SetString(1, book.Isbn);
            record.SetString(2, book.Title);
            record.SetDecimal(3, book.Price);
            record.SetInt32(4, book.Edition);

            yield return record;
        }
    }
}
