using System.Data.SqlClient.BulkOperations.Execution;
using System.Data.SqlClient.BulkOperations.Metadata;
using System.Data.SqlClient.BulkOperations.Tests.Shared;
using Xunit;

namespace System.Data.SqlClient.BulkOperations.Tests.Unit.Execution;

public class TempTableBuilderTests
{
    private readonly EntityMetadata _metadata = MetadataCache.For<Book>();

    [Fact]
    public void BuildCreateTable_WhenBuilt_ShouldQuoteTheTableNameAndDeclareEveryStagingColumn()
    {
        // Arrange & Act
        var sql = Build(staging: ["Isbn", "Title", "Price"], match: []);

        // Assert
        Assert.StartsWith("CREATE TABLE [#bulk_x] (", sql);
        Assert.Contains("[Isbn] nvarchar(20)", sql);
        Assert.Contains("[Title] nvarchar(200)", sql);
        Assert.Contains("[Price] decimal(18,2)", sql);
        Assert.EndsWith(");" + Environment.NewLine, sql);
    }

    [Fact]
    public void BuildCreateTable_WhenAColumnIsACharacterType_ShouldCollateItToTheDatabaseDefault()
    {
        // Arrange & Act
        var sql = Build(staging: ["Isbn", "Price"], match: []);

        // Assert
        Assert.Contains("[Isbn] nvarchar(20) COLLATE DATABASE_DEFAULT", sql);
        Assert.DoesNotContain("[Price] decimal(18,2) COLLATE", sql);
    }

    [Fact]
    public void BuildCreateTable_WhenTheOrdinalIsIncluded_ShouldDeclareItFirst()
    {
        // Arrange
        var sql = Build(staging: ["Isbn"], match: []);

        // Act
        var ordinalIndex = sql.IndexOf("[__bulk_index] int NOT NULL", StringComparison.Ordinal);
        var isbnIndex = sql.IndexOf("[Isbn]", StringComparison.Ordinal);

        // Assert
        Assert.True(ordinalIndex > 0);
        Assert.True(ordinalIndex < isbnIndex);
    }

    [Fact]
    public void BuildCreateTable_WhenThereAreNoMatchColumns_ShouldClusterOnTheOrdinal()
    {
        // Arrange & Act
        var sql = Build(staging: ["Isbn"], match: []);

        // Assert
        Assert.Contains("PRIMARY KEY CLUSTERED ([__bulk_index])", sql);
    }

    [Fact]
    public void BuildCreateTable_WhenTheMatchIsUnique_ShouldMakeItThePrimaryKey()
    {
        // Arrange & Act
        var sql = Build(staging: ["Isbn", "Title"], match: ["Isbn"], matchIsUnique: true);

        // Assert
        Assert.Contains("PRIMARY KEY CLUSTERED ([Isbn])", sql);
        Assert.DoesNotContain("__bulk_index])", sql);
    }

    [Fact]
    public void BuildCreateTable_WhenTheMatchIsNotUnique_ShouldUseAnIndexEndingInTheOrdinal()
    {
        // Arrange & Act
        var sql = Build(staging: ["Isbn", "Title"], match: ["Isbn"], matchIsUnique: false);

        // Assert
        Assert.Contains("INDEX IX_bulk CLUSTERED ([Isbn], [__bulk_index])", sql);
        Assert.DoesNotContain("PRIMARY KEY", sql);
    }

    [Fact]
    public void BuildCreateTable_WhenAMatchColumnIsOptional_ShouldStillDeclareItNotNull()
    {
        // Arrange & Act
        var sql = Build(staging: ["RowVersion"], match: ["RowVersion"], matchIsUnique: false);

        // Assert
        Assert.Contains("[RowVersion] binary(8) NOT NULL", sql);
    }

    [Fact]
    public void BuildCreateTable_WhenAnOptionalColumnIsNotAMatchColumn_ShouldDeclareItNullable()
    {
        // Arrange & Act
        var sql = Build(staging: ["RowVersion"], match: []);

        // Assert
        Assert.Contains("[RowVersion] binary(8) NULL", sql);
    }

    [Fact]
    public void BuildCreateTable_WhenBuilt_ShouldSeparateDefinitionsWithCommasAndNotTrailOne()
    {
        // Arrange & Act
        var sql = Build(staging: ["Isbn", "Title"], match: ["Isbn"], matchIsUnique: true);

        // Assert
        Assert.DoesNotContain(",\r\n)", sql);
        Assert.DoesNotContain(",\n)", sql);
        Assert.Equal(3, sql.Count(character => character == ','));
    }

    [Fact]
    public void BuildCreateTable_WhenTheTableNameHasAClosingBracket_ShouldEscapeIt()
    {
        // Arrange & Act
        var sql = TempTableBuilder.BuildCreateTable(
            tempTableName: "#odd]name",
            stagingColumns: Columns("Isbn"),
            matchColumns: [],
            includeOrdinal: false,
            matchIsUnique: false
        );

        // Assert
        Assert.StartsWith("CREATE TABLE [#odd]]name] (", sql);
    }

    private string Build(
        string[] staging,
        string[] match,
        bool matchIsUnique = false,
        bool includeOrdinal = true
    ) => TempTableBuilder.BuildCreateTable(
        tempTableName: "#bulk_x",
        stagingColumns: Columns(staging),
        matchColumns: Columns(match),
        includeOrdinal: includeOrdinal,
        matchIsUnique: matchIsUnique
    );

    private ColumnMetadata[] Columns(params string[] propertyNames)
        => [.. propertyNames.Select(name => _metadata.ByPropertyName[name])];
}
