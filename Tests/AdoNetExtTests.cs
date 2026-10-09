using Com.H.Data.Common;
using Microsoft.Data.Sqlite;
using System.Data.Common;
using System.Data.Odbc;
using System.Text.Json;

namespace Tests;

/// <summary>
/// Test classes that change AdoNetExt's static settings (DefaultParameterTemplate, MaxParameterNameLength),
/// or depend on their defaults, run one at a time.
/// </summary>
internal static class TestCollections
{
    public const string AdoNetExtStaticSettings = "AdoNetExt static settings";
}

/// <summary>
/// SQLite-based integration tests for the AdoNetExt extension methods.
/// Covers ExecuteQuery, ExecuteQueryAsync, ExecuteCommand, ExecuteCommandAsync,
/// parameterized queries, typed queries, long parameter names, null handling,
/// custom delimiters, multiple parameters, and edge cases.
/// </summary>
[Collection(TestCollections.AdoNetExtStaticSettings)]
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
        AdoNetExt.DefaultParameterTemplate = "{{DefaultParameterPrefix}}vxv_{{ParameterCount}}_{{ParameterName}}";
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
    public void ExecuteQuery_FullyConsumedViaToList_ClosesReaderAutomatically()
    {
        // The iterator's finally block closes the reader when it reaches the end,
        // without requiring the caller to explicitly dispose the wrapper. This makes
        // a subsequent ExecuteQuery on the same connection safe on strict providers.
        using var result = _connection.ExecuteQuery("SELECT id, name FROM Users ORDER BY id");
        var reader = result.Reader;
        Assert.NotNull(reader);

        var rows = result.ToList();

        Assert.Equal(3, rows.Count);
        Assert.True(reader!.IsClosed, "Reader should be auto-closed once .ToList() exhausts the enumerator.");
    }

    [Fact]
    public void ExecuteQuery_PartialConsumptionViaFirst_ClosesReaderAutomatically()
    {
        // .First() stops the iteration after one row and disposes the enumerator — the
        // iterator's finally block runs on enumerator disposal, so the reader is closed
        // even though the result set wasn't fully read.
        using var result = _connection.ExecuteQuery("SELECT id, name FROM Users ORDER BY id");
        var reader = result.Reader;
        Assert.NotNull(reader);

        var firstRow = result.First();
        Assert.Equal("John", (string)firstRow.name);
        Assert.True(reader!.IsClosed, "Reader should be auto-closed after .First() disposes the enumerator early.");
    }

    [Fact]
    public void ExecuteQuery_FullyConsumedViaToList_AllowsNewQueryWithoutExplicitDispose()
    {
        // The observable payoff of the auto-close: no explicit Dispose needed before the
        // next query. On strict providers (SQL Server without MARS, Oracle, etc.) this
        // pattern would throw "already open DataReader" without the auto-close in the
        // iterator. SQLite is permissive so this test would pass either way, but it
        // documents the intended usage pattern.
        var firstRows = _connection.ExecuteQuery("SELECT id, name FROM Users ORDER BY id").ToList();
        Assert.Equal(3, firstRows.Count);

        var secondRows = _connection.ExecuteQuery("SELECT id, product FROM Orders ORDER BY id").ToList();
        Assert.Equal(3, secondRows.Count);
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

    #region Deferred Null Parameter Replacement Tests

    /// <summary>
    /// Test 1: Overlapping patterns with null in lower-priority source.
    /// Two DbQueryParams with overlapping regex patterns. The JSON-marker source
    /// provides a value for "name", while the QueryString-marker source has no "name"
    /// (null). Verifies the value resolves correctly and no raw markers remain.
    /// </summary>
    [Fact]
    public async Task ExecuteQueryAsync_OverlappingPatternsWithNull_ResolvesToValue()
    {
        // JSON body pattern: matches {j{x}} and {{x}}
        var jsonPattern = @"(?<open_marker>\{\{|\{j\{)(?<param>.*?)?(?<close_marker>\}\})";
        // QueryString pattern: matches {qs{x}} and {{x}}
        var qsPattern = @"(?<open_marker>\{\{|\{qs\{)(?<param>.*?)?(?<close_marker>\}\})";

        var queryParamsList = new List<DbQueryParams>
        {
            new()
            {
                DataModel = new Dictionary<string, object> { { "name", "John" } },
                QueryParamsRegex = jsonPattern
            },
            new()
            {
                // name not present → will be null when matched by qsPattern
                DataModel = new Dictionary<string, object>(),
                QueryParamsRegex = qsPattern
            }
        };

        // {{name}} is matchable by BOTH patterns
        await using var result = await _connection.ExecuteQueryAsync(
            "SELECT name, email FROM Users WHERE name = {{name}}",
            queryParamsList);

        var rows = new List<dynamic>();
        await foreach (var row in result) rows.Add(row);

        // The value from jsonPattern wins; qsPattern, which has no value, adds no parameter
        Assert.Single(rows);
        Assert.Equal("John", (string)rows[0].name);
        Assert.Equal("john@test.com", (string)rows[0].email);
    }

    /// <summary>
    /// Test 2: Mutation-proof null replacement.
    /// Pattern A ({a{...}a}) matches a marker with a null param. Pattern B ({{...}})
    /// matches a different marker and replaces it, mutating the query text. The deferred
    /// null replacement for Pattern A re-matches via regex against the mutated query and
    /// still correctly finds and replaces the {a{...}a} marker.
    /// </summary>
    [Fact]
    public async Task ExecuteQueryAsync_MutationProofNullReplacement_StillReplacesAfterMutation()
    {
        var patternA = @"(?<open_marker>\{a\{)(?<param>.*?)?(?<close_marker>\}a\})";
        var patternB = @"(?<open_marker>\{\{)(?<param>.*?)?(?<close_marker>\}\})";

        // Source order matters: after ReduceToUnique(reverse:true), the LAST item
        // in the original list is processed FIRST. We want Pattern A to process first
        // so its null param is deferred, then Pattern B mutates the query.
        var queryParamsList = new List<DbQueryParams>
        {
            new()
            {
                DataModel = new Dictionary<string, object> { { "name", "John" } },
                QueryParamsRegex = patternB
            },
            new()
            {
                // "status" not present → null, will be deferred
                DataModel = new Dictionary<string, object>(),
                QueryParamsRegex = patternA
            }
        };

        // Pattern A matches {a{status}a} (null → deferred).
        // Pattern B then replaces {{name}} with a SQL param, mutating the query.
        // The deferred null replacement re-matches Pattern A's regex against the
        // mutated query and still finds {a{status}a}.
        await using var result = await _connection.ExecuteQueryAsync(
            "SELECT name FROM Users WHERE name = {{name}} OR name = {a{status}a}",
            queryParamsList);

        var rows = new List<dynamic>();
        await foreach (var row in result) rows.Add(row);

        // name = 'John' matches; name = NULL (from {a{status}a}) doesn't match
        Assert.Single(rows);
        Assert.Equal("John", (string)rows[0].name);
    }

    /// <summary>
    /// Test 3: A placeholder one set has no value for takes another set's value.
    /// Pattern A has no value for {{ghost}} and leaves it, then Pattern B (which also matches
    /// {{ghost}}) replaces it with a value. No DBNull parameter is added for Pattern A.
    /// </summary>
    [Fact]
    public async Task ExecuteQueryAsync_NullInOneSet_TakesTheOtherSetsValue()
    {
        // Pattern A: matches only {{x}}
        var patternA = @"(?<open_marker>\{\{)(?<param>.*?)?(?<close_marker>\}\})";
        // Pattern B: matches {{x}} AND {b{x}}
        var patternB = @"(?<open_marker>\{\{|\{b\{)(?<param>.*?)?(?<close_marker>\}\})";

        // After reversal: Pattern A processed first, Pattern B second
        var queryParamsList = new List<DbQueryParams>
        {
            new()
            {
                DataModel = new Dictionary<string, object> { { "ghost", "John" } },
                QueryParamsRegex = patternB
            },
            new()
            {
                // ghost not present → null, deferred by Pattern A
                DataModel = new Dictionary<string, object>(),
                QueryParamsRegex = patternA
            }
        };

        // Pattern A leaves {{ghost}}, which has no value there. Pattern B then replaces it with a value.
        using var command = _connection.CreateCommand();
        await using var result = await command.ExecuteQueryAsync(
            "SELECT name FROM Users WHERE name = {{ghost}}",
            queryParamsList);

        var rows = new List<dynamic>();
        await foreach (var row in result) rows.Add(row);

        // Pattern B's value "John" is the only parameter
        Assert.Single(rows);
        Assert.Equal("John", (string)rows[0].name);
        Assert.Single(command.Parameters.Cast<DbParameter>());
    }

    /// <summary>
    /// Test 4: Simple null parameter replacement.
    /// Single DbQueryParams with a param not in the data model. The marker should be
    /// replaced with a SQL parameter name and the parameter value set to DBNull.Value.
    /// </summary>
    [Fact]
    public void ExecuteQuery_SimpleNullParam_ReplacedWithDbNull()
    {
        // "missing" is not in the data model → null → deferred → replaced with DBNull
        using var result = _connection.ExecuteQuery(
            "SELECT name FROM Users WHERE name = {{name}} AND {{missing}} IS NULL",
            new { name = "John" });

        var rows = result.ToList();

        // {{missing}} is replaced with a SQL param whose value is DBNull.
        // NULL IS NULL evaluates to true in SQL, so the AND clause succeeds.
        Assert.Single(rows);
        Assert.Equal("John", (string)rows[0].name);
    }

    /// <summary>
    /// Test 5: Case-insensitive null replacement.
    /// Query contains {{Name}} (uppercase N) and {{name}} (lowercase n). Neither key
    /// exists in the data model. The regex callback uses OrdinalIgnoreCase comparison,
    /// so both markers are treated as the same param and replaced. Execution succeeds.
    /// </summary>
    [Fact]
    public async Task ExecuteQueryAsync_CaseInsensitiveNullReplacement_WorksCorrectly()
    {
        using var command = _connection.CreateCommand();
        await using var result = await command.ExecuteQueryAsync(
            "SELECT 'found' as result WHERE {{Name}} IS NULL AND {{name}} IS NULL",
            new { unrelated = 1 }); // no Name or name key

        var rows = new List<dynamic>();
        await foreach (var row in result) rows.Add(row);

        // {{Name}} and {{name}} are one DBNull parameter.
        // NULL IS NULL → true for both conditions. Returns one row.
        Assert.Single(rows);
        Assert.Equal("found", (string)rows[0].result);
        Assert.Single(command.Parameters.Cast<DbParameter>());
    }

    [Fact]
    public async Task ExecuteQueryAsync_MoreThanTenPlaceholders_EachGetsItsOwnValue()
    {
        // Slots are numbered: indexes of two digits (10, 11, and the NULL one after them) must not be
        // confused with one-digit ones.
        var values = Enumerable.Range(0, 12).ToDictionary(i => "a" + i, i => (object)("v" + i));
        var query = "SELECT " + string.Join(", ", Enumerable.Range(0, 13).Select(i => "{{a" + i + "}} AS c" + i));
        await using var result = await _connection.ExecuteQueryAsync(query, values);

        var row = (IDictionary<string, object?>)await SingleRowAsync(result);
        for (int i = 0; i < 12; i++)
            Assert.Equal("v" + i, row["c" + i]);
        Assert.Null(row["c12"]);
    }

    [Fact]
    public void BuildPositional_MoreThanTenPlaceholders_ValuesInOrder()
    {
        var values = Enumerable.Range(0, 12).ToDictionary(i => "a" + i, i => (object)("v" + i));
        var query = "SELECT " + string.Join(", ", Enumerable.Range(0, 13).Select(i => "{{a" + i + "}}"));

        var (transformed, paramList) = AdoNetExt.BuildPositionalParameters(query, MakeQueryParams(values));

        Assert.Equal("SELECT " + string.Join(", ", Enumerable.Repeat("?", 13)), transformed);
        Assert.Equal(Enumerable.Range(0, 12).Select(i => (object?)("v" + i)).Append(null), paramList.Select(p => p.Value));
    }

    #endregion

    #region Placeholder Names

    // A placeholder can be named with any characters, the way a JSON body's keys are. Its SQL parameter
    // is named after it with only ASCII letters, digits and underscores kept, a name that comes out the
    // same as another's (compared ignoring case) gets a numbered suffix, and placeholders whose names
    // differ only in case are one parameter.

    private static readonly JsonElement NamesBody = JsonDocument.Parse("""
        {
          "first name": "Ann", "first-name": "Bob", "first_name": "Cy",
          "Email": "a@b.c", "@type": "admin", "price (€)": "5 EUR", "prénom": "Zoé"
        }
        """).RootElement;

    private const string JsonBodyPattern = @"(?<open_marker>\{\{|\{j\{)(?<param>.*?)?(?<close_marker>\}\})";
    private const string QueryStringPattern = @"(?<open_marker>\{\{|\{qs\{)(?<param>.*?)?(?<close_marker>\}\})";

    private static async Task<dynamic> SingleRowAsync(DbAsyncQueryResult<dynamic> result)
    {
        var rows = new List<dynamic>();
        await foreach (var row in result) rows.Add(row);
        return Assert.Single(rows);
    }

    /// <summary>
    /// Every parameter the command got is named with ASCII letters, digits and underscores after the
    /// prefix, no two names are the same ignoring case, and each one is in the SQL.
    /// </summary>
    private static void AssertParameterNames(DbCommand command, int expectedCount)
    {
        var names = command.Parameters.Cast<DbParameter>().Select(p => p.ParameterName).ToList();
        Assert.Equal(expectedCount, names.Count);
        Assert.All(names, name => Assert.Matches("^@[A-Za-z0-9_]+$", name));
        Assert.Equal(names.Count, names.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(names, name => Assert.Contains(name, command.CommandText));
    }

    [Fact]
    public async Task ExecuteQueryAsync_NamesThatCleanToTheSameName_EachGetsItsOwnValue()
    {
        using var command = _connection.CreateCommand();
        await using var result = await command.ExecuteQueryAsync(
            "SELECT {{first name}} AS a, {{first-name}} AS b, {{first_name}} AS c", NamesBody);

        var row = await SingleRowAsync(result);
        Assert.Equal("Ann", (string)row.a);
        Assert.Equal("Bob", (string)row.b);
        Assert.Equal("Cy", (string)row.c);
        AssertParameterNames(command, 3);
    }

    [Fact]
    public async Task ExecuteQueryAsync_NamesThatDifferOnlyInCase_AreOneParameter()
    {
        using var command = _connection.CreateCommand();
        await using var result = await command.ExecuteQueryAsync(
            "SELECT {{Email}} AS a, {{email}} AS b, {{EMAIL}} AS c", NamesBody);

        var row = await SingleRowAsync(result);
        Assert.Equal("a@b.c", (string)row.a);
        Assert.Equal("a@b.c", (string)row.b);
        Assert.Equal("a@b.c", (string)row.c);
        AssertParameterNames(command, 1);
    }

    [Fact]
    public async Task ExecuteQueryAsync_OneNameUnderTwoMarkersOfOnePattern_IsOneParameter()
    {
        // The pattern matches both {{Email}} and {j{Email}}: one name, so one parameter.
        using var command = _connection.CreateCommand();
        await using var result = await command.ExecuteQueryAsync(
            "SELECT {{Email}} AS a, {j{Email}} AS b", NamesBody, JsonBodyPattern);

        var row = await SingleRowAsync(result);
        Assert.Equal("a@b.c", (string)row.a);
        Assert.Equal("a@b.c", (string)row.b);
        AssertParameterNames(command, 1);
    }

    [Theory]
    [InlineData("@type", "admin")]
    [InlineData("price (€)", "5 EUR")]
    [InlineData("prénom", "Zoé")]
    public async Task ExecuteQueryAsync_NameWithAnyCharacters_GetsAValidParameterName(string name, string expected)
    {
        using var command = _connection.CreateCommand();
        await using var result = await command.ExecuteQueryAsync($"SELECT {{{{{name}}}}} AS v", NamesBody);

        var row = await SingleRowAsync(result);
        Assert.Equal(expected, (string)row.v);
        AssertParameterNames(command, 1);
    }

    [Fact]
    public async Task ExecuteQueryAsync_GeneratedNameAlreadyTaken_GetsTheNextFreeSuffix()
    {
        // Names are given left to right. "a-b" cleans to the name "a_b" has, and its first suffixed name
        // is the one "a_b_2" already has, so it gets _3.
        var queryParams = new Dictionary<string, object> { ["a_b"] = "1", ["a-b"] = "2", ["a_b_2"] = "3" };
        using var command = _connection.CreateCommand();
        await using var result = await command.ExecuteQueryAsync(
            "SELECT {{a_b}} AS x, {{a_b_2}} AS z, {{a-b}} AS y", queryParams);

        var row = await SingleRowAsync(result);
        Assert.Equal("1", (string)row.x);
        Assert.Equal("2", (string)row.y);
        Assert.Equal("3", (string)row.z);
        AssertParameterNames(command, 3);
        Assert.Equal(new[] { "@vxv_1_a_b", "@vxv_1_a_b_2", "@vxv_1_a_b_3" },
            command.Parameters.Cast<DbParameter>().Select(p => p.ParameterName));
    }

    [Fact]
    public async Task ExecuteQueryAsync_ShortNameEqualToALongNamesReplacement_StaysUnique()
    {
        // A name longer than MaxParameterNameLength is replaced by lp_vxv_long_name_N, which a short
        // placeholder can be named too.
        var longName = new string('l', 40);
        var queryParams = new Dictionary<string, object> { ["lp_vxv_long_name_1"] = "short", [longName] = "long" };
        using var command = _connection.CreateCommand();
        await using var result = await command.ExecuteQueryAsync(
            $"SELECT {{{{lp_vxv_long_name_1}}}} AS s, {{{{{longName}}}}} AS l", queryParams);

        var row = await SingleRowAsync(result);
        Assert.Equal("short", (string)row.s);
        Assert.Equal("long", (string)row.l);
        AssertParameterNames(command, 2);
    }

    [Fact]
    public async Task ExecuteQueryAsync_TemplateWithoutCount_NamesStayUnique()
    {
        // Without {{ParameterCount}}, two sets' placeholders with the same name would get the same SQL name.
        AdoNetExt.DefaultParameterTemplate = "{{DefaultParameterPrefix}}{{ParameterName}}";
        var queryParamsList = new List<DbQueryParams>
        {
            new() { DataModel = new { x = "curly" } },
            new() { DataModel = new { x = "square" }, QueryParamsRegex = @"(?<open_marker>\[\[)(?<param>.*?)?(?<close_marker>\]\])" }
        };
        using var command = _connection.CreateCommand();
        await using var result = await command.ExecuteQueryAsync("SELECT {{x}} AS a, [[X]] AS b", queryParamsList);

        var row = await SingleRowAsync(result);
        Assert.Equal("curly", (string)row.a);
        Assert.Equal("square", (string)row.b);
        AssertParameterNames(command, 2);
    }

    [Fact]
    public async Task ExecuteQueryAsync_TemplateWithoutParameterName_NamesStayUnique()
    {
        // Every placeholder of a set gets the same generated name, so the suffixes tell them apart.
        AdoNetExt.DefaultParameterTemplate = "{{DefaultParameterPrefix}}p{{ParameterCount}}";
        using var command = _connection.CreateCommand();
        var run = Task.Run(async () =>
        {
            await using var result = await command.ExecuteQueryAsync(
                "SELECT {{a}} AS a, {{b}} AS b, {{c}} AS c", new { a = "1", b = "2" });
            return await SingleRowAsync(result);
        });
        Assert.Same(run, await Task.WhenAny(run, Task.Delay(TimeSpan.FromSeconds(30))));

        var row = await run;
        Assert.Equal("1", (string)row.a);
        Assert.Equal("2", (string)row.b);
        Assert.Null(row.c);
        Assert.Equal(new[] { "@p1", "@p1_2", "@p1_3" },
            command.Parameters.Cast<DbParameter>().Select(p => p.ParameterName));
    }

    [Fact]
    public async Task ExecuteQueryAsync_SetsWithOnePattern_AreMerged_TheLaterValueWinningEvenWhenNull()
    {
        // Both sets use the default pattern, so they are one set: the later one's null is the value.
        // Under a pattern of its own, the later set's null would leave {{x}} to the earlier set.
        var queryParamsList = new List<DbQueryParams>
        {
            new() { DataModel = new { x = "earlier", y = "earlier" } },
            new() { DataModel = new Dictionary<string, object?> { ["x"] = null } }
        };
        await using var result = await _connection.ExecuteQueryAsync("SELECT {{x}} AS x, {{y}} AS y", queryParamsList);

        var row = await SingleRowAsync(result);
        Assert.Null(row.x);
        Assert.Equal("earlier", (string)row.y);
    }

    [Fact]
    public async Task ExecuteQueryAsync_SetsWithOnePattern_AreReadInThePlaceOfTheLastOfThem()
    {
        // The first and third sets share a pattern, so they are one set, read in the third's place:
        // before the second set, whose value for {{x}} it therefore beats.
        var queryParamsList = new List<DbQueryParams>
        {
            new() { DataModel = new { x = "first" }, QueryParamsRegex = JsonBodyPattern },
            new() { DataModel = new { x = "second" }, QueryParamsRegex = QueryStringPattern },
            new() { DataModel = new Dictionary<string, object>(), QueryParamsRegex = JsonBodyPattern }
        };
        await using var result = await _connection.ExecuteQueryAsync("SELECT {{x}} AS x", queryParamsList);

        var row = await SingleRowAsync(result);
        Assert.Equal("first", (string)row.x);
    }

    [Fact]
    public async Task ExecuteQueryAsync_PlaceholderAnotherSetResolves_GetsNoNullParameter()
    {
        // The query-string set is read first and has no "name", so {{name}} is left for the JSON set,
        // which has it. The command gets that one parameter and no unused NULL one.
        var queryParamsList = new List<DbQueryParams>
        {
            new() { DataModel = new { name = "John" }, QueryParamsRegex = JsonBodyPattern },
            new() { DataModel = new Dictionary<string, object>(), QueryParamsRegex = QueryStringPattern }
        };
        using var command = _connection.CreateCommand();
        await using var result = await command.ExecuteQueryAsync(
            "SELECT name FROM Users WHERE name = {{name}}", queryParamsList);

        var row = await SingleRowAsync(result);
        Assert.Equal("John", (string)row.name);
        AssertParameterNames(command, 1);
    }

    [Theory]
    [InlineData("SELECT {{id}} AS a, '{{' AS lit, {{id}} AS b")]
    [InlineData("SELECT {{id}} AS a, '{{' AS lit, {{ID}} AS b")]
    [InlineData("SELECT {{id}} AS a /* {{ */, {{id}} AS b")]
    public async Task ExecuteQueryAsync_StrayOpenMarkerBeforeASecondUse_BindsBoth(string query)
    {
        // The pattern pairs the stray "{{" with the second {{id}}'s "}}", so that {{id}} is not a match of
        // its own: it is bound by its text, as the first {{id}} is.
        await using var result = await _connection.ExecuteQueryAsync(query, new { id = 7 });

        var row = (IDictionary<string, object?>)await SingleRowAsync(result);
        Assert.Equal(7L, row["a"]);
        Assert.Equal(7L, row["b"]);
    }

    [Fact]
    public async Task ExecuteQueryAsync_PipeDelimitersBesideAPipeOperator_BindBothUses()
    {
        // The pattern pairs the operator's pipe with the second |a|'s opening pipe, so that |a| is no
        // match of its own: its text is bound, as the first |a| is.
        await using var result = await _connection.ExecuteQueryAsync(
            "SELECT |a| | 4 AS x, |a| AS y", new { a = 1 }, @"(?<open_marker>\|)(?<param>.*?)?(?<close_marker>\|)");

        var row = (IDictionary<string, object?>)await SingleRowAsync(result);
        Assert.Equal(5L, row["x"]);
        Assert.Equal(1L, row["y"]);
    }

    [Fact]
    public async Task ExecuteQueryAsync_PatternThatAlsoMatchesTheCharacterBefore_KeepsIt()
    {
        // The pattern takes the character before the marker too (an escape check without a lookbehind):
        // the "(" and the "," stay, and only the placeholders become parameters.
        await using var result = await _connection.ExecuteQueryAsync(
            "SELECT 1 AS n WHERE 'b' IN({{a}},{{b}})", new { a = "a", b = "b" },
            @"(?:^|[^\\])(?<open_marker>\{\{)(?<param>.*?)(?<close_marker>\}\})");

        var row = await SingleRowAsync(result);
        Assert.Equal(1L, (long)row.n);
    }

    // A colon-style placeholder with no close marker, which also takes the character before the colon,
    // so that a PostgreSQL :: cast is not a placeholder.
    private const string ColonPattern = @"(?:^|[^:])(?<open_marker>:)(?<param>\w+)(?<close_marker>)";

    [Theory]
    [InlineData("SELECT 1 AS v WHERE 'b' IN(:a,:b)", 1L)]
    [InlineData("SELECT -:c AS v", -3L)]
    public async Task ExecuteQueryAsync_PatternWithOnlyAnOpenMarker_KeepsTheCharacterBefore(string query, long expected)
    {
        await using var result = await _connection.ExecuteQueryAsync(query, new { a = "a", b = "b", c = 3 }, ColonPattern);

        var row = await SingleRowAsync(result);
        Assert.Equal(expected, (long)row.v);
    }

    [Fact]
    public async Task ExecuteQueryAsync_PatternWithOnlyAnOpenMarker_NullThatBeginsWithAResolvedName_KeepsTheCharacterBefore()
    {
        // ":ab" has no value and begins with the resolved ":a"; its NULL keeps the "(" the pattern
        // matched before the colon.
        await using var result = await _connection.ExecuteQueryAsync(
            "SELECT :a AS x, coalesce(:ab, 0) AS y", new { a = 1 }, ColonPattern);

        var row = await SingleRowAsync(result);
        Assert.Equal(1L, (long)row.x);
        Assert.Equal(0L, (long)row.y);
    }

    [Theory]
    [InlineData("SELECT :a AS x, 1-:a AS y")]
    [InlineData("SELECT :a AS x, 1-:A AS y")]
    public async Task ExecuteQueryAsync_PatternWithOnlyAnOpenMarker_UseThePatternSkips_IsBoundByItsText(string query)
    {
        // The pattern allows only a space, "(", "," or "=" before the colon, so it skips "-:a"; the
        // placeholder's text binds that use too, written in any case.
        await using var result = await _connection.ExecuteQueryAsync(
            query, new { a = 1 }, @"(?:^|[\s(,=])(?<open_marker>:)(?<param>\w+)");

        var row = await SingleRowAsync(result);
        Assert.Equal(1L, (long)row.x);
        Assert.Equal(0L, (long)row.y);
    }

    [Theory]
    [InlineData("SELECT a$ AS x, 1-a$ AS y")]
    [InlineData("SELECT a$ AS x, 1-A$ AS y")]
    public async Task ExecuteQueryAsync_PatternWithOnlyACloseMarker_UseThePatternSkips_IsBoundByItsText(string query)
    {
        // The pattern allows only a space, "(", "," or "=" before the name, so it skips "-a$"; the
        // placeholder's text binds that use too, written in any case.
        await using var result = await _connection.ExecuteQueryAsync(
            query, new { a = 1 }, @"(?:^|[\s(,=])(?<param>\w+)(?<close_marker>\$)");

        var row = await SingleRowAsync(result);
        Assert.Equal(1L, (long)row.x);
        Assert.Equal(0L, (long)row.y);
    }

    [Theory]
    [InlineData("SELECT a$ AS x, coalesce(ba$, 0) AS y")]
    [InlineData("SELECT a$ AS x, coalesce(éa$, 0) AS y")]
    [InlineData("SELECT a$ AS x, coalesce(1a$, 0) AS y")]
    [InlineData("SELECT a$ AS x, coalesce(Ba$, 0) AS y")]
    public async Task ExecuteQueryAsync_PatternWithOnlyACloseMarker_NameThatEndsALongerName_LeavesItAlone(string query)
    {
        // "a$" is also the end of "ba$", "éa$", "1a$" and "Ba$", which have no value: binding a$ by its
        // text must not reach into them (a name character is any letter or digit, not only ASCII).
        await using var result = await _connection.ExecuteQueryAsync(
            query, new { a = 1 }, @"(?<param>\w+)(?<close_marker>\$)");

        var row = await SingleRowAsync(result);
        Assert.Equal(1L, (long)row.x);
        Assert.Equal(0L, (long)row.y);
    }

    [Fact]
    public async Task ExecuteQueryAsync_PatternWithOnlyAnOpenMarker_NameThatBeginsALongerName_LeavesItAlone()
    {
        // ":name" is also the start of ":name_ar" and ":nameé", which have no value (a name character is
        // any letter, not only an ASCII one).
        await using var result = await _connection.ExecuteQueryAsync(
            "SELECT :name AS x, :name_ar AS y, :nameé AS z", new { name = "n" }, ColonPattern);

        var row = await SingleRowAsync(result);
        Assert.Equal("n", (string)row.x);
        Assert.Null(row.y);
        Assert.Null(row.z);
    }

    [Theory]
    [InlineData("SELECT :p1 AS x, coalesce(:p10, 0) AS y")]
    [InlineData("SELECT :user AS x, coalesce(:userId, 0) AS y")]
    public async Task ExecuteQueryAsync_PatternWithOnlyAnOpenMarker_NameThatBeginsANumberedOrCamelCaseName_LeavesItAlone(string query)
    {
        // ":p10" and ":userId" have no value; ":p1" and ":user" must not reach into them.
        await using var result = await _connection.ExecuteQueryAsync(
            query, new Dictionary<string, object> { ["p1"] = 1, ["user"] = 1 }, ColonPattern);

        var row = await SingleRowAsync(result);
        Assert.Equal(1L, (long)row.x);
        Assert.Equal(0L, (long)row.y);
    }

    [Fact]
    public async Task ExecuteQueryAsync_PatternWithOnlyAnOpenMarker_NullUseThePatternSkips_StaysAsWritten()
    {
        // The pattern skips ":token" in the literal; token has no value, so only its match becomes NULL.
        await using var result = await _connection.ExecuteQueryAsync(
            "SELECT 'session:token' AS label, :token AS t",
            new { a = 1 }, @"(?:^|[\s(,=])(?<open_marker>:)(?<param>\w+)");

        var row = await SingleRowAsync(result);
        Assert.Equal("session:token", (string)row.label);
        Assert.Null(row.t);
    }

    [Fact]
    public void BuildPositional_PatternWithOnlyAnOpenMarker_UseThePatternSkips_StaysAsWritten()
    {
        // ODBC and OleDb replace only the pattern's matches; the named path also binds "1-:a" by its text.
        var (query, paramList) = AdoNetExt.BuildPositionalParameters(
            "SELECT :a AS x, 1-:a AS y", MakeQueryParams(new { a = 1 }, @"(?:^|[\s(,=])(?<open_marker>:)(?<param>\w+)"));

        Assert.Equal("SELECT ? AS x, 1-:a AS y", query);
        Assert.Equal(new[] { ("a", (object?)1) }, paramList);
    }

    [Fact]
    public async Task ExecuteQueryAsync_PatternThatAlsoMatchesTheCharacterAfter_KeepsIt_ForANull()
    {
        // The pattern takes the "," or ")" after the name too; x has no value.
        await using var result = await _connection.ExecuteQueryAsync(
            "SELECT coalesce(:x, 0) AS v", new { a = 1 }, @"(?<open_marker>:)(?<param>\w+)(?:,|\))");

        var row = await SingleRowAsync(result);
        Assert.Equal(0L, (long)row.v);
    }

    [Theory]
    [InlineData("SELECT -:c AS v", "SELECT -? AS v", ColonPattern)]
    [InlineData("SELECT 1 WHERE 'b' IN({{a}},{{b}})", "SELECT 1 WHERE 'b' IN(?,?)", @"(?:^|[^\\])(?<open_marker>\{\{)(?<param>.*?)(?<close_marker>\}\})")]
    [InlineData("SELECT 1 WHERE 'b' IN(:a,:b)", "SELECT 1 WHERE 'b' IN(?,?)", @"(?<open_marker>:)(?<param>\w+)(?:,|\))")]
    [InlineData("SELECT coalesce(a$, 0)", "SELECT coalesce(?, 0)", @"(?:^|[(,\s])(?<param>\w+)(?<close_marker>\$)")]
    public void BuildPositional_PatternThatAlsoMatchesTextAroundThePlaceholder_KeepsIt(string query, string expected, string pattern)
    {
        var (transformed, _) = AdoNetExt.BuildPositionalParameters(query, MakeQueryParams(new { a = "a", b = "b", c = 3 }, pattern));

        Assert.Equal(expected, transformed);
    }

    [Theory]
    [InlineData("SELECT '{{' AS s, {{a}} AS x, {{a}} AS y, :b AS z")]
    [InlineData("SELECT '{{' AS s, {{a}} AS x, {{A}} AS y, :b AS z")]
    public async Task ExecuteQueryAsync_PatternMixingTwoAndOneMarkerAlternatives_StrayOpenMarker_BindsBoth(string query)
    {
        // The pattern has a {{name}} and a :name alternative; the {{name}} uses are bound by their text,
        // in any case, as with a {{name}}-only pattern, though :b is there too.
        await using var result = await _connection.ExecuteQueryAsync(
            query, new { a = 1, b = 2 },
            @"(?<open_marker>\{\{)(?<param>.*?)(?<close_marker>\}\})|(?<open_marker>:)(?<param>\w+)");

        var row = (IDictionary<string, object?>)await SingleRowAsync(result);
        Assert.Equal("{{", row["s"]);
        Assert.Equal(1L, row["x"]);
        Assert.Equal(1L, row["y"]);
        Assert.Equal(2L, row["z"]);
    }

    [Fact]
    public async Task ExecuteQueryAsync_PatternWithOnlyAnOpenMarker_TextComparedAsNames()
    {
        // ":STRAẞE" is not ":straße" ignoring case the way names are compared (ordinal), so it has no
        // value; a culture-aware comparison would treat ẞ as ß and bind it to straße's value.
        await using var result = await _connection.ExecuteQueryAsync(
            "SELECT :straße AS a, :STRAẞE AS b", new Dictionary<string, object> { ["straße"] = 1 },
            @"(?<open_marker>:)(?<param>\w+)");

        var row = await SingleRowAsync(result);
        Assert.Equal(1L, (long)row.a);
        Assert.Null(row.b);
    }

    private const string NoMarkerGroupsPattern = @"\{\{(?<param>\w+)\}\}";
    private const string EscapablePattern = @"(?<!\\)(?<open_marker>\{\{)(?<param>\w+)(?<close_marker>\}\})";

    [Fact]
    public async Task ExecuteQueryAsync_PatternWithoutMarkerGroups_BindsOnlyItsMatches()
    {
        // Without open_marker and close_marker groups, a placeholder's text is its bare name, which is
        // also the column's name here, and part of {{username}}, which has no value: only the pattern's
        // matches are parameters.
        using var command = _connection.CreateCommand();
        await using var result = await command.ExecuteQueryAsync(
            "SELECT name FROM Users WHERE name = {{name}} AND {{username}} IS NULL", new { name = "Jane" }, NoMarkerGroupsPattern);

        var row = await SingleRowAsync(result);
        Assert.Equal("Jane", (string)row.name);
        Assert.StartsWith("SELECT name FROM Users WHERE name = @", command.CommandText);
    }

    [Fact]
    public void BuildPositional_PatternWithoutMarkerGroups_ReplacesOnlyItsMatches()
    {
        var (query, paramList) = AdoNetExt.BuildPositionalParameters(
            "DELETE FROM Users WHERE name = {{name}}", MakeQueryParams(new { name = "Jane" }, NoMarkerGroupsPattern));

        Assert.Equal("DELETE FROM Users WHERE name = ?", query);
        Assert.Equal(new[] { ("name", (object?)"Jane") }, paramList);
    }

    [Fact]
    public void BuildPositional_MarkerThePatternDoesNotMatch_StaysAsWritten()
    {
        // The pattern skips an escaped marker, so its text stays, though {{name}} has a value.
        var (query, paramList) = AdoNetExt.BuildPositionalParameters(
            @"SELECT '\{{name}}' AS lit, {{name}} AS v", MakeQueryParams(new { name = "Jane" }, EscapablePattern));

        Assert.Equal(@"SELECT '\{{name}}' AS lit, ? AS v", query);
        Assert.Equal(new[] { ("name", (object?)"Jane") }, paramList);
    }

    [Fact]
    public async Task ExecuteQueryAsync_SourceSpecificMarkers_TakeOnlyTheirSetsValue()
    {
        // The later set (query string) wins {{x}}; {j{x}} is only the JSON pattern's, {qs{x}} only the query string's.
        var queryParamsList = new List<DbQueryParams>
        {
            new() { DataModel = new { x = "body" }, QueryParamsRegex = JsonBodyPattern },
            new() { DataModel = new { x = "query" }, QueryParamsRegex = QueryStringPattern }
        };
        await using var result = await _connection.ExecuteQueryAsync(
            "SELECT {{x}} AS a, {j{x}} AS b, {qs{x}} AS c", queryParamsList);

        var row = await SingleRowAsync(result);
        Assert.Equal("query", (string)row.a);
        Assert.Equal("body", (string)row.b);
        Assert.Equal("query", (string)row.c);
    }

    #endregion

    #region Positional Parameter Tests

    [Fact]
    public void IsPositionalParameterPrefix_QuestionMark_ReturnsTrue()
    {
        Assert.True(AdoNetExt.IsPositionalParameterPrefix("?"));
    }

    [Fact]
    public void IsPositionalParameterPrefix_AtSign_ReturnsFalse()
    {
        Assert.False(AdoNetExt.IsPositionalParameterPrefix("@"));
    }

    [Fact]
    public void IsPositionalParameterPrefix_Colon_ReturnsFalse()
    {
        Assert.False(AdoNetExt.IsPositionalParameterPrefix(":"));
    }

    private static List<DbQueryParams> MakeQueryParams(object dataModel,
        string regex = @"(?<open_marker>\{\{)(?<param>.*?)?(?<close_marker>\}\})")
    {
        return [new DbQueryParams { DataModel = dataModel, QueryParamsRegex = regex }];
    }

    [Fact]
    public void BuildPositional_SingleParam_ReplacesWithQuestionMark()
    {
        var (query, paramList) = AdoNetExt.BuildPositionalParameters(
            "SELECT * FROM Users WHERE name = {{name}}",
            MakeQueryParams(new { name = "Jane" }));

        Assert.Equal("SELECT * FROM Users WHERE name = ?", query);
        Assert.Single(paramList);
        Assert.Equal("name", paramList[0].Name);
        Assert.Equal("Jane", paramList[0].Value);
    }

    [Fact]
    public void BuildPositional_MultipleParams_OrderedLeftToRight()
    {
        var (query, paramList) = AdoNetExt.BuildPositionalParameters(
            "SELECT * FROM Users WHERE age >= {{minAge}} AND age <= {{maxAge}}",
            MakeQueryParams(new { minAge = 25, maxAge = 35 }));

        Assert.Equal("SELECT * FROM Users WHERE age >= ? AND age <= ?", query);
        Assert.Equal(2, paramList.Count);
        Assert.Equal("minAge", paramList[0].Name);
        Assert.Equal(25, paramList[0].Value);
        Assert.Equal("maxAge", paramList[1].Name);
        Assert.Equal(35, paramList[1].Value);
    }

    [Fact]
    public void BuildPositional_SameParamTwice_DuplicatesInOrder()
    {
        var (query, paramList) = AdoNetExt.BuildPositionalParameters(
            "SELECT * FROM Users WHERE name = {{name}} OR email LIKE {{name}}",
            MakeQueryParams(new { name = "John" }));

        Assert.Equal("SELECT * FROM Users WHERE name = ? OR email LIKE ?", query);
        Assert.Equal(2, paramList.Count);
        // Both entries should have the same value
        Assert.Equal("name", paramList[0].Name);
        Assert.Equal("John", paramList[0].Value);
        Assert.Equal("name", paramList[1].Name);
        Assert.Equal("John", paramList[1].Value);
    }

    [Fact]
    public void BuildPositional_NullParam_ProducesNullValue()
    {
        var (query, paramList) = AdoNetExt.BuildPositionalParameters(
            "SELECT * FROM Users WHERE email = {{email}}",
            MakeQueryParams(new { email = (string?)null }));

        Assert.Equal("SELECT * FROM Users WHERE email = ?", query);
        Assert.Single(paramList);
        Assert.Equal("email", paramList[0].Name);
        Assert.Null(paramList[0].Value);
    }

    [Fact]
    public void BuildPositional_MissingParam_ProducesNullValue()
    {
        // {{missing}} is not in the data model — should still become ? with null value
        var (query, paramList) = AdoNetExt.BuildPositionalParameters(
            "SELECT * FROM Users WHERE name = {{name}} AND {{missing}} IS NULL",
            MakeQueryParams(new { name = "John" }));

        Assert.Equal("SELECT * FROM Users WHERE name = ? AND ? IS NULL", query);
        Assert.Equal(2, paramList.Count);
        Assert.Equal("name", paramList[0].Name);
        Assert.Equal("John", paramList[0].Value);
        Assert.Equal("missing", paramList[1].Name);
        Assert.Null(paramList[1].Value);
    }

    [Fact]
    public void BuildPositional_ThreeParamsWithRepeats_CorrectOrder()
    {
        // {{a}} appears at positions 1 and 3, {{b}} at position 2
        var (query, paramList) = AdoNetExt.BuildPositionalParameters(
            "SELECT * FROM t WHERE x = {{a}} AND y = {{b}} AND z = {{a}}",
            MakeQueryParams(new { a = 1, b = 2 }));

        Assert.Equal("SELECT * FROM t WHERE x = ? AND y = ? AND z = ?", query);
        Assert.Equal(3, paramList.Count);
        Assert.Equal(("a", (object?)1), paramList[0]);
        Assert.Equal(("b", (object?)2), paramList[1]);
        Assert.Equal(("a", (object?)1), paramList[2]);
    }

    [Fact]
    public void BuildPositional_CustomDelimiters_WorksCorrectly()
    {
        var (query, paramList) = AdoNetExt.BuildPositionalParameters(
            "SELECT * FROM Users WHERE name = [[name]]",
            MakeQueryParams(new { name = "Jane" },
                @"(?<open_marker>\[\[)(?<param>.*?)?(?<close_marker>\]\])"));

        Assert.Equal("SELECT * FROM Users WHERE name = ?", query);
        Assert.Single(paramList);
        Assert.Equal("Jane", paramList[0].Value);
    }

    [Fact]
    public void BuildPositional_DictionaryParam_WorksCorrectly()
    {
        var queryParams = new Dictionary<string, object> { { "name", "Bob" }, { "age", 40 } };
        var (query, paramList) = AdoNetExt.BuildPositionalParameters(
            "SELECT * FROM Users WHERE name = {{name}} AND age = {{age}}",
            MakeQueryParams(queryParams));

        Assert.Equal("SELECT * FROM Users WHERE name = ? AND age = ?", query);
        Assert.Equal(2, paramList.Count);
        Assert.Equal("Bob", paramList[0].Value);
        Assert.Equal(40, paramList[1].Value);
    }

    [Fact]
    public void BuildPositional_NoPlaceholders_ReturnsUnchangedQuery()
    {
        var (query, paramList) = AdoNetExt.BuildPositionalParameters(
            "SELECT * FROM Users ORDER BY id",
            MakeQueryParams(new { name = "unused" }));

        Assert.Equal("SELECT * FROM Users ORDER BY id", query);
        Assert.Empty(paramList);
    }

    [Fact]
    public void BuildPositional_MultipleDbQueryParams_CombinesValues()
    {
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

        var (query, paramList) = AdoNetExt.BuildPositionalParameters(
            "SELECT * FROM Users WHERE age >= {{minAge}} AND age <= [[maxAge]]",
            queryParamsList);

        Assert.Equal("SELECT * FROM Users WHERE age >= ? AND age <= ?", query);
        Assert.Equal(2, paramList.Count);
        Assert.Equal(25, paramList[0].Value);
        Assert.Equal(35, paramList[1].Value);
    }

    [Fact]
    public void BuildPositional_PlaceholderMatchedBySeveralPatterns_IsReplacedOnce()
    {
        // Both patterns match {{name}}. It used to be replaced once per pattern at the same position,
        // which cut into the text after it ("name = ? = 1"), and bound the value twice.
        var queryParamsList = new List<DbQueryParams>
        {
            new() { DataModel = new { name = "John" }, QueryParamsRegex = JsonBodyPattern },
            new() { DataModel = new Dictionary<string, object>(), QueryParamsRegex = QueryStringPattern }
        };

        var (query, paramList) = AdoNetExt.BuildPositionalParameters(
            "SELECT * FROM Users WHERE name = {{name}} AND id = 1", queryParamsList);

        Assert.Equal("SELECT * FROM Users WHERE name = ? AND id = 1", query);
        Assert.Equal(new[] { ("name", (object?)"John") }, paramList);
    }

    [Fact]
    public void BuildPositional_SourceSpecificMarkers_TakeOnlyTheirSetsValue()
    {
        var queryParamsList = new List<DbQueryParams>
        {
            new() { DataModel = new { x = "body" }, QueryParamsRegex = JsonBodyPattern },
            new() { DataModel = new { x = "query" }, QueryParamsRegex = QueryStringPattern }
        };

        var (query, paramList) = AdoNetExt.BuildPositionalParameters(
            "SELECT {{x}} AS a, {j{x}} AS b, {qs{x}} AS c", queryParamsList);

        Assert.Equal("SELECT ? AS a, ? AS b, ? AS c", query);
        Assert.Equal(new object?[] { "query", "body", "query" }, paramList.Select(p => p.Value));
    }

    [Fact]
    public void BuildPositional_NullInTheLaterSet_LeavesThePlaceholderToTheEarlierSet()
    {
        // As on the named path: a set whose value is null doesn't claim the placeholder.
        var queryParamsList = new List<DbQueryParams>
        {
            new() { DataModel = new { x = "query" }, QueryParamsRegex = QueryStringPattern },
            new() { DataModel = new Dictionary<string, object?> { ["x"] = null }, QueryParamsRegex = JsonBodyPattern }
        };

        var (query, paramList) = AdoNetExt.BuildPositionalParameters("SELECT {{x}} AS a", queryParamsList);

        Assert.Equal("SELECT ? AS a", query);
        Assert.Equal(new[] { ("x", (object?)"query") }, paramList);
    }

    [Fact]
    public void BuildPositional_QueryWithPrivateUseCharacters_KeepsThem()
    {
        // Placeholders are marked internally with a private-use character the query doesn't contain:
        // here the first two are in it, the first around a 0 the way a placeholder's mark would be.
        var (query, paramList) = AdoNetExt.BuildPositionalParameters(
            "SELECT '\uE0000\uE000' AS a, '\uE001' AS b, {{x}} AS c",
            MakeQueryParams(new { x = 7 }));

        Assert.Equal("SELECT '\uE0000\uE000' AS a, '\uE001' AS b, ? AS c", query);
        Assert.Equal(new[] { ("x", (object?)7) }, paramList);
    }

    #endregion

    #region Statements After The First Result Set

    // Only the first result set's rows are returned. Before 10.1.0.10 the reader was closed as
    // soon as those rows ran out, without moving on through NextResult. The later statements
    // still ran, because closing the reader runs the rest of the batch on SQL Server and SQLite,
    // but closing also discards their errors. So a failing later statement (SQL Server's THROW
    // after a SELECT, a constraint violation in a later INSERT) left the caller with a success.

    [Fact]
    public async Task ExecuteQueryAsync_ErrorAfterFirstResultSet_Throws()
    {
        await using var result = await _connection.ExecuteQueryAsync(
            "SELECT name FROM Users WHERE id = 1; SELECT * FROM missing_table;");

        await Assert.ThrowsAsync<SqliteException>(async () =>
        {
            await foreach (var _ in result) { }
        });
    }

    [Fact]
    public async Task ExecuteQueryAsync_ErrorAfterEmptyFirstResultSet_Throws()
    {
        await using var result = await _connection.ExecuteQueryAsync(
            "SELECT name FROM Users WHERE id = -1; SELECT * FROM missing_table;");

        await Assert.ThrowsAsync<SqliteException>(async () =>
        {
            await foreach (var _ in result) { }
        });
    }

    [Fact]
    public void ExecuteQuery_ErrorAfterFirstResultSet_Throws()
    {
        using var result = _connection.ExecuteQuery(
            "SELECT name FROM Users WHERE id = 1; SELECT * FROM missing_table;");

        Assert.Throws<SqliteException>(() => result.ToList());
    }

    [Fact]
    public async Task ExecuteCommandAsync_ErrorAfterFirstResultSet_Throws()
    {
        await Assert.ThrowsAsync<SqliteException>(() => _connection.ExecuteCommandAsync(
            "SELECT name FROM Users WHERE id = 1; SELECT * FROM missing_table;"));
    }

    [Fact]
    public async Task ExecuteQueryAsync_ReturnsOnlyTheFirstResultSet()
    {
        await using var result = await _connection.ExecuteQueryAsync(
            "SELECT name FROM Users WHERE id = 1; SELECT name FROM Users WHERE id = 2;");

        var rows = new List<dynamic>();
        await foreach (var row in result) rows.Add(row);

        Assert.Single(rows);
        Assert.Equal("John", (string)rows[0].name);
    }

    [Fact]
    public async Task ExecuteQueryAsync_StoppingEarly_DoesNotThrow_AndFreesTheConnection()
    {
        // A caller that stops after the first row has chosen not to read on, so the error in the
        // rest of the batch is not raised, and nothing is thrown from disposal.
        await using (var result = await _connection.ExecuteQueryAsync(
            "SELECT name FROM Users; SELECT * FROM missing_table;"))
        {
            await foreach (var _ in result) break;
        }

        using var next = _connection.ExecuteQuery("SELECT name FROM Users WHERE id = 2");
        Assert.Equal("Jane", (string)next.First().name);
    }

    private const string ErrorAfterRows = "SELECT name FROM Users ORDER BY id; SELECT * FROM missing_table;";

    [Fact]
    public void ReadingThatThrows_ClosesTheReader()
    {
        using var result = _connection.ExecuteQuery(ErrorAfterRows);

        Assert.Throws<SqliteException>(() => result.ToList());

        Assert.True(result.Reader!.IsClosed);
    }

    [Fact]
    public async Task ReadingAgainAfterTheEnd_Throws()
    {
        // The reader closed when the rows ran out, so a second read finds it closed.
        await using var result = await _connection.ExecuteQueryAsync("SELECT name FROM Users");
        await foreach (var _ in result) { }

        await Assert.ThrowsAsync<InvalidOperationException>(async () => { await foreach (var _ in result) { } });
    }

    #endregion

    #region Single Column Without A Name

    // A single column with no name (SQL Server's COUNT(*) without an alias) is returned as its
    // bare value. Before 10.1.0.10 every row also came back as an object with an empty key, and
    // a NULL came back three times.

    [Fact]
    public async Task UnnamedSingleColumn_ReturnsOneValuePerRow()
    {
        await using var result = await _connection.ExecuteQueryAsync(
            "SELECT age AS \"\" FROM Users WHERE id IN (1, 2) ORDER BY id");

        var rows = new List<object?>();
        await foreach (var row in result) rows.Add((object?)row);

        Assert.Equal(new object?[] { 30L, 25L }, rows);
    }

    [Fact]
    public void UnnamedSingleColumn_Typed_ReturnsOneValuePerRow_AndDefaultForNull()
    {
        // A NULL unnamed scalar used to come back as null, DBNull and an object; the typed API
        // dropped the null and mapped the other two. It must stay one element per row.
        using var values = _connection.ExecuteQuery<long>(
            "SELECT age AS \"\" FROM Users WHERE id IN (1, 2) ORDER BY id");
        Assert.Equal(new long[] { 30, 25 }, values.ToList());

        using var nullRow = _connection.ExecuteQuery<long>("SELECT NULL AS \"\"");
        Assert.Equal(new long[] { 0 }, nullRow.ToList());
    }

    [Fact]
    public async Task UnnamedSingleColumn_Null_ReturnsOneNull()
    {
        await using var result = await _connection.ExecuteQueryAsync("SELECT NULL AS \"\"");

        var rows = new List<object?>();
        await foreach (var row in result) rows.Add((object?)row);

        Assert.Equal(new object?[] { null }, rows);
    }

    #endregion

    #region ExecuteCommand Connection Handling

    [Fact]
    public async Task ExecuteCommandAsync_CloseConnectionOnExit_ClosesTheConnection_WithoutDisposingIt()
    {
        // The flag was ignored. It must close, not dispose: SqlClient, Npgsql and MySqlConnector
        // can't open a disposed connection again. SQLite can, so the Disposed event is checked.
        using var connection = new SqliteConnection("Data Source=:memory:");
        var disposed = false;
        connection.Disposed += (_, _) => disposed = true;
        await connection.OpenAsync();

        await connection.ExecuteCommandAsync("SELECT 1", closeConnectionOnExit: true);

        Assert.Equal(System.Data.ConnectionState.Closed, connection.State);
        Assert.False(disposed);

        // Used again: the library opens it, and closes it again.
        await connection.ExecuteCommandAsync("SELECT 1", closeConnectionOnExit: true);
        Assert.Equal(System.Data.ConnectionState.Closed, connection.State);
        Assert.False(disposed);
    }

    [Fact]
    public async Task ExecuteCommandAsync_CloseConnectionOnExit_ClosesWhenTheFirstStatementFails()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        await Assert.ThrowsAsync<SqliteException>(() =>
            connection.ExecuteCommandAsync("SELECT * FROM missing_table", closeConnectionOnExit: true));

        Assert.Equal(System.Data.ConnectionState.Closed, connection.State);
    }

    [Fact]
    public async Task ExecuteCommandAsync_OnACommand_CloseConnectionOnExit_ClosesTheConnection()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        var disposed = false;
        connection.Disposed += (_, _) => disposed = true;
        await connection.OpenAsync();
        using var command = connection.CreateCommand();

        await command.ExecuteCommandAsync("SELECT 1", closeConnectionOnExit: true);

        Assert.Equal(System.Data.ConnectionState.Closed, connection.State);
        Assert.False(disposed);
    }

    [Fact]
    public async Task ExecuteCommandAsync_WithoutTheFlag_LeavesTheConnectionOpen()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        await connection.ExecuteCommandAsync("CREATE TABLE t (a INTEGER)");

        Assert.Equal(System.Data.ConnectionState.Open, connection.State);
    }

    [Fact]
    public async Task ExecuteCommandAsync_NullCommand_StillThrowsArgumentNullException()
    {
        // The close in the finally must not turn the argument check into a NullReferenceException.
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            ((DbCommand)null!).ExecuteCommandAsync("SELECT 1", closeConnectionOnExit: true));
    }

    [Fact]
    public void ExecuteCommand_CloseConnectionOnExit_ClosesTheConnection_WithoutDisposingIt()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        var disposed = false;
        connection.Disposed += (_, _) => disposed = true;
        connection.Open();

        connection.ExecuteCommand("CREATE TABLE t (a INTEGER)", closeConnectionOnExit: true);

        Assert.Equal(System.Data.ConnectionState.Closed, connection.State);
        Assert.False(disposed);
    }

    #endregion
}

/// <summary>
/// Integration tests using a real ODBC connection to a Microsoft Access database.
/// Validates end-to-end positional parameter support.
/// Tests are silently skipped if sample.accdb or the ODBC driver is not available.
/// </summary>
public class OdbcAccessIntegrationTests : IDisposable
{
    private readonly DbConnection? _connection;
    private readonly bool _canRun;

    public OdbcAccessIntegrationTests()
    {
        // Next to the test assembly: on .NET Framework, with app domains off (Tests.Net481's
        // xunit.runner.json), AppContext.BaseDirectory is the test host's folder instead.
        var dbPath = Path.Combine(Path.GetDirectoryName(typeof(OdbcAccessIntegrationTests).Assembly.Location)!, "sample.accdb");
        if (!File.Exists(dbPath)) { _canRun = false; return; }

        try
        {
            var connStr = $"Driver={{Microsoft Access Driver (*.mdb, *.accdb)}};Dbq={dbPath};";
            _connection = new OdbcConnection(connStr);
            _connection.Open();
            _canRun = true;
        }
        catch
        {
            _canRun = false;
        }
    }

    public void Dispose()
    {
        _connection?.Dispose();
    }

    [Fact]
    public void Odbc_SelectAll_ReturnsAllRows()
    {
        if (!_canRun) return;

        using var result = _connection!.ExecuteQuery("SELECT * FROM TestTable ORDER BY Id");
        var rows = result.ToList();

        Assert.Equal(3, rows.Count);
        Assert.Equal("Alice", (string)rows[0].Name);
        Assert.Equal("Bob", (string)rows[1].Name);
        Assert.Equal("Charlie", (string)rows[2].Name);
    }

    [Fact]
    public void Odbc_ParameterizedQuery_SingleParam()
    {
        if (!_canRun) return;

        using var result = _connection!.ExecuteQuery(
            "SELECT * FROM TestTable WHERE Name = {{Name}}",
            new { Name = "Bob" });
        var rows = result.ToList();

        Assert.Single(rows);
        Assert.Equal("Bob", (string)rows[0].Name);
    }

    [Fact]
    public void Odbc_ParameterizedQuery_MultipleParams()
    {
        if (!_canRun) return;

        using var result = _connection!.ExecuteQuery(
            "SELECT * FROM TestTable WHERE Id >= {{MinId}} AND Id <= {{MaxId}} ORDER BY Id",
            new { MinId = 1, MaxId = 2 });
        var rows = result.ToList();

        Assert.Equal(2, rows.Count);
        Assert.Equal("Alice", (string)rows[0].Name);
        Assert.Equal("Bob", (string)rows[1].Name);
    }

    [Fact]
    public void Odbc_ParameterizedQuery_RepeatedParam()
    {
        if (!_canRun) return;

        using var result = _connection!.ExecuteQuery(
            "SELECT * FROM TestTable WHERE Name = {{Name}} OR Name = {{Name}}",
            new { Name = "Alice" });
        var rows = result.ToList();

        Assert.Single(rows);
        Assert.Equal("Alice", (string)rows[0].Name);
    }

    [Fact]
    public void Odbc_PlaceholderMatchedBySeveralPatterns_IsBoundOnce()
    {
        if (!_canRun) return;

        // Both patterns match {{Name}}, as a JSON body's and a query string's do in an application
        // that reads both.
        var queryParamsList = new List<DbQueryParams>
        {
            new() { DataModel = new { Name = "Bob" }, QueryParamsRegex = @"(?<open_marker>\{\{|\{j\{)(?<param>.*?)?(?<close_marker>\}\})" },
            new() { DataModel = new Dictionary<string, object>(), QueryParamsRegex = @"(?<open_marker>\{\{|\{qs\{)(?<param>.*?)?(?<close_marker>\}\})" }
        };

        using var result = _connection!.ExecuteQuery(
            "SELECT * FROM TestTable WHERE Name = {{Name}} AND Id > 0", queryParamsList);
        var rows = result.ToList();

        Assert.Single(rows);
        Assert.Equal("Bob", (string)rows[0].Name);
    }

    [Fact]
    public async Task Odbc_AsyncQuery_ReturnsResults()
    {
        if (!_canRun) return;

        await using var result = await _connection!.ExecuteQueryAsync(
            "SELECT * FROM TestTable WHERE Value > {{MinValue}} ORDER BY Id",
            new { MinValue = 15.0 });
        var rows = new List<dynamic>();
        await foreach (var row in result) rows.Add(row);

        Assert.Equal(2, rows.Count);
        Assert.Equal("Bob", (string)rows[0].Name);
        Assert.Equal("Charlie", (string)rows[1].Name);
    }

    [Fact]
    public void Odbc_TypedQuery_ReturnsTypedResults()
    {
        if (!_canRun) return;

        using var result = _connection!.ExecuteQuery<TestRow>(
            "SELECT * FROM TestTable ORDER BY Id");
        var rows = result.ToList();

        Assert.Equal(3, rows.Count);
        Assert.Equal(1, rows[0].Id);
        Assert.Equal("Alice", rows[0].Name);
    }

    [Fact]
    public void Odbc_PrefixDetection_ReturnsQuestionMark()
    {
        if (!_canRun) return;

        var prefix = AdoNetExt.GetParameterPrefix(_connection!);
        Assert.Equal("?", prefix);
    }

    public class TestRow
    {
        public int Id { get; set; }
        public string? Name { get; set; }
        public double Value { get; set; }
    }
}