using System.Data.SqlClient.BulkOperations.Execution;
using System.Data.SqlClient.BulkOperations.Metadata;
using System.Data.SqlClient.BulkOperations.Tests.Shared;
using Xunit;

namespace System.Data.SqlClient.BulkOperations.Tests.Unit.Execution;

public class SqlStatementBuilderTests
{
    private const string Target = "[dbo].[Books]";

    private const string TempTable = "#bulk_x";

    private readonly EntityMetadata _metadata = MetadataCache.For<Book>();

    [Fact]
    public void BuildInsertWithOutput_WhenOutputColumnsAreGiven_ShouldInsertEveryStagedRowAndReturnRows()
    {
        // Arrange & Act
        var statement = InsertWithOutput(output: ["Id"]);

        // Assert
        Assert.Contains("USING [#bulk_x] AS src", statement.Sql);
        Assert.Contains("WHEN NOT MATCHED BY TARGET THEN", statement.Sql);
        Assert.Contains("INSERT ([Isbn], [Title], [Price]) VALUES (src.[Isbn], src.[Title], src.[Price])", statement.Sql);
        Assert.Contains("OUTPUT INSERTED.[Id], src.[__bulk_index]", statement.Sql);
        Assert.True(statement.ReturnsRows);
    }

    [Fact]
    public void BuildInsertWithOutput_WhenBuilt_ShouldUseAPredicateThatCanNeverMatch()
    {
        // Arrange & Act
        // A plain INSERT cannot output source columns, so this is a MERGE that only ever inserts.
        var statement = InsertWithOutput(output: ["Id"]);

        // Assert
        Assert.Contains("ON 1 = 0", statement.Sql);
        Assert.DoesNotContain("WHEN MATCHED", statement.Sql);
        Assert.DoesNotContain("BY SOURCE", statement.Sql);
        Assert.DoesNotContain("$action", statement.Sql);
    }

    [Fact]
    public void BuildInsertWithOutput_WhenSeveralOutputColumnsAreGiven_ShouldListThemAllBeforeTheOrdinal()
    {
        // Arrange & Act
        var statement = InsertWithOutput(output: ["Id", "CreatedUtc", "RowVersion"]);

        // Assert
        Assert.Contains(
            "OUTPUT INSERTED.[Id], INSERTED.[CreatedUtc], INSERTED.[RowVersion], src.[__bulk_index]",
            statement.Sql
        );
    }

    [Fact]
    public void BuildInsertIfMissing_WhenNoOutputIsRequested_ShouldGuardWithNotExistsAndAvoidMerge()
    {
        // Arrange & Act
        var statement = SqlStatementBuilder.BuildInsertIfMissing(
            targetQualified: Target,
            tempTable: TempTable,
            insertColumns: Columns("Isbn", "Title", "Price"),
            matchColumns: Columns("Isbn"),
            outputColumns: []
        );

        // Assert
        Assert.Contains("WHERE NOT EXISTS (SELECT 1 FROM [dbo].[Books] AS tgt WHERE tgt.[Isbn] = src.[Isbn]);", statement.Sql);
        Assert.DoesNotContain("MERGE", statement.Sql);
        Assert.False(statement.ReturnsRows);
    }

    [Fact]
    public void BuildInsertIfMissing_WhenOutputColumnsAreGiven_ShouldMergeOnTheMatchKeyAndReturnRows()
    {
        // Arrange & Act
        var statement = SqlStatementBuilder.BuildInsertIfMissing(
            targetQualified: Target,
            tempTable: TempTable,
            insertColumns: Columns("Isbn"),
            matchColumns: Columns("Isbn"),
            outputColumns: Columns("Id")
        );

        // Assert
        Assert.Contains("ON tgt.[Isbn] = src.[Isbn]", statement.Sql);
        Assert.Contains("WHEN NOT MATCHED BY TARGET THEN", statement.Sql);
        Assert.DoesNotContain("WHEN MATCHED", statement.Sql);
        Assert.Contains("OUTPUT INSERTED.[Id], src.[__bulk_index]", statement.Sql);
        Assert.True(statement.ReturnsRows);
    }

    [Fact]
    public void BuildUpdate_WhenBuilt_ShouldJoinStagingOnTheMatchColumns()
    {
        // Arrange & Act
        var statement = Update(concurrency: [], output: []);

        // Assert
        Assert.Contains("UPDATE tgt", statement.Sql);
        Assert.Contains("SET tgt.[Title] = src.[Title], tgt.[Price] = src.[Price]", statement.Sql);
        Assert.Contains("INNER JOIN [#bulk_x] AS src ON tgt.[Isbn] = src.[Isbn]", statement.Sql);
        Assert.EndsWith(";" + Environment.NewLine, statement.Sql);
        Assert.False(statement.ReturnsRows);
    }

    [Fact]
    public void BuildUpdate_WhenConcurrencyColumnsAreGiven_ShouldAddThemToTheWhereClause()
    {
        // Arrange & Act
        var statement = Update(concurrency: ["RowVersion"], output: []);

        // Assert
        Assert.Contains("WHERE tgt.[RowVersion] = src.[RowVersion]", statement.Sql);
    }

    [Fact]
    public void BuildUpdate_WhenOutputColumnsAreGiven_ShouldReturnRows()
    {
        // Arrange & Act
        var statement = Update(concurrency: [], output: ["RowVersion"]);

        // Assert
        Assert.Contains("OUTPUT INSERTED.[RowVersion], src.[__bulk_index]", statement.Sql);
        Assert.True(statement.ReturnsRows);
    }

    [Fact]
    public void BuildDelete_WhenBuilt_ShouldJoinStagingAndNeverReturnRows()
    {
        // Arrange & Act
        var statement = SqlStatementBuilder.BuildDelete(
            targetQualified: Target,
            tempTable: TempTable,
            matchColumns: Columns("Isbn"),
            concurrencyColumns: []
        );

        // Assert
        Assert.Contains("DELETE tgt", statement.Sql);
        Assert.Contains("INNER JOIN [#bulk_x] AS src ON tgt.[Isbn] = src.[Isbn]", statement.Sql);
        Assert.False(statement.ReturnsRows);
    }

    [Fact]
    public void BuildDelete_WhenConcurrencyColumnsAreGiven_ShouldAddThemToTheWhereClause()
    {
        // Arrange & Act
        var statement = SqlStatementBuilder.BuildDelete(
            targetQualified: Target,
            tempTable: TempTable,
            matchColumns: Columns("Isbn"),
            concurrencyColumns: Columns("RowVersion")
        );

        // Assert
        Assert.Contains("WHERE tgt.[RowVersion] = src.[RowVersion]", statement.Sql);
    }

    [Fact]
    public void BuildMerge_WhenBuilt_ShouldHoldLockAndReportTheActionPerRow()
    {
        // Arrange & Act
        var statement = Merge();

        // Assert
        Assert.Contains("MERGE INTO [dbo].[Books] WITH (HOLDLOCK) AS tgt", statement.Sql);
        Assert.Contains("USING [#bulk_x] AS src", statement.Sql);
        Assert.Contains("ON tgt.[Isbn] = src.[Isbn]", statement.Sql);
        Assert.Contains("OUTPUT $action", statement.Sql);
        Assert.EndsWith(";", statement.Sql.TrimEnd());
        Assert.True(statement.ReturnsRows);
    }

    [Fact]
    public void BuildMerge_WhenUpdateColumnsAreGiven_ShouldEmitTheMatchedBranch()
    {
        // Arrange & Act
        var statement = Merge(update: ["Title"]);

        // Assert
        Assert.Contains("WHEN MATCHED THEN", statement.Sql);
        Assert.Contains("UPDATE SET tgt.[Title] = src.[Title]", statement.Sql);
    }

    [Fact]
    public void BuildMerge_WhenNoUpdateColumnsAreGiven_ShouldOmitTheMatchedBranch()
    {
        // Arrange & Act
        var statement = Merge(update: []);

        // Assert
        Assert.DoesNotContain("WHEN MATCHED", statement.Sql);
        Assert.Contains("WHEN NOT MATCHED BY TARGET THEN", statement.Sql);
    }

    [Fact]
    public void BuildMerge_WhenConcurrencyColumnsAreGiven_ShouldGuardTheMatchedBranch()
    {
        // Arrange & Act
        var statement = Merge(update: ["Title"], concurrency: ["RowVersion"]);

        // Assert
        Assert.Contains("WHEN MATCHED AND tgt.[RowVersion] = src.[RowVersion] THEN", statement.Sql);
    }

    [Fact]
    public void BuildMerge_WhenDeleteWhenNotMatchedIsSet_ShouldEmitTheNotMatchedBySourceBranch()
    {
        // Arrange & Act
        var statement = Merge(deleteWhenNotMatched: true);

        // Assert
        Assert.Contains("WHEN NOT MATCHED BY SOURCE THEN DELETE", statement.Sql);
    }

    [Fact]
    public void BuildMerge_WhenDeleteWhenNotMatchedIsNotSet_ShouldNotTouchUnmatchedTargetRows()
    {
        // Arrange & Act
        var statement = Merge(deleteWhenNotMatched: false);

        // Assert
        Assert.DoesNotContain("BY SOURCE", statement.Sql);
    }

    [Fact]
    public void BuildMerge_WhenOutputColumnsAreGiven_ShouldListThemBetweenTheActionAndTheOrdinal()
    {
        // Arrange & Act
        var statement = Merge(output: ["Id"]);

        // Assert
        Assert.Contains("OUTPUT $action, INSERTED.[Id], src.[__bulk_index]", statement.Sql);
    }

    private BuiltStatement InsertWithOutput(string[] output)
        => SqlStatementBuilder.BuildInsertWithOutput(
            targetQualified: Target,
            tempTable: TempTable,
            insertColumns: Columns("Isbn", "Title", "Price"),
            outputColumns: Columns(output)
        );

    private BuiltStatement Update(string[] concurrency, string[] output)
        => SqlStatementBuilder.BuildUpdate(
            targetQualified: Target,
            tempTable: TempTable,
            updateColumns: Columns("Title", "Price"),
            matchColumns: Columns("Isbn"),
            concurrencyColumns: Columns(concurrency),
            outputColumns: Columns(output)
        );

    private BuiltStatement Merge(
        string[]? update = null,
        string[]? concurrency = null,
        string[]? output = null,
        bool deleteWhenNotMatched = false
    ) => SqlStatementBuilder.BuildMerge(
        targetQualified: Target,
        tempTable: TempTable,
        insertColumns: Columns("Isbn", "Title", "Price"),
        updateColumns: Columns(update ?? []),
        matchColumns: Columns("Isbn"),
        concurrencyColumns: Columns(concurrency ?? []),
        outputColumns: Columns(output ?? []),
        deleteWhenNotMatched: deleteWhenNotMatched
    );

    private ColumnMetadata[] Columns(params string[] propertyNames)
        => [.. propertyNames.Select(name => _metadata.ByPropertyName[name])];
}
