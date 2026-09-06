using System.Data.SqlClient.BulkOperations.Execution;
using Xunit;

namespace System.Data.SqlClient.BulkOperations.Tests.Unit.Execution;

public class RetryPolicyTests
{
    [Fact]
    public void IsTransient_WhenTheExceptionIsATimeout_ShouldBeTrue()
        => Assert.True(RetryPolicy.IsTransient(new TimeoutException()));

    [Theory]
    [InlineData(typeof(InvalidOperationException))]
    [InlineData(typeof(ArgumentException))]
    [InlineData(typeof(OperationCanceledException))]
    public void IsTransient_WhenTheExceptionIsUnrelated_ShouldBeFalse(Type exceptionType)
    {
        // Arrange & Act
        var exception = (Exception)Activator.CreateInstance(exceptionType)!;

        // Assert
        Assert.False(RetryPolicy.IsTransient(exception));
    }

    [Fact]
    public void IsTransient_WhenATimeoutIsWrappedByTheClient_ShouldBeTrue()
        => Assert.True(RetryPolicy.IsTransient(new InvalidOperationException("wrapped", new TimeoutException())));

    [Fact]
    public void FindSqlException_WhenThereIsNone_ShouldReturnNull()
    {
        // Act & Assert
        Assert.Null(RetryPolicy.FindSqlException(null));
        Assert.Null(RetryPolicy.FindSqlException(new InvalidOperationException("no sql error here")));
    }

    [Fact]
    public void Backoff_WhenTheAttemptNumberRises_ShouldGrow()
    {
        // Arrange
        var baseDelay = TimeSpan.FromMilliseconds(100);

        // Act
        var first = RetryPolicy.Backoff(baseDelay, attempt: 0);
        var fourth = RetryPolicy.Backoff(baseDelay, attempt: 3);

        // Assert
        Assert.True(fourth > first);
    }

    [Fact]
    public void Backoff_WhenCalled_ShouldStayWithinTheExponentialWindowPlusJitter()
    {
        // Arrange
        var baseDelay = TimeSpan.FromMilliseconds(100);

        // Act
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var delay = RetryPolicy.Backoff(baseDelay, attempt);
            var floor = baseDelay.TotalMilliseconds * Math.Pow(2, attempt);

            Assert.InRange(delay.TotalMilliseconds, floor, floor + baseDelay.TotalMilliseconds);
        }
    }

    [Fact]
    public void Backoff_WhenCalledRepeatedly_ShouldJitterSoRetriesDoNotLineUp()
    {
        // Arrange
        var baseDelay = TimeSpan.FromMilliseconds(100);

        // Act
        var delays = Enumerable.Range(0, 50)
            .Select(_ => RetryPolicy.Backoff(baseDelay, attempt: 1))
            .ToArray();

        // Assert
        Assert.True(delays.Distinct().Count() > 1);
    }
}
