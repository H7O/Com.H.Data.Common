using Com.H.Data.Common;
using Microsoft.Data.Sqlite;
using System.Data.Common;
using System.Text.Json;

namespace Tests;

/// <summary>
/// SQLite-based integration tests for the AdoNetExt extension methods.
/// Covers ExecuteQuery, ExecuteQueryAsync, ExecuteCommand, ExecuteCommandAsync,
/// parameterized queries, typed queries, long parameter names, null handling,
/// custom delimiters, multiple parameters, and edge cases.
/// </summary>
public class AdoNetExtTests : IDisposable
{
    private readonly DbConnection _connection;

    public AdoNetExtTests()
    {
        // In-memory SQLite database, shared across the test lifetime
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        // Create test tables
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = @"
            CREATE TABLE Users (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                name TEXT NOT NULL,
                email TEXT,
                age INTEGER
            );
            INSERT INTO Users (name, email, age) VALUES ('John', 'john@test.com', 30);
            INSERT INTO Users (name, email, age) VALUES ('Jane', 'jane@test.com', 25);
            INSERT INTO Users (name, email, age) VALUES ('Bob', 'bob@test.com', 40);

            CREATE TABLE Orders (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                userId INTEGER,
                product TEXT,
                amount REAL
            );
            INSERT INTO Orders (userId, product, amount) VALUES (1, 'Widget', 9.99);
            INSERT INTO Orders (userId, product, amount) VALUES (1, 'Gadget', 24.50);
            INSERT INTO Orders (userId, product, amount) VALUES (2, 'Widget', 9.99);
        ";
        cmd.ExecuteNonQuery();
    }

    public void Dispose()
    {
        _connection.Dispose();
        // Reset static state that tests may have changed
        AdoNetExt.MaxParameterNameLength = 30;
        AdoNetExt.ClearPrefixCache();
    }

    #region Basic Query Tests

    [Fact]
    public void ExecuteQuery_NoParams_ReturnsAllRows()
    {
        using var result = _connection.ExecuteQuery("SELECT id, name, email, age FROM Users ORDER BY id");
        var rows = result.ToList();

        Assert.Equal(3, rows.Count);
        Assert.Equal("John", (string)rows[0].name);
        Assert.Equal("Jane", (string)rows[1].name);
        Assert.Equal("Bob", (string)rows[2].name);
    }

    [Fact]
    public async Task ExecuteQueryAsync_NoParams_ReturnsAllRows()
    {
        await using var result = await _connection.ExecuteQueryAsync("SELECT id, name, email, age FROM Users ORDER BY id");
        var rows = new List<dynamic>();
        await foreach (var row in result)
        {
            rows.Add(row);
        }

        Assert.Equal(3, rows.Count);
        Assert.Equal("John", (string)rows[0].name);
        Assert.Equal("Jane", (string)rows[1].name);
        Assert.Equal("Bob", (string)rows[2].name);
    }

    [Fact]
    public void ExecuteQuery_SimpleSelect_ReturnsCorrectColumns()
    {
        using var result = _connection.ExecuteQuery("SELECT name, age FROM Users WHERE id = 1");
        var row = result.First();

        Assert.Equal("John", (string)row.name);
        Assert.Equal(30L, (long)row.age);
    }

    #endregion

    #region Parameterized Query Tests

    [Fact]
    public void ExecuteQuery_WithAnonymousObjectParam_FiltersCorrectly()
    {
        using var result = _connection.ExecuteQuery(
            "SELECT name, email FROM Users WHERE name = {{name}}",
            new { name = "Jane" });

        var rows = result.ToList();
        Assert.Single(rows);
        Assert.Equal("Jane", (string)rows[0].name);
        Assert.Equal("jane@test.com", (string)rows[0].email);
    }

    [Fact]
    public async Task ExecuteQueryAsync_WithAnonymousObjectParam_FiltersCorrectly()
    {
        await using var result = await _connection.ExecuteQueryAsync(
            "SELECT name, email FROM Users WHERE name = {{name}}",
            new { name = "Bob" });

        var rows = new List<dynamic>();
        await foreach (var row in result) rows.Add(row);

        Assert.Single(rows);
        Assert.Equal("Bob", (string)rows[0].name);
    }

    [Fact]
    public void ExecuteQuery_WithDictionaryParam_FiltersCorrectly()
    {
        var queryParams = new Dictionary<string, object> { { "name", "John" } };
        using var result = _connection.ExecuteQuery(
            "SELECT name, age FROM Users WHERE name = {{name}}",
            queryParams);

        var row = result.First();
        Assert.Equal("John", (string)row.name);
    }

    [Fact]
    public void ExecuteQuery_WithMultipleParams_FiltersCorrectly()
    {
        using var result = _connection.ExecuteQuery(
            "SELECT name FROM Users WHERE age >= {{minAge}} AND age <= {{maxAge}} ORDER BY name",
            new { minAge = 25, maxAge = 35 });

        var rows = result.ToList();
        Assert.Equal(2, rows.Count);
        Assert.Equal("Jane", (string)rows[0].name);
        Assert.Equal("John", (string)rows[1].name);
    }

    [Fact]
    public void ExecuteQuery_WithJsonStringParam_FiltersCorrectly()
    {
        var jsonParams = "{\"name\":\"Jane\"}";
        using var result = _connection.ExecuteQuery(
            "SELECT name, email FROM Users WHERE name = {{name}}",
            jsonParams);

        var rows = result.ToList();
        Assert.Single(rows);
        Assert.Equal("Jane", (string)rows[0].name);
    }

    [Fact]
    public void ExecuteQuery_WithJsonElementParam_FiltersCorrectly()
    {
        var jsonElement = JsonDocument.Parse("{\"name\":\"Bob\"}").RootElement;
        using var result = _connection.ExecuteQuery(
            "SELECT name, age FROM Users WHERE name = {{name}}",
            jsonElement);

        var row = result.First();
        Assert.Equal("Bob", (string)row.name);
    }

    [Fact]
    public void ExecuteQuery_ParamUsedMultipleTimes_WorksCorrectly()
    {
        // Use the same parameter in two places
        using var result = _connection.ExecuteQuery(
            "SELECT name FROM Users WHERE name = {{name}} OR email LIKE {{name}} || '%'",
            new { name = "John" });

        var rows = result.ToList();
        Assert.True(rows.Count >= 1);
        Assert.Equal("John", (string)rows[0].name);
    }

    #endregion

    #region ExecuteCommand Tests

    [Fact]
    public void ExecuteCommand_Insert_AddsRow()
    {
        _connection.ExecuteCommand(
            "INSERT INTO Users (name, email, age) VALUES ({{name}}, {{email}}, {{age}})",
            new { name = "Alice", email = "alice@test.com", age = 28 });

        using var result = _connection.ExecuteQuery(
            "SELECT name FROM Users WHERE name = {{name}}",
            new { name = "Alice" });
        var rows = result.ToList();
        Assert.Single(rows);
        Assert.Equal("Alice", (string)rows[0].name);
    }

    [Fact]
    public async Task ExecuteCommandAsync_Update_ModifiesRow()
    {
        await _connection.ExecuteCommandAsync(
            "UPDATE Users SET email = {{email}} WHERE name = {{name}}",
            new { name = "John", email = "john.updated@test.com" });

        await using var result = await _connection.ExecuteQueryAsync(
            "SELECT email FROM Users WHERE name = {{name}}",
            new { name = "John" });

        var rows = new List<dynamic>();
        await foreach (var row in result) rows.Add(row);
        Assert.Single(rows);
        Assert.Equal("john.updated@test.com", (string)rows[0].email);
    }

    [Fact]
    public void ExecuteCommand_Delete_RemovesRow()
    {
        _connection.ExecuteCommand(
            "DELETE FROM Users WHERE name = {{name}}",
            new { name = "Bob" });

        using var result = _connection.ExecuteQuery("SELECT COUNT(*) as cnt FROM Users");
        var row = result.First();
        Assert.Equal(2L, (long)row.cnt);
    }

    #endregion

    #region Typed Query Tests

    public class UserModel
    {
        public long Id { get; set; }
        public string Name { get; set; } = "";
        public string Email { get; set; } = "";
        public long Age { get; set; }
    }

    [Fact]
    public void ExecuteQuery_Typed_MapsToModel()
    {
        using var result = _connection.ExecuteQuery<UserModel>(
            "SELECT id as Id, name as Name, email as Email, age as Age FROM Users ORDER BY id");

        var users = result.ToList();
        Assert.Equal(3, users.Count);
        Assert.Equal("John", users[0].Name);
        Assert.Equal("john@test.com", users[0].Email);
        Assert.Equal(30L, users[0].Age);
    }

    [Fact]
    public async Task ExecuteQueryAsync_Typed_MapsToModel()
    {
        await using var result = await _connection.ExecuteQueryAsync<UserModel>(
            "SELECT id as Id, name as Name, email as Email, age as Age FROM Users WHERE name = {{name}}",
            new { name = "Jane" });

        var users = new List<UserModel>();
        await foreach (var user in result) users.Add(user);

        Assert.Single(users);
        Assert.Equal("Jane", users[0].Name);
        Assert.Equal(25L, users[0].Age);
    }

    [Fact]
    public void ExecuteQuery_TypedWithParams_FiltersAndMaps()
    {
        using var result = _connection.ExecuteQuery<UserModel>(
            "SELECT id as Id, name as Name, email as Email, age as Age FROM Users WHERE age > {{minAge}} ORDER BY name",
            new { minAge = 28 });

        var users = result.ToList();
        Assert.Equal(2, users.Count);
        Assert.Equal("Bob", users[0].Name);
        Assert.Equal("John", users[1].Name);
    }

    #endregion

    #region Long Parameter Name Tests

    [Fact]
    public void ExecuteQuery_LongParamName_DoesNotCrash()
    {
        // Create a parameter name longer than MaxParameterNameLength (default 30)
        var longParamName = new string('a', 50);
        var queryParams = new Dictionary<string, object> { { longParamName, "John" } };

        // Build query with the long parameter name
        var query = $"SELECT name, email FROM Users WHERE name = {{{{{longParamName}}}}}";

        using var result = _connection.ExecuteQuery(query, queryParams);
        var rows = result.ToList();
        Assert.Single(rows);
        Assert.Equal("John", (string)rows[0].name);
    }

    [Fact]
    public async Task ExecuteQueryAsync_LongParamName_DoesNotCrash()
    {
        var longParamName = new string('x', 100);
        var queryParams = new Dictionary<string, object> { { longParamName, "Jane" } };

        var query = $"SELECT name FROM Users WHERE name = {{{{{longParamName}}}}}";

        await using var result = await _connection.ExecuteQueryAsync(query, queryParams);
        var rows = new List<dynamic>();
        await foreach (var row in result) rows.Add(row);

        Assert.Single(rows);
        Assert.Equal("Jane", (string)rows[0].name);
    }

    [Fact]
    public void ExecuteQuery_VeryLongParamName_128Chars_DoesNotCrash()
    {
        // 128 characters — the old limit that would fail on SQL Server
        var longParamName = new string('p', 128);
        var queryParams = new Dictionary<string, object> { { longParamName, "Bob" } };
        var query = $"SELECT name, age FROM Users WHERE name = {{{{{longParamName}}}}}";

        using var result = _connection.ExecuteQuery(query, queryParams);
        var row = result.First();
        Assert.Equal("Bob", (string)row.name);
    }

    [Fact]
    public void ExecuteQuery_MultipleLongParamNames_AllResolveCorrectly()
    {
        // Two different long parameter names in one query
        var longName1 = "param_name_that_is_definitely_over_thirty_chars_long_name";
        var longName2 = "another_really_long_parameter_name_that_exceeds_the_limit";
        var queryParams = new Dictionary<string, object>
        {
            { longName1, 25 },
            { longName2, 40 }
        };

        var query = $"SELECT name FROM Users WHERE age >= {{{{{longName1}}}}} AND age <= {{{{{longName2}}}}} ORDER BY name";

        using var result = _connection.ExecuteQuery(query, queryParams);
        var rows = result.ToList();
        Assert.Equal(3, rows.Count);
    }

    [Fact]
    public void ExecuteQuery_ParamNameExactlyAtLimit_UsesOriginalName()
    {
        // Name exactly at MaxParameterNameLength should NOT be replaced
        var savedMax = AdoNetExt.MaxParameterNameLength;
        try
        {
            AdoNetExt.MaxParameterNameLength = 10;
            var paramName = new string('n', 10); // exactly at limit
            var queryParams = new Dictionary<string, object> { { paramName, "John" } };
            var query = $"SELECT name FROM Users WHERE name = {{{{{paramName}}}}}";

            using var result = _connection.ExecuteQuery(query, queryParams);
            var rows = result.ToList();
            Assert.Single(rows);
            Assert.Equal("John", (string)rows[0].name);
        }
        finally
        {
            AdoNetExt.MaxParameterNameLength = savedMax;
        }
    }

    [Fact]
    public void ExecuteQuery_ParamNameOneOverLimit_UsesShortName()
    {
        // Name one char over MaxParameterNameLength should be replaced
        var savedMax = AdoNetExt.MaxParameterNameLength;
        try
        {
            AdoNetExt.MaxParameterNameLength = 10;
            var paramName = new string('n', 11); // one over limit
            var queryParams = new Dictionary<string, object> { { paramName, "Jane" } };
            var query = $"SELECT name FROM Users WHERE name = {{{{{paramName}}}}}";

            using var result = _connection.ExecuteQuery(query, queryParams);
            var rows = result.ToList();
            Assert.Single(rows);
            Assert.Equal("Jane", (string)rows[0].name);
        }
        finally
        {
            AdoNetExt.MaxParameterNameLength = savedMax;
        }
    }

    [Fact]
    public void ExecuteCommand_LongParamName_InsertsCorrectly()
    {
        var longParamName = new string('z', 60);
        var queryParams = new Dictionary<string, object>
        {
            { longParamName, "LongNameUser" }
        };

        _connection.ExecuteCommand(
            $"INSERT INTO Users (name, email, age) VALUES ({{{{{longParamName}}}}}, 'long@test.com', 99)",
            queryParams);

        using var result = _connection.ExecuteQuery(
            "SELECT name FROM Users WHERE name = {{name}}",
            new { name = "LongNameUser" });
        var rows = result.ToList();
        Assert.Single(rows);
    }

    #endregion

    #region Null Parameter Handling Tests

    [Fact]
    public void ExecuteQuery_NullParamValue_HandledAsDbNull()
    {
        using var result = _connection.ExecuteQuery(
            "SELECT name FROM Users WHERE email = {{email}}",
            new { email = (string?)null });

        var rows = result.ToList();
        // NULL = NULL is false in SQL, so no rows should match
        Assert.Empty(rows);
    }

    [Fact]
    public void ExecuteQuery_NullParamWithIsNull_ReturnsResults()
    {
        // Insert a row with null email
        _connection.ExecuteCommand(
            "INSERT INTO Users (name, email, age) VALUES ('NullEmail', NULL, 50)");

        using var result = _connection.ExecuteQuery(
            "SELECT name FROM Users WHERE email IS NULL");

        var rows = result.ToList();
        Assert.Single(rows);
        Assert.Equal("NullEmail", (string)rows[0].name);
    }

    #endregion

    #region Custom Delimiter Tests

    [Fact]
    public void ExecuteQuery_SquareBracketDelimiters_WorksCorrectly()
    {
        using var result = _connection.ExecuteQuery(
            "SELECT name FROM Users WHERE name = [[name]]",
            new { name = "John" },
            @"(?<open_marker>\[\[)(?<param>.*?)?(?<close_marker>\]\])");

        var rows = result.ToList();
        Assert.Single(rows);
        Assert.Equal("John", (string)rows[0].name);
    }

    [Fact]
    public void ExecuteQuery_PipeDelimiters_WorksCorrectly()
    {
        using var result = _connection.ExecuteQuery(
            "SELECT name FROM Users WHERE name = |name|",
            new { name = "Jane" },
            @"(?<open_marker>\|)(?<param>.*?)?(?<close_marker>\|)");

        var rows = result.ToList();
        Assert.Single(rows);
        Assert.Equal("Jane", (string)rows[0].name);
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void ExecuteQuery_NoMatchingRows_ReturnsEmpty()
    {
        using var result = _connection.ExecuteQuery(
            "SELECT name FROM Users WHERE name = {{name}}",
            new { name = "NonExistent" });

        var rows = result.ToList();
        Assert.Empty(rows);
    }

    [Fact]
    public async Task ExecuteQueryAsync_NoMatchingRows_ReturnsEmpty()
    {
        await using var result = await _connection.ExecuteQueryAsync(
            "SELECT name FROM Users WHERE name = {{name}}",
            new { name = "NonExistent" });

        var rows = new List<dynamic>();
        await foreach (var row in result) rows.Add(row);
        Assert.Empty(rows);
    }

    [Fact]
    public void ExecuteQuery_SingleValueResult_ReturnsValue()
    {
        using var result = _connection.ExecuteQuery("SELECT COUNT(*) FROM Users");
        var row = result.First();
        // SQLite returns unnamed single-value column
        Assert.NotNull(row);
    }

    [Fact]
    public void ExecuteQuery_NoParams_SelectLiteral()
    {
        using var result = _connection.ExecuteQuery("SELECT 'hello' as greeting, 42 as number");
        var row = result.First();
        Assert.Equal("hello", (string)row.greeting);
        Assert.Equal(42L, (long)row.number);
    }

    [Fact]
    public void ExecuteQuery_Dispose_AllowsNewQuery()
    {
        // Verifies that disposing a result frees the reader for the next query
        using var result1 = _connection.ExecuteQuery("SELECT * FROM Users");
        var first = result1.First();
        result1.Dispose();

        using var result2 = _connection.ExecuteQuery("SELECT * FROM Orders");
        var rows = result2.ToList();
        Assert.Equal(3, rows.Count);
    }

    [Fact]
    public void ExecuteQuery_WithCommandTimeout_DoesNotThrow()
    {
        using var result = _connection.ExecuteQuery(
            "SELECT name FROM Users",
            commandTimeout: 30);

        var rows = result.ToList();
        Assert.Equal(3, rows.Count);
    }

    [Fact]
    public void GetParameterPrefix_SqliteConnection_ReturnsAtSign()
    {
        var prefix = AdoNetExt.GetParameterPrefix(_connection);
        Assert.Equal("@", prefix);
    }

    #endregion

    #region Special Characters in Parameter Values

    [Fact]
    public void ExecuteQuery_ParamWithSpecialChars_HandlesCorrectly()
    {
        _connection.ExecuteCommand(
            "INSERT INTO Users (name, email, age) VALUES ({{name}}, {{email}}, {{age}})",
            new { name = "O'Brien", email = "o'brien@test.com", age = 35 });

        using var result = _connection.ExecuteQuery(
            "SELECT name FROM Users WHERE name = {{name}}",
            new { name = "O'Brien" });

        var rows = result.ToList();
        Assert.Single(rows);
        Assert.Equal("O'Brien", (string)rows[0].name);
    }

    [Fact]
    public void ExecuteQuery_ParamWithUnicode_HandlesCorrectly()
    {
        _connection.ExecuteCommand(
            "INSERT INTO Users (name, email, age) VALUES ({{name}}, {{email}}, {{age}})",
            new { name = "München", email = "user@münchen.de", age = 45 });

        using var result = _connection.ExecuteQuery(
            "SELECT name FROM Users WHERE name = {{name}}",
            new { name = "München" });

        var rows = result.ToList();
        Assert.Single(rows);
        Assert.Equal("München", (string)rows[0].name);
    }

    #endregion

    #region Multiple Query Params (DbQueryParams list)

    [Fact]
    public void ExecuteQuery_MultipleDbQueryParams_CombinesCorrectly()
    {
        // Use two separate DbQueryParams sources with different delimiters
        var queryParamsList = new List<DbQueryParams>
        {
            new()
            {
                DataModel = new Dictionary<string, object> { { "minAge", 25 } },
                QueryParamsRegex = @"(?<open_marker>\{\{)(?<param>.*?)?(?<close_marker>\}\})"
            },
            new()
            {
                DataModel = new Dictionary<string, object> { { "maxAge", 35 } },
                QueryParamsRegex = @"(?<open_marker>\[\[)(?<param>.*?)?(?<close_marker>\]\])"
            }
        };

        using var result = _connection.ExecuteQuery(
            "SELECT name FROM Users WHERE age >= {{minAge}} AND age <= [[maxAge]] ORDER BY name",
            queryParamsList);

        var rows = result.ToList();
        Assert.Equal(2, rows.Count);
        Assert.Equal("Jane", (string)rows[0].name);
        Assert.Equal("John", (string)rows[1].name);
    }

    #endregion

    #region Parameter Name Cleaning Tests

    [Fact]
    public void ExecuteQuery_ParamNameWithSpecialChars_CleanedAndWorks()
    {
        // Parameter names with characters that need cleaning (dots, spaces, etc.)
        // These get replaced with underscores by the regex cleaner
        var queryParams = new Dictionary<string, object> { { "user.name", "John" } };
        using var result = _connection.ExecuteQuery(
            "SELECT name FROM Users WHERE name = {{user.name}}",
            queryParams);

        var rows = result.ToList();
        Assert.Single(rows);
        Assert.Equal("John", (string)rows[0].name);
    }

    [Fact]
    public void ExecuteQuery_ParamNameWithSpaces_CleanedAndWorks()
    {
        var queryParams = new Dictionary<string, object> { { "user name", "Jane" } };
        using var result = _connection.ExecuteQuery(
            "SELECT name FROM Users WHERE name = {{user name}}",
            queryParams);

        var rows = result.ToList();
        Assert.Single(rows);
        Assert.Equal("Jane", (string)rows[0].name);
    }

    #endregion

    #region Argument Validation Tests

    [Fact]
    public void ExecuteQuery_NullQuery_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            _connection.ExecuteQuery(null!));
    }

    [Fact]
    public void ExecuteQuery_EmptyQuery_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            _connection.ExecuteQuery(""));
    }

    [Fact]
    public async Task ExecuteQueryAsync_NullQuery_ThrowsArgumentNullException()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await _connection.ExecuteQueryAsync(null!));
    }

    #endregion
}
