using SqlDiagnostics.Core.Utilities;
using Xunit;

namespace SqlDiagnostics.Core.Tests;

public class ConnectionStringParserTests
{
    [Theory]
    [InlineData("Server=tcp:myserver.database.windows.net,1433;Database=AdventureWorks;", "tcp:myserver.database.windows.net,1433")]
    [InlineData("Data Source=localhost;Initial Catalog=master;", "localhost")]
    [InlineData("Addr=my.sql.server;Uid=sa;Pwd=p@ss;", "my.sql.server")]
    public void TryGetDataSource_ReturnsExpected(string connectionString, string expected)
    {
        var value = ConnectionStringParser.TryGetDataSource(connectionString);
        Assert.Equal(expected, value);
    }

    [Fact]
    public void TryGetDataSource_WithEmpty_ReturnsNull()
    {
        var value = ConnectionStringParser.TryGetDataSource(string.Empty);
        Assert.Null(value);
    }
}
