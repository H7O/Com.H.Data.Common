using Com.H.Data.Common;
using Microsoft.Data.SqlClient;
using System.Data.Odbc;
using System.Text.Json;

namespace Tests;

/// <summary>
/// The SQL Server tests run against LocalDB. Each test returns without checking anything when LocalDB
/// isn't installed (Linux, for one).
/// </summary>
internal static class LocalDb
{
    public static SqlConnection? TryOpen()
    {
        if (!OperatingSystem.IsWindows()) return null;
        var connection = new SqlConnection(@"Server=(localdb)\MSSQLLocalDB;Integrated Security=true;Connect Timeout=60");
        try
        {
            connection.Open();
            return connection;
        }
        catch (SqlException)
        {
            connection.Dispose();
            return null;
        }
    }
}

/// <summary>
/// Placeholder names on SQL Server, which compares parameter names ignoring case. It rejects some
/// characters in them that SQLite accepts (€), and accepts some that SQLite rejects (@), so the
/// "@type" and "prénom" rows pass on either version of the naming; the SQLite tests check the
/// generated names themselves. These check that SQL Server runs the query with the right values.
/// </summary>
[Collection(TestCollections.AdoNetExtStaticSettings)]
public class SqlServerParameterNameTests : IDisposable
{
    private readonly SqlConnection? _connection = LocalDb.TryOpen();

    private static readonly JsonElement Body = JsonDocument.Parse("""
        {
          "first name": "Ann", "first-name": "Bob",
          "Email": "a@b.c", "@type": "admin", "price (€)": "5 EUR", "prénom": "Zoé"
        }
        """).RootElement;

    public void Dispose()
    {
        _connection?.Dispose();
        AdoNetExt.DefaultParameterTemplate = "{{DefaultParameterPrefix}}vxv_{{ParameterCount}}_{{ParameterName}}";
    }

    // The number of parameters the last SingleRowAsync's command had.
    private int _parameterCount;

    private async Task<dynamic> SingleRowAsync(string query, object queryParams,
        string queryParamsRegex = @"(?<open_marker>\{\{)(?<param>.*?)?(?<close_marker>\}\})")
    {
        using var command = _connection!.CreateCommand();
        await using var result = await command.ExecuteQueryAsync(query, queryParams, queryParamsRegex);
        var rows = new List<dynamic>();
        await foreach (var row in result) rows.Add(row);
        _parameterCount = command.Parameters.Count;
        return Assert.Single(rows);
    }

    [Fact]
    public async Task NamesThatDifferOnlyInCase_AreOneParameter()
    {
        // Each spelling used to get its own parameter, and SQL Server refused the batch:
        // "The variable name '@vxv_1_email' has already been declared."
        if (_connection is null) return;

        var row = await SingleRowAsync("SELECT {{Email}} AS a, {{email}} AS b", Body);

        Assert.Equal("a@b.c", (string)row.a);
        Assert.Equal("a@b.c", (string)row.b);
        Assert.Equal(1, _parameterCount);
    }

    [Fact]
    public async Task NamesThatCleanToTheSameName_EachGetsItsOwnValue()
    {
        if (_connection is null) return;

        var row = await SingleRowAsync("SELECT {{first name}} AS a, {{first-name}} AS b", Body);

        Assert.Equal("Ann", (string)row.a);
        Assert.Equal("Bob", (string)row.b);
    }

    [Fact]
    public async Task OneNameUnderTwoMarkersOfOnePattern_IsOneParameter()
    {
        if (_connection is null) return;

        var row = await SingleRowAsync("SELECT {{Email}} AS a, {j{Email}} AS b", Body,
            @"(?<open_marker>\{\{|\{j\{)(?<param>.*?)?(?<close_marker>\}\})");

        Assert.Equal("a@b.c", (string)row.a);
        Assert.Equal("a@b.c", (string)row.b);
        Assert.Equal(1, _parameterCount);
    }

    [Theory]
    [InlineData("@type", "admin")]
    [InlineData("price (€)", "5 EUR")]
    [InlineData("prénom", "Zoé")]
    public async Task NameWithAnyCharacters_GetsAValidParameterName(string name, string expected)
    {
        if (_connection is null) return;

        var row = await SingleRowAsync($"SELECT {{{{{name}}}}} AS v", Body);

        Assert.Equal(expected, (string)row.v);
    }

    [Fact]
    public async Task TemplateWithoutCount_NamesThatDifferOnlyInCaseStayUnique()
    {
        // @X and @x are the same name to SQL Server.
        if (_connection is null) return;
        AdoNetExt.DefaultParameterTemplate = "{{DefaultParameterPrefix}}{{ParameterName}}";
        var queryParamsList = new List<DbQueryParams>
        {
            new() { DataModel = new { X = "curly" } },
            new() { DataModel = new { x = "square" }, QueryParamsRegex = @"(?<open_marker>\[\[)(?<param>.*?)?(?<close_marker>\]\])" }
        };

        var row = await SingleRowAsync("SELECT {{X}} AS a, [[x]] AS b", queryParamsList);

        Assert.Equal("curly", (string)row.a);
        Assert.Equal("square", (string)row.b);
    }
}

/// <summary>
/// SQL Server, where closing a reader before the end of the rows discards the errors of the rest of
/// the batch.
/// </summary>
public class SqlServerErrorAfterRowsTests : IDisposable
{
    private readonly SqlConnection? _connection = LocalDb.TryOpen();

    // Two rows, so a caller that reads one stops before the end, then an error.
    private const string ThrowAfterRows = "SELECT 1 AS n UNION ALL SELECT 2; THROW 50400, 'Raised after the rows', 1;";

    public void Dispose() => _connection?.Dispose();

    [Fact]
    public async Task ReadingToTheEnd_ThrowsTheError()
    {
        if (_connection is null) return;

        await using var result = await _connection.ExecuteQueryAsync(ThrowAfterRows);

        var ex = await Assert.ThrowsAsync<SqlException>(async () => { await foreach (var _ in result) { } });
        Assert.Equal(50400, ex.Number);
    }

    [Fact]
    public async Task StoppingAfterTheFirstRow_DiscardsTheError()
    {
        // As the README says: the caller sees a success.
        if (_connection is null) return;

        await using (var result = await _connection.ExecuteQueryAsync(ThrowAfterRows))
        {
            await foreach (var _ in result) break;
        }
    }
}

/// <summary>
/// SQL Server through System.Data.Odbc (ODBC Driver 17 for SQL Server, LocalDB), which reads the rest of
/// the batch when a reader closes and throws from Close. Each test returns without checking anything
/// when the driver or LocalDB isn't installed.
/// </summary>
public class OdbcSqlServerErrorAfterRowsTests : IDisposable
{
    private readonly OdbcConnection? _connection;

    public OdbcSqlServerErrorAfterRowsTests()
    {
        if (!OperatingSystem.IsWindows()) return;
        var connection = new OdbcConnection(@"Driver={ODBC Driver 17 for SQL Server};Server=(localdb)\MSSQLLocalDB;Trusted_Connection=yes;");
        try
        {
            connection.Open();
            _connection = connection;
        }
        catch (Exception ex) when (ex is OdbcException or InvalidOperationException)
        {
            connection.Dispose();
        }
    }

    public void Dispose() => _connection?.Dispose();

    [Theory]
    [InlineData("SELECT 1 AS n UNION ALL SELECT 2; RAISERROR('first', 16, 1); SELECT 5 AS m; RAISERROR('second', 16, 1);")]
    [InlineData("SELECT 1 UNION ALL SELECT 2; RAISERROR('first', 16, 1); SELECT 5 AS m; RAISERROR('second', 16, 1);")]
    public void ReadingToTheEnd_TwoErrors_ThrowsTheFirst_AndClosesTheReader(string query)
    {
        // Closing the reader reads on to the second error: it must not replace the first, and the reader,
        // which stays open after a Close that threw, must end up closed. The second query's single
        // unnamed column is returned as bare values, which take their own path.
        if (_connection is null) return;

        using var result = _connection.ExecuteQuery(query);

        var ex = Assert.Throws<OdbcException>(() => result.ToList());
        Assert.Contains("first", ex.Message);
        Assert.DoesNotContain("second", ex.Message);
        AssertClosedAndFree(result);
    }

    [Fact]
    public void StoppingEarly_ThrowsFromClosing_AndClosesTheReader()
    {
        // System.Data.Odbc stays open after a Close that threw; the library closes it again.
        if (_connection is null) return;

        using var result = _connection.ExecuteQuery("SELECT 1 AS n UNION ALL SELECT 2; THROW 50400, 'Raised after the rows', 1;");

        Assert.Throws<OdbcException>(() => result.First());
        AssertClosedAndFree(result);
    }

    [Fact]
    public void StoppingEarly_OnAnUnnamedColumn_ThrowsFromClosing()
    {
        // A single unnamed column is returned as bare values; stopping at one still closes the reader
        // the way stopping at a row does, so the error comes through.
        if (_connection is null) return;

        using var result = _connection.ExecuteQuery("SELECT 1 UNION ALL SELECT 2; THROW 50400, 'Raised after the rows', 1;");

        Assert.Throws<OdbcException>(() => result.First());
        AssertClosedAndFree(result);
    }

    /// <summary>The reader is closed, and the connection runs another query.</summary>
    private void AssertClosedAndFree(DbQueryResult<dynamic> result)
    {
        Assert.True(result.Reader!.IsClosed);
        using var next = _connection!.ExecuteQuery("SELECT 3 AS n");
        Assert.Equal(3, (int)next.First().n);
    }
}
