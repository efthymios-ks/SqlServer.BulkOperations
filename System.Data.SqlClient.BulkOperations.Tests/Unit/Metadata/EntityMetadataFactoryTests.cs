using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Data;
using System.Data.SqlClient.BulkOperations.Exceptions;
using System.Data.SqlClient.BulkOperations.Metadata;
using System.Data.SqlClient.BulkOperations.Tests.Shared;
using Xunit;

namespace System.Data.SqlClient.BulkOperations.Tests.Unit.Metadata;

public class EntityMetadataFactoryTests
{
    [Fact]
    public void Build_WhenTheTypeHasATableAttribute_ShouldReadItsNameAndSchema()
    {
        // Arrange & Act
        var metadata = EntityMetadataFactory.Build(typeof(ShopItem));

        // Assert
        Assert.Equal("shop", metadata.Schema);
        Assert.Equal("Items", metadata.TableName);
        Assert.Equal("[shop].[Items]", metadata.QualifiedName);
    }

    [Fact]
    public void Build_WhenTheTypeHasNoTableAttribute_ShouldUseTheTypeNameAndDbo()
    {
        // Arrange & Act
        var metadata = EntityMetadataFactory.Build(typeof(Plain));

        // Assert
        Assert.Equal("dbo", metadata.Schema);
        Assert.Equal(nameof(Plain), metadata.TableName);
    }

    [Fact]
    public void Build_WhenAPropertyHasAKeyAttribute_ShouldMarkThatColumnAsTheKey()
    {
        // Arrange & Act
        var metadata = EntityMetadataFactory.Build(typeof(Book));

        // Assert
        Assert.Equal(["Id"], metadata.KeyColumns.Select(column => column.PropertyName));
        Assert.Equal("Id", metadata.IdentityColumn?.PropertyName);
    }

    [Fact]
    public void Build_WhenThereIsAnIdPropertyAndNoKeyAttribute_ShouldPromoteItToAnIdentityKey()
    {
        // Arrange & Act
        var metadata = EntityMetadataFactory.Build(typeof(Plain));
        var id = metadata.ByPropertyName["Id"];

        // Assert
        Assert.True(id.IsKey);
        Assert.True(id.IsIdentity);
        Assert.False(id.IsNullable);
    }

    [Fact]
    public void Build_WhenTheKeyFollowsTheTypeNameIdConvention_ShouldPromoteItToo()
    {
        // Arrange & Act
        var metadata = EntityMetadataFactory.Build(typeof(Widget));

        // Assert
        Assert.Equal(["WidgetId"], metadata.KeyColumns.Select(column => column.PropertyName));
    }

    [Fact]
    public void Build_WhenTheIdPropertyIsNotIntegral_ShouldMakeItAKeyButNotAnIdentity()
    {
        // Arrange & Act
        var metadata = EntityMetadataFactory.Build(typeof(GuidKeyed));
        var id = metadata.ByPropertyName["Id"];

        // Assert
        Assert.True(id.IsKey);
        Assert.False(id.IsIdentity);
    }

    [Fact]
    public void Build_WhenAPropertyIsATimestamp_ShouldMarkItAsRowVersionAndConcurrencyToken()
    {
        // Arrange & Act
        var metadata = EntityMetadataFactory.Build(typeof(Book));
        var rowVersion = metadata.ByPropertyName["RowVersion"];

        // Assert
        Assert.True(rowVersion.IsRowVersion);
        Assert.True(rowVersion.IsConcurrencyToken);
        Assert.True(rowVersion.IsStoreGenerated);
        Assert.Equal("binary(8)", rowVersion.SqlTypeDeclaration);
        Assert.Same(rowVersion, metadata.ConcurrencyColumn);
    }

    [Fact]
    public void Build_WhenAColumnIsComputed_ShouldMarkItComputedAndStoreGenerated()
    {
        // Arrange & Act
        var metadata = EntityMetadataFactory.Build(typeof(Book));
        var createdUtc = metadata.ByPropertyName["CreatedUtc"];

        // Assert
        Assert.True(createdUtc.IsComputed);
        Assert.True(createdUtc.IsStoreGenerated);
    }

    [Fact]
    public void Build_WhenAPropertyHasAColumnAttribute_ShouldUseItsNameAndTypeName()
    {
        // Arrange & Act
        var metadata = EntityMetadataFactory.Build(typeof(Annotated));
        var renamed = metadata.ByPropertyName["Renamed"];

        // Assert
        Assert.Equal("actual_column", renamed.ColumnName);
        Assert.Equal("varchar(50)", renamed.SqlTypeDeclaration);
    }

    [Fact]
    public void Build_WhenAPropertyHasAMaxLength_ShouldSizeTheColumnDeclaration()
    {
        // Arrange & Act
        var metadata = EntityMetadataFactory.Build(typeof(Book));

        // Assert
        Assert.Equal("nvarchar(20)", metadata.ByPropertyName["Isbn"].SqlTypeDeclaration);
        Assert.Equal(20, metadata.ByPropertyName["Isbn"].MaxLength);
    }

    [Fact]
    public void Build_WhenAPropertyHasNoMaxLength_ShouldDeclareTheColumnAsMax()
    {
        // Arrange & Act
        var metadata = EntityMetadataFactory.Build(typeof(Plain));

        // Assert
        Assert.Equal("nvarchar(max)", metadata.ByPropertyName["Name"].SqlTypeDeclaration);
    }

    [Fact]
    public void Build_WhenAPropertyIsRequired_ShouldMarkTheColumnNotNullable()
    {
        // Arrange & Act
        var metadata = EntityMetadataFactory.Build(typeof(Book));

        // Assert
        Assert.False(metadata.ByPropertyName["Isbn"].IsNullable);
        Assert.True(metadata.ByPropertyName["RowVersion"].IsNullable);
    }

    [Fact]
    public void Build_WhenThePropertyIsAValueType_ShouldMarkTheColumnNotNullable()
    {
        // Arrange & Act
        var metadata = EntityMetadataFactory.Build(typeof(Book));

        // Assert
        Assert.False(metadata.ByPropertyName["Price"].IsNullable);
    }

    [Fact]
    public void Build_WhenThePropertyIsANullableValueType_ShouldMarkTheColumnNullable()
    {
        // Arrange & Act
        var metadata = EntityMetadataFactory.Build(typeof(Annotated));

        // Assert
        Assert.True(metadata.ByPropertyName["Optional"].IsNullable);
        Assert.Equal(SqlDbType.Int, metadata.ByPropertyName["Optional"].SqlDbType);
    }

    [Fact]
    public void Build_WhenAPropertyHasConcurrencyCheck_ShouldMarkTheColumnAsAToken()
    {
        // Arrange & Act
        var metadata = EntityMetadataFactory.Build(typeof(Annotated));

        // Assert
        Assert.True(metadata.ByPropertyName["Version"].IsConcurrencyToken);
        Assert.False(metadata.ByPropertyName["Version"].IsRowVersion);
    }

    [Fact]
    public void Build_WhenAPropertyIsNotMapped_ShouldSkipIt()
    {
        // Arrange & Act
        var metadata = EntityMetadataFactory.Build(typeof(Annotated));

        // Assert
        Assert.DoesNotContain(nameof(Annotated.Ignored), metadata.ByPropertyName.Keys);
    }

    [Fact]
    public void Build_WhenAPropertyIsANavigation_ShouldSkipIt()
    {
        // Arrange & Act
        var metadata = EntityMetadataFactory.Build(typeof(WithNavigation));

        // Assert
        Assert.DoesNotContain(nameof(WithNavigation.Related), metadata.ByPropertyName.Keys);
        Assert.DoesNotContain(nameof(WithNavigation.Children), metadata.ByPropertyName.Keys);
        Assert.Contains(nameof(WithNavigation.Id), metadata.ByPropertyName.Keys);
    }

    [Fact]
    public void Build_WhenAPropertyIsWriteOnlyOrIndexed_ShouldSkipIt()
    {
        // Arrange & Act
        var metadata = EntityMetadataFactory.Build(typeof(Awkward));

        // Assert
        Assert.Equal(["Id"], metadata.ByPropertyName.Keys);
    }

    [Fact]
    public void Build_WhenAPropertyIsReadOnly_ShouldMapItWithoutASetter()
    {
        // Arrange & Act
        var metadata = EntityMetadataFactory.Build(typeof(Annotated));

        // Assert
        Assert.Null(metadata.ByPropertyName["Derived"].Setter);
        Assert.NotNull(metadata.ByPropertyName["Version"].Setter);
    }

    [Fact]
    public void Build_WhenTheTypeHasNoMappableProperties_ShouldThrow()
        => Assert.Throws<BulkConfigurationException>(() => EntityMetadataFactory.Build(typeof(Empty)));

    [Fact]
    public void Build_WhenAPropertyTypeIsUnsupported_ShouldSkipItAsANavigation()
    {
        // Arrange & Act
        var metadata = EntityMetadataFactory.Build(typeof(WithNavigation));

        // Assert
        Assert.Single(metadata.Columns);
    }

    [Fact]
    public void FindByPropertyName_WhenTheNameIsUnknown_ShouldReturnNull()
    {
        // Arrange & Act
        var metadata = EntityMetadataFactory.Build(typeof(Book));

        // Assert
        Assert.Null(metadata.FindByPropertyName("NotAProperty"));
    }

    public class Plain
    {
        public int Id { get; set; }

        public string? Name { get; set; }
    }

    public class GuidKeyed
    {
        public Guid Id { get; set; }

        public string? Name { get; set; }
    }

    public class Annotated
    {
        public int Id { get; set; }

        [Column("actual_column", TypeName = "varchar(50)")]
        public string Renamed { get; set; } = string.Empty;

        public int? Optional { get; set; }

        [ConcurrencyCheck]
        public int Version { get; set; }

        [NotMapped]
        public string Ignored { get; set; } = string.Empty;

        public string Derived
            => Renamed + Version;
    }

    public class WithNavigation
    {
        public int Id { get; set; }

        public Plain? Related { get; set; }

        public List<Plain> Children { get; set; } = [];
    }

    public class Awkward
    {
        private string _writeOnly = string.Empty;

        public int Id { get; set; }

        public string WriteOnly
        {
            set => _writeOnly = value;
        }

        public string this[int index]
            => _writeOnly + index;
    }

    public class Empty;
}
