using System.Data.SqlClient.BulkOperations.Configuration;
using System.Data.SqlClient.BulkOperations.Exceptions;
using System.Data.SqlClient.BulkOperations.Execution;
using System.Data.SqlClient.BulkOperations.Metadata;
using System.Data.SqlClient.BulkOperations.Tests.Shared;
using Xunit;

namespace System.Data.SqlClient.BulkOperations.Tests.Unit.Execution;

public class OperationPlannerTests
{
    [Fact]
    public void Plan_WhenPlainInsert_ShouldNotMatchOnAnything()
    {
        // Arrange & Act
        // Matching on an identity key here would put a primary key on the staging table over
        // values that are still 0 on every unsaved item.
        var plan = Plan<Book>(BulkOperationKind.Insert);

        // Assert
        Assert.Empty(plan.MatchColumns);
        Assert.False(plan.MatchIsUnique);
    }

    [Fact]
    public void Plan_WhenInsertIfMissingWithoutASelector_ShouldFallBackToTheKeyColumns()
    {
        // Arrange & Act
        var plan = Plan<Book>(BulkOperationKind.Insert, config => config.InsertIfMissing = true);

        // Assert
        Assert.Equal(["Id"], plan.MatchColumns.Select(column => column.PropertyName));
    }

    [Fact]
    public void Plan_WhenTheOperationNeedsKeys_ShouldFallBackToTheKeyColumns()
    {
        // Arrange
        BulkOperationKind[] kinds = [BulkOperationKind.Update, BulkOperationKind.Delete, BulkOperationKind.Merge];

        // Act
        foreach (var kind in kinds)
        {
            var plan = Plan<Book>(kind);

            Assert.Equal(["Id"], plan.MatchColumns.Select(column => column.PropertyName));
        }
    }

    [Fact]
    public void Plan_WhenMatchOnIsGiven_ShouldUseTheSelectedColumns()
    {
        // Arrange & Act
        var plan = Plan<Book>(
            BulkOperationKind.Update,
            config => config.MatchSelectors.Add(book => book.Isbn)
        );

        // Assert
        Assert.Equal(["Isbn"], plan.MatchColumns.Select(column => column.PropertyName));
    }

    [Fact]
    public void Plan_WhenMatchingOnANonKeyColumn_ShouldNotTreatTheKeyAsUnique()
    {
        // Arrange & Act
        var plan = Plan<Book>(
            BulkOperationKind.Update,
            config => config.MatchSelectors.Add(book => book.Isbn)
        );

        // Assert
        Assert.False(plan.MatchIsUnique);
    }

    [Fact]
    public void Plan_WhenMerging_ShouldTreatTheMatchAsUniqueBecauseTheSourceIsDeduplicated()
    {
        // Arrange & Act
        var plan = Plan<Book>(
            BulkOperationKind.Merge,
            config => config.MatchSelectors.Add(book => book.Isbn)
        );

        // Assert
        Assert.True(plan.MatchIsUnique);
    }

    [Fact]
    public void Plan_WhenInserting_ShouldExcludeIdentityComputedAndRowVersionColumns()
    {
        // Arrange & Act
        var plan = Plan<Book>(BulkOperationKind.Insert);

        // Assert
        Assert.Equal(
            ["Isbn", "Title", "Price", "Edition"],
            plan.InsertColumns.Select(column => column.PropertyName)
        );
    }

    [Fact]
    public void Plan_WhenKeepIdentityIsSet_ShouldIncludeTheIdentityColumnInTheInsertList()
    {
        // Arrange & Act
        var plan = Plan<Book>(BulkOperationKind.Insert, config => config.KeepIdentity = true);

        // Assert
        Assert.Contains(plan.InsertColumns, column => column.PropertyName == "Id");
        Assert.DoesNotContain(plan.InsertColumns, column => column.PropertyName == "CreatedUtc");
    }

    [Fact]
    public void Plan_WhenAnIdentityColumnIsOverridden_ShouldExcludeItFromInserts()
    {
        // Arrange & Act
        var plan = Plan<Widget>(
            BulkOperationKind.Insert,
            config => config.IdentityColumnOverride = widget => widget.Quantity
        );

        // Assert
        Assert.DoesNotContain(plan.InsertColumns, column => column.PropertyName == "Quantity");
    }

    [Fact]
    public void Plan_WhenUpdating_ShouldExcludeKeyIdentityComputedAndConcurrencyColumns()
    {
        // Arrange & Act
        var plan = Plan<Book>(BulkOperationKind.Update);

        // Assert
        Assert.Equal(
            ["Isbn", "Title", "Price", "Edition"],
            plan.UpdateColumns.Select(column => column.PropertyName)
        );
    }

    [Fact]
    public void Plan_WhenUpdateColumnsAreGiven_ShouldUseThatListMinusAnyKeyColumn()
    {
        // Arrange & Act
        var plan = Plan<Book>(
            BulkOperationKind.Update,
            config => config.UpdateColumnSelectors.Add(book => new { book.Id, book.Title, book.Price })
        );

        // Assert
        Assert.Equal(["Title", "Price"], plan.UpdateColumns.Select(column => column.PropertyName));
    }

    [Fact]
    public void Plan_WhenAColumnWhitelistIsGiven_ShouldRestrictEveryColumnSetToIt()
    {
        // Arrange & Act
        var plan = Plan<Book>(
            BulkOperationKind.Insert,
            config => config.ColumnSelectors.Add(book => new { book.Isbn, book.Title })
        );

        // Assert
        Assert.Equal(["Isbn", "Title"], plan.InsertColumns.Select(column => column.PropertyName));
        Assert.Equal(["Isbn", "Title"], plan.StagingColumns.Select(column => column.PropertyName));
    }

    [Fact]
    public void Plan_WhenAColumnIsListedTwice_ShouldKeepOneEntryPerProperty()
    {
        // Arrange & Act
        var plan = Plan<Book>(
            BulkOperationKind.Insert,
            config =>
            {
                config.ColumnSelectors.Add(book => book.Isbn);
                config.ColumnSelectors.Add(book => new { book.Isbn, book.Title });
            }
        );

        // Assert
        Assert.Equal(["Isbn", "Title"], plan.InsertColumns.Select(column => column.PropertyName));
    }

    [Fact]
    public void Plan_WhenColumnMappingsAreGiven_ShouldRenameTheColumnButKeepThePropertyName()
    {
        // Arrange
        var plan = Plan<WidgetDto>(
            BulkOperationKind.Insert,
            config =>
            {
                config.ColumnMappings["Label"] = "Name";
                config.ColumnMappings["Count"] = "Quantity";
            }
        );

        // Act
        var label = plan.InsertColumns.Single(column => column.PropertyName == "Label");

        // Assert
        Assert.Equal("Name", label.ColumnName);
    }

    [Fact]
    public void Plan_WhenColumnMappingsCoverTheKey_ShouldRenameTheMatchColumnsToo()
    {
        // Arrange & Act
        var plan = Plan<WidgetDto>(
            BulkOperationKind.Update,
            config => config.ColumnMappings["Id"] = "WidgetId"
        );

        // Assert
        Assert.Equal("WidgetId", plan.MatchColumns.Single().ColumnName);
    }

    [Fact]
    public void Plan_WhenAMappingNamesAnUnknownProperty_ShouldThrow()
    {
        // Act & Assert
        var exception = Assert.Throws<BulkConfigurationException>(() => Plan<Book>(
            BulkOperationKind.Insert,
            config => config.ColumnMappings["NotAProperty"] = "Whatever"
        ));

        Assert.Contains("NotAProperty", exception.Message);
    }

    [Fact]
    public void Plan_WhenInserting_ShouldNeverCheckConcurrency()
    {
        // Arrange & Act
        var plan = Plan<Book>(BulkOperationKind.Insert, config => config.ConcurrencyCheckOverride = true);

        // Assert
        Assert.Empty(plan.ConcurrencyColumns);
    }

    [Fact]
    public void Plan_WhenTheTokenIsNotInTheColumnWhitelist_ShouldNotCheckConcurrency()
    {
        // Arrange & Act
        var plan = Plan<Book>(
            BulkOperationKind.Update,
            config =>
            {
                config.ColumnSelectors.Add(book => new { book.Isbn, book.Title });
                config.MatchSelectors.Add(book => book.Isbn);
            }
        );

        // Assert
        Assert.Empty(plan.ConcurrencyColumns);
    }

    [Fact]
    public void Plan_WhenTheEntityHasARowVersion_ShouldCheckConcurrencyByDefault()
    {
        // Arrange & Act
        var plan = Plan<Book>(BulkOperationKind.Update);

        // Assert
        Assert.Equal(["RowVersion"], plan.ConcurrencyColumns.Select(column => column.PropertyName));
    }

    [Fact]
    public void Plan_WhenConcurrencyCheckingIsTurnedOff_ShouldDropTheConcurrencyColumns()
    {
        // Arrange & Act
        var plan = Plan<Book>(
            BulkOperationKind.Update,
            config => config.ConcurrencyCheckOverride = false
        );

        // Assert
        Assert.Empty(plan.ConcurrencyColumns);
    }

    [Fact]
    public void Plan_WhenTheEntityHasNoToken_ShouldCheckNothingEvenIfAsked()
    {
        // Arrange & Act
        var plan = Plan<Widget>(
            BulkOperationKind.Update,
            config => config.ConcurrencyCheckOverride = true
        );

        // Assert
        Assert.Empty(plan.ConcurrencyColumns);
    }

    [Fact]
    public void Plan_WhenOutputIdentityIsSet_ShouldOutputEveryStoreGeneratedColumn()
    {
        // Arrange & Act
        var plan = Plan<Book>(BulkOperationKind.Insert, config => config.OutputIdentity = true);

        // Assert
        Assert.Equal(
            ["Id", "CreatedUtc", "RowVersion"],
            plan.OutputColumns.Select(column => column.PropertyName)
        );
    }

    [Fact]
    public void Plan_WhenAnOutputSelectorOverlapsOutputIdentity_ShouldNotRepeatTheColumn()
    {
        // Arrange & Act
        var plan = Plan<Book>(
            BulkOperationKind.Insert,
            config =>
            {
                config.OutputIdentity = true;
                config.OutputSelectors.Add(book => book.Id);
            }
        );

        // Assert
        Assert.Single(plan.OutputColumns, column => column.PropertyName == "Id");
    }

    [Fact]
    public void Plan_WhenDeleting_ShouldStageOnlyMatchAndConcurrencyColumns()
    {
        // Arrange & Act
        var plan = Plan<Book>(
            BulkOperationKind.Delete,
            config => config.MatchSelectors.Add(book => book.Isbn)
        );

        // Assert
        Assert.Equal(["Isbn", "RowVersion"], plan.StagingColumns.Select(column => column.PropertyName));
    }

    [Fact]
    public void Plan_WhenUpdating_ShouldStageMatchUpdateAndConcurrencyColumns()
    {
        // Arrange & Act
        var plan = Plan<Book>(
            BulkOperationKind.Update,
            config =>
            {
                config.MatchSelectors.Add(book => book.Isbn);
                config.UpdateColumnSelectors.Add(book => book.Title);
            }
        );

        // Assert
        Assert.Equal(["Isbn", "Title", "RowVersion"], plan.StagingColumns.Select(column => column.PropertyName));
    }

    [Fact]
    public void Plan_WhenMerging_ShouldStageMatchInsertAndUpdateColumnsWithoutDuplicates()
    {
        // Arrange & Act
        var plan = Plan<Book>(
            BulkOperationKind.Merge,
            config =>
            {
                config.MatchSelectors.Add(book => book.Isbn);
                config.UpdateColumnSelectors.Add(book => book.Title);
                config.ConcurrencyCheckOverride = false;
            }
        );

        // Assert
        Assert.Equal(
            ["Isbn", "Title", "Price", "Edition"],
            plan.StagingColumns.Select(column => column.PropertyName)
        );
    }

    [Fact]
    public void Plan_WhenTableAndSchemaAreOverridden_ShouldQuoteTheGivenNames()
    {
        // Arrange & Act
        var plan = Plan<Book>(
            BulkOperationKind.Insert,
            config =>
            {
                config.SchemaOverride = "archive";
                config.TableOverride = "Old Books";
            }
        );

        // Assert
        Assert.Equal("[archive].[Old Books]", plan.TargetTable);
    }

    [Fact]
    public void Plan_WhenNothingIsOverridden_ShouldUseTheTableAttribute()
    {
        // Arrange & Act
        var plan = Plan<ShopItem>(BulkOperationKind.Insert);

        // Assert
        Assert.Equal("[shop].[Items]", plan.TargetTable);
    }

    [Fact]
    public void Plan_WhenOperationFlagsAreSet_ShouldCarryThemThrough()
    {
        // Arrange & Act
        var plan = Plan<Book>(
            BulkOperationKind.Merge,
            config =>
            {
                config.DeleteWhenNotMatched = true;
                config.KeepIdentity = true;
            }
        );

        // Assert
        Assert.True(plan.DeleteWhenNotMatched);
        Assert.True(plan.KeepIdentity);
        Assert.Equal(BulkOperationKind.Merge, plan.Kind);
    }

    private static OperationPlan Plan<TEntity>(
        BulkOperationKind kind,
        Action<BulkConfiguration<TEntity>>? configure = null
    ) where TEntity : class
    {
        var config = new BulkConfiguration<TEntity>(kind, []);
        configure?.Invoke(config);

        return OperationPlanner.Plan(config, MetadataCache.For<TEntity>());
    }
}
