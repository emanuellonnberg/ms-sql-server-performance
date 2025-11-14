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

    [Theory]
    [InlineData("localhost\\SQLEXPRESS", "localhost", null)]
    [InlineData("tcp:demo.database.windows.net,1433", "demo.database.windows.net", 1433)]
    [InlineData("192.168.1.50,1500", "192.168.1.50", 1500)]
    [InlineData(".\\INSTANCE", "localhost", null)]
    [InlineData("(local)", "localhost", null)]
    [InlineData("[fe80::1%4],1501", "fe80::1%4", 1501)]
    public void TryGetNetworkEndpoint_NormalisesHostAndPort(string dataSource, string expectedHost, int? expectedPort)
    {
        var success = ConnectionStringParser.TryGetNetworkEndpoint(dataSource, out var host, out var port);

        Assert.True(success);
        Assert.Equal(expectedHost, host);
        Assert.Equal(expectedPort, port);
    }

    [Fact]
    public void TryGetNetworkEndpoint_WithEmpty_ReturnsFalse()
    {
        var success = ConnectionStringParser.TryGetNetworkEndpoint(null, out var host, out var port);

        Assert.False(success);
        Assert.Null(host);
        Assert.Null(port);
    }
}
