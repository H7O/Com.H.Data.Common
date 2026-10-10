using Com.H.Data.Common;

namespace Tests;

/// <summary>
/// Tests for the Fill extension method in DataExtensions.
/// Covers basic functionality, overlapping regex patterns, null handling,
/// custom value converters, and edge cases.
/// </summary>
public class FillExtensionTests
{
    // Regex patterns for testing
    // Pattern 1: Matches {{var}} and {j{var}} (JSON marker)
    private const string JsonOrGenericRegex = @"(?<open_marker>\{\{|\{j\{)(?<param>.*?)?(?<close_marker>\}\})";
    
    // Pattern 2: Matches {{var}} and {qs{var}} (QueryString marker)  
    private const string QueryStringOrGenericRegex = @"(?<open_marker>\{\{|\{qs\{)(?<param>.*?)?(?<close_marker>\}\})";
    
    // Pattern 3: Matches only {auth{var}} (Auth marker - specific only)
    private const string AuthOnlyRegex = @"(?<open_marker>\{auth\{)(?<param>.*?)?(?<close_marker>\}\})";
    
    // Default pattern: Matches only {{var}}
    private const string DefaultRegex = @"(?<open_marker>\{\{)(?<param>.*?)?(?<close_marker>\}\})";

    #region Basic Functionality Tests

    [Fact]
    public void Fill_BasicReplacement_ReplacesPlaceholder()
    {
        // Arrange
        var template = "Hello {{name}}!";
        var queryParams = new List<DbQueryParams>
        {
            new() 
            { 
                DataModel = new Dictionary<string, object> { { "name", "World" } },
                QueryParamsRegex = DefaultRegex
            }
        };

        // Act
        var result = template.Fill(queryParams);

        // Assert
        Assert.Equal("Hello World!", result);
    }

    [Fact]
    public void Fill_MultipleVariables_ReplacesAll()
    {
        // Arrange
        var template = "{{greeting}} {{name}}, you are {{age}} years old.";
        var queryParams = new List<DbQueryParams>
        {
            new() 
            { 
                DataModel = new Dictionary<string, object> 
                { 
                    { "greeting", "Hello" },
                    { "name", "John" },
                    { "age", 30 }
                },
                QueryParamsRegex = DefaultRegex
            }
        };

        // Act
        var result = template.Fill(queryParams);

        // Assert
        Assert.Equal("Hello John, you are 30 years old.", result);
    }

    [Fact]
    public void Fill_MultipleOccurrencesOfSameVariable_ReplacesAll()
    {
        // Arrange
        var template = "{{name}} said: 'My name is {{name}}'";
        var queryParams = new List<DbQueryParams>
        {
            new() 
            { 
                DataModel = new Dictionary<string, object> { { "name", "Bob" } },
                QueryParamsRegex = DefaultRegex
            }
        };

        // Act
        var result = template.Fill(queryParams);

        // Assert
        Assert.Equal("Bob said: 'My name is Bob'", result);
    }

    #endregion

    #region Null Handling Tests

    [Fact]
    public void Fill_NullValue_ReplacesWithEmptyStringByDefault()
    {
        // Arrange
        var template = "Value: {{value}}";
        var queryParams = new List<DbQueryParams>
        {
            new() 
            { 
                DataModel = new Dictionary<string, object> { { "value", null! } },
                QueryParamsRegex = DefaultRegex
            }
        };

        // Act
        var result = template.Fill(queryParams);

        // Assert
        Assert.Equal("Value: ", result);
    }

    [Fact]
    public void Fill_NullValue_ReplacesWithCustomNullString()
    {
        // Arrange
        var template = "Value: {{value}}";
        var queryParams = new List<DbQueryParams>
        {
            new() 
            { 
                DataModel = new Dictionary<string, object> { { "value", null! } },
                QueryParamsRegex = DefaultRegex
            }
        };

        // Act
        var result = template.Fill(queryParams, nullReplacementString: "N/A");

        // Assert
        Assert.Equal("Value: N/A", result);
    }

    [Fact]
    public void Fill_MissingVariable_ReplacesWithNullString()
    {
        // Arrange - template has variable not in data model
        var template = "Hello {{name}}, your id is {{id}}";
        var queryParams = new List<DbQueryParams>
        {
            new() 
            { 
                DataModel = new Dictionary<string, object> { { "name", "John" } }, // id is missing
                QueryParamsRegex = DefaultRegex
            }
        };

        // Act
        var result = template.Fill(queryParams, nullReplacementString: "[MISSING]");

        // Assert
        Assert.Equal("Hello John, your id is [MISSING]", result);
    }

    #endregion

    #region Edge Cases - Input Validation

    [Fact]
    public void Fill_NullInput_ReturnsEmptyString()
    {
        // Arrange
        string? template = null;
        var queryParams = new List<DbQueryParams>
        {
            new() { DataModel = new Dictionary<string, object> { { "name", "John" } } }
        };

        // Act
        var result = template!.Fill(queryParams);

        // Assert
        Assert.Equal("", result);
    }

    [Fact]
    public void Fill_EmptyInput_ReturnsEmptyString()
    {
        // Arrange
        var template = "";
        var queryParams = new List<DbQueryParams>
        {
            new() { DataModel = new Dictionary<string, object> { { "name", "John" } } }
        };

        // Act
        var result = template.Fill(queryParams);

        // Assert
        Assert.Equal("", result);
    }

    [Fact]
    public void Fill_NullQueryParamsList_ReturnsOriginalString()
    {
        // Arrange
        var template = "Hello {{name}}!";

        // Act
        var result = template.Fill(null);

        // Assert
        Assert.Equal("Hello {{name}}!", result);
    }

    [Fact]
    public void Fill_EmptyQueryParamsList_ReturnsOriginalString()
    {
        // Arrange
        var template = "Hello {{name}}!";
        var queryParams = new List<DbQueryParams>();

        // Act
        var result = template.Fill(queryParams);

        // Assert
        Assert.Equal("Hello {{name}}!", result);
    }

    #endregion

    #region Custom Value Converter Tests

    [Fact]
    public void Fill_WithValueConverter_UsesCustomConversion()
    {
        // Arrange
        var template = "Date: {{date}}";
        var testDate = new DateTime(2024, 1, 15);
        var queryParams = new List<DbQueryParams>
        {
            new() 
            { 
                DataModel = new Dictionary<string, object> { { "date", testDate } },
                QueryParamsRegex = DefaultRegex
            }
        };

        // Act
        var result = template.Fill(queryParams, valueConverter: (name, value) => 
            value is DateTime dt ? dt.ToString("yyyy-MM-dd") : value?.ToString() ?? "");

        // Assert
        Assert.Equal("Date: 2024-01-15", result);
    }

    [Fact]
    public void Fill_ValueConverterReceivesParameterName_CanFormatBasedOnName()
    {
        // Arrange
        var template = "Price: {{price}}, Quantity: {{quantity}}";
        var queryParams = new List<DbQueryParams>
        {
            new() 
            { 
                DataModel = new Dictionary<string, object> 
                { 
                    { "price", 19.99 },
                    { "quantity", 5 }
                },
                QueryParamsRegex = DefaultRegex
            }
        };

        // Act - format price with currency, quantity as-is
        var result = template.Fill(queryParams, valueConverter: (name, value) => 
            name == "price" ? $"${value:F2}" : value?.ToString() ?? "");

        // Assert
        Assert.Equal("Price: $19.99, Quantity: 5", result);
    }

    #endregion

    #region Overlapping Regex Pattern Tests

    [Fact]
    public void Fill_OverlappingRegex_GenericPattern_LastValueWins()
    {
        // Arrange - Both regex patterns can match {{name}}
        // The LAST item in the list should win
        var template = "Hello {{name}}!";
        var queryParams = new List<DbQueryParams>
        {
            new() 
            { 
                DataModel = new Dictionary<string, object> { { "name", "First" } },
                QueryParamsRegex = JsonOrGenericRegex // matches {{name}}
            },
            new() 
            { 
                DataModel = new Dictionary<string, object> { { "name", "Second" } },
                QueryParamsRegex = QueryStringOrGenericRegex // also matches {{name}}
            }
        };

        // Act
        var result = template.Fill(queryParams);

        // Assert - "Second" should win because it's last in the list
        Assert.Equal("Hello Second!", result);
    }

    [Fact]
    public void Fill_OverlappingRegex_ReversedOrder_FirstValueWins()
    {
        // Arrange - Same as above but reversed order
        var template = "Hello {{name}}!";
        var queryParams = new List<DbQueryParams>
        {
            new() 
            { 
                DataModel = new Dictionary<string, object> { { "name", "Second" } },
                QueryParamsRegex = QueryStringOrGenericRegex // matches {{name}}
            },
            new() 
            { 
                DataModel = new Dictionary<string, object> { { "name", "First" } },
                QueryParamsRegex = JsonOrGenericRegex // also matches {{name}}
            }
        };

        // Act
        var result = template.Fill(queryParams);

        // Assert - "First" should win because it's last in the list
        Assert.Equal("Hello First!", result);
    }

    #endregion

    #region Specific Pattern Tests (Non-overlapping)

    [Fact]
    public void Fill_SpecificJsonPattern_OnlyMatchesJsonMarker()
    {
        // Arrange - {j{name}} should only be matched by JsonOrGenericRegex
        var template = "JSON: {j{jsonValue}}, QS: {qs{qsValue}}";
        var queryParams = new List<DbQueryParams>
        {
            new() 
            { 
                DataModel = new Dictionary<string, object> { { "jsonValue", "JSON_DATA" } },
                QueryParamsRegex = JsonOrGenericRegex
            },
            new() 
            { 
                DataModel = new Dictionary<string, object> { { "qsValue", "QS_DATA" } },
                QueryParamsRegex = QueryStringOrGenericRegex
            }
        };

        // Act
        var result = template.Fill(queryParams);

        // Assert - Each pattern matches its own specific marker
        Assert.Equal("JSON: JSON_DATA, QS: QS_DATA", result);
    }

    [Fact]
    public void Fill_AuthPattern_OnlyMatchesAuthMarker()
    {
        // Arrange - {auth{token}} should only be matched by AuthOnlyRegex
        var template = "Auth: {auth{token}}, Generic: {{name}}";
        var queryParams = new List<DbQueryParams>
        {
            new() 
            { 
                DataModel = new Dictionary<string, object> { { "token", "SECRET_TOKEN" } },
                QueryParamsRegex = AuthOnlyRegex
            },
            new() 
            { 
                DataModel = new Dictionary<string, object> { { "name", "User" } },
                QueryParamsRegex = DefaultRegex
            }
        };

        // Act
        var result = template.Fill(queryParams);

        // Assert
        Assert.Equal("Auth: SECRET_TOKEN, Generic: User", result);
    }

    #endregion

    #region Mixed Pattern Tests

    [Fact]
    public void Fill_MixedPatterns_GenericAndSpecific_AllReplaced()
    {
        // Arrange - Template with generic {{}} and specific {j{}} and {qs{}} patterns
        var template = "Generic: {{generic}}, JSON: {j{json}}, QS: {qs{qs}}";
        var queryParams = new List<DbQueryParams>
        {
            new() 
            { 
                DataModel = new Dictionary<string, object> 
                { 
                    { "generic", "GENERIC_VALUE" },
                    { "json", "JSON_VALUE" }
                },
                QueryParamsRegex = JsonOrGenericRegex // matches both {{}} and {j{}}
            },
            new() 
            { 
                DataModel = new Dictionary<string, object> 
                { 
                    { "qs", "QS_VALUE" }
                },
                QueryParamsRegex = QueryStringOrGenericRegex // matches both {{}} and {qs{}}
            }
        };

        // Act
        var result = template.Fill(queryParams);

        // Assert
        Assert.Equal("Generic: GENERIC_VALUE, JSON: JSON_VALUE, QS: QS_VALUE", result);
    }

    [Fact]
    public void Fill_AllThreePatterns_AuthJsonQueryString()
    {
        // Arrange - Template with all three specific patterns
        var template = "Auth: {auth{token}}, JSON: {j{data}}, QS: {qs{query}}, Generic: {{name}}";
        var queryParams = new List<DbQueryParams>
        {
            new() 
            { 
                DataModel = new Dictionary<string, object> { { "token", "AUTH_TOKEN" } },
                QueryParamsRegex = AuthOnlyRegex
            },
            new() 
            { 
                DataModel = new Dictionary<string, object> { { "data", "JSON_DATA" } },
                QueryParamsRegex = JsonOrGenericRegex
            },
            new() 
            { 
                DataModel = new Dictionary<string, object> { { "query", "QUERY_STRING" } },
                QueryParamsRegex = QueryStringOrGenericRegex
            },
            new() 
            { 
                DataModel = new Dictionary<string, object> { { "name", "TestUser" } },
                QueryParamsRegex = DefaultRegex
            }
        };

        // Act
        var result = template.Fill(queryParams);

        // Assert
        Assert.Equal("Auth: AUTH_TOKEN, JSON: JSON_DATA, QS: QUERY_STRING, Generic: TestUser", result);
    }

    #endregion

    #region Same Variable Different Values - Order Priority Tests

    [Fact]
    public void Fill_SameVariableDifferentPatterns_SpecificPatternsWin()
    {
        // Arrange - Same variable name "value" with different specific patterns
        // The specific pattern {j{value}} should get JSON data
        // The specific pattern {qs{value}} should get QS data
        var template = "JSON value: {j{value}}, QS value: {qs{value}}";
        var queryParams = new List<DbQueryParams>
        {
            new() 
            { 
                DataModel = new Dictionary<string, object> { { "value", "JSON_SPECIFIC" } },
                QueryParamsRegex = JsonOrGenericRegex
            },
            new() 
            { 
                DataModel = new Dictionary<string, object> { { "value", "QS_SPECIFIC" } },
                QueryParamsRegex = QueryStringOrGenericRegex
            }
        };

        // Act
        var result = template.Fill(queryParams);

        // Assert - Each specific pattern gets its own value
        Assert.Equal("JSON value: JSON_SPECIFIC, QS value: QS_SPECIFIC", result);
    }

    [Fact]
    public void Fill_GenericPatternWithMultipleSources_LastSourceWins()
    {
        // Arrange - Generic {{value}} can be matched by both patterns
        // Since both patterns can match {{value}}, the last one should win
        var template = "Value: {{value}}";
        var queryParams = new List<DbQueryParams>
        {
            new() 
            { 
                DataModel = new Dictionary<string, object> { { "value", "FROM_JSON_PATTERN" } },
                QueryParamsRegex = JsonOrGenericRegex
            },
            new() 
            { 
                DataModel = new Dictionary<string, object> { { "value", "FROM_QS_PATTERN" } },
                QueryParamsRegex = QueryStringOrGenericRegex
            }
        };

        // Act
        var result = template.Fill(queryParams);

        // Assert - Last source wins
        Assert.Equal("Value: FROM_QS_PATTERN", result);
    }

    #endregion

    #region Complex Real-World Scenario Tests

    [Fact]
    public void Fill_RealWorldScenario_ApiRequestTemplate()
    {
        // Arrange - Simulating an API request template with different data sources
        var template = """
            Authorization: Bearer {auth{token}}
            Content-Type: application/json
            
            {
                "user": "{{username}}",
                "data": {j{payload}},
                "filters": "{qs{filters}}"
            }
            """;

        var queryParams = new List<DbQueryParams>
        {
            new() 
            { 
                DataModel = new Dictionary<string, object> { { "token", "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9" } },
                QueryParamsRegex = AuthOnlyRegex
            },
            new() 
            { 
                DataModel = new Dictionary<string, object> { { "username", "john.doe" } },
                QueryParamsRegex = DefaultRegex
            },
            new() 
            { 
                DataModel = new Dictionary<string, object> { { "payload", "{\"action\":\"update\"}" } },
                QueryParamsRegex = JsonOrGenericRegex
            },
            new() 
            { 
                DataModel = new Dictionary<string, object> { { "filters", "status=active&page=1" } },
                QueryParamsRegex = QueryStringOrGenericRegex
            }
        };

        // Act
        var result = template.Fill(queryParams);

        // Assert
        var expected = """
            Authorization: Bearer eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9
            Content-Type: application/json
            
            {
                "user": "john.doe",
                "data": {"action":"update"},
                "filters": "status=active&page=1"
            }
            """;
        Assert.Equal(expected, result);
    }

    [Fact]
    public void Fill_RealWorldScenario_SqlQueryTemplate()
    {
        // Arrange - Simulating SQL query building (though normally you'd use parameterized queries)
        var template = "SELECT * FROM {{table}} WHERE name = '{{name}}' AND status = {{status}}";
        var queryParams = new List<DbQueryParams>
        {
            new() 
            { 
                DataModel = new Dictionary<string, object> 
                { 
                    { "table", "users" },
                    { "name", "John" },
                    { "status", 1 }
                },
                QueryParamsRegex = DefaultRegex
            }
        };

        // Act
        var result = template.Fill(queryParams);

        // Assert
        Assert.Equal("SELECT * FROM users WHERE name = 'John' AND status = 1", result);
    }

    #endregion

    #region Case Sensitivity Tests

    [Fact]
    public void Fill_CaseInsensitive_ByDefault_MatchesDifferentCases()
    {
        // Arrange - Template has {{NAME}} but data model has "name" (lowercase)
        // By default, ignoreCase is true, so case-insensitive matching is used
        var template = "Hello {{NAME}}!";
        var queryParams = new List<DbQueryParams>
        {
            new() 
            { 
                DataModel = new Dictionary<string, object> { { "name", "World" } },
                QueryParamsRegex = DefaultRegex
            }
        };

        // Act
        var result = template.Fill(queryParams);

        // Assert - "NAME" matches "name" because case-insensitive by default
        Assert.Equal("Hello World!", result);
    }

    [Fact]
    public void Fill_CaseSensitive_WhenEnabled_TreatedAsNull()
    {
        // Arrange - Template has {{NAME}} but data model has "name" (lowercase)
        // With caseSensitive: true, dictionary lookup is case-sensitive
        var template = "Hello {{NAME}}!";
        var queryParams = new List<DbQueryParams>
        {
            new() 
            { 
                DataModel = new Dictionary<string, object> { { "name", "World" } },
                QueryParamsRegex = DefaultRegex
            }
        };

        // Act
        var result = template.Fill(queryParams, caseSensitive: true);

        // Assert - "NAME" not found (case-sensitive), treated as null, replaced with empty string
        Assert.Equal("Hello !", result);
    }

    [Fact]
    public void Fill_CaseSensitive_WithNullReplacement_ShowsMissing()
    {
        // Arrange - Same case mismatch scenario with case-sensitive matching
        var template = "Hello {{NAME}}!";
        var queryParams = new List<DbQueryParams>
        {
            new() 
            { 
                DataModel = new Dictionary<string, object> { { "name", "World" } },
                QueryParamsRegex = DefaultRegex
            }
        };

        // Act
        var result = template.Fill(queryParams, nullReplacementString: "[NOT_FOUND]", caseSensitive: true);

        // Assert - Shows the null replacement, indicating the key wasn't found
        Assert.Equal("Hello [NOT_FOUND]!", result);
    }

    [Fact]
    public void Fill_ExactCaseMatch_ReplacesCorrectly()
    {
        // Arrange - Exact case match
        var template = "Hello {{name}}!";
        var queryParams = new List<DbQueryParams>
        {
            new() 
            { 
                DataModel = new Dictionary<string, object> { { "name", "World" } },
                QueryParamsRegex = DefaultRegex
            }
        };

        // Act
        var result = template.Fill(queryParams);

        // Assert
        Assert.Equal("Hello World!", result);
    }

    [Fact]
    public void Fill_CaseInsensitiveDefault_MatchesDifferentCases()
    {
        // Arrange - Case-insensitive matching is now the default behavior
        // No need for special dictionary setup
        var template = "Hello {{NAME}}, welcome to {{City}}!";
        var queryParams = new List<DbQueryParams>
        {
            new() 
            { 
                DataModel = new Dictionary<string, object> 
                { 
                    { "name", "World" },
                    { "city", "New York" }
                },
                QueryParamsRegex = DefaultRegex
            }
        };

        // Act
        var result = template.Fill(queryParams);

        // Assert - Both {{NAME}} and {{City}} are matched despite different casing
        Assert.Equal("Hello World, welcome to New York!", result);
    }

    [Fact]
    public void Fill_CaseInsensitiveDefault_MixedCaseVariables()
    {
        // Arrange - Template has variables in various cases, dictionary keys are lowercase
        // Case-insensitive matching is the default
        var template = "{{GREETING}} {{Name}}, your ID is {{userId}}.";
        var queryParams = new List<DbQueryParams>
        {
            new() 
            { 
                DataModel = new Dictionary<string, object> 
                { 
                    { "greeting", "Hello" },
                    { "name", "John" },
                    { "userid", "12345" }
                },
                QueryParamsRegex = DefaultRegex
            }
        };

        // Act
        var result = template.Fill(queryParams);

        // Assert
        Assert.Equal("Hello John, your ID is 12345.", result);
    }

    #endregion

    #region Simple API Tests (object queryParams)

    [Fact]
    public void Fill_SimpleApi_AnonymousObject_ReplacesPlaceholder()
    {
        // Arrange - Using the simpler API with anonymous object
        var template = "Hello {{name}}!";

        // Act
        var result = template.Fill(new { name = "World" });

        // Assert
        Assert.Equal("Hello World!", result);
    }

    [Fact]
    public void Fill_SimpleApi_AnonymousObject_MultipleVariables()
    {
        // Arrange
        var template = "{{greeting}} {{name}}, you are {{age}} years old.";

        // Act
        var result = template.Fill(new { greeting = "Hello", name = "John", age = 30 });

        // Assert
        Assert.Equal("Hello John, you are 30 years old.", result);
    }

    [Fact]
    public void Fill_SimpleApi_Dictionary_ReplacesPlaceholder()
    {
        // Arrange - Using Dictionary<string, object>
        var template = "Hello {{name}}, your score is {{score}}.";
        var data = new Dictionary<string, object>
        {
            { "name", "Alice" },
            { "score", 95.5 }
        };

        // Act
        var result = template.Fill(data);

        // Assert
        Assert.Equal("Hello Alice, your score is 95.5.", result);
    }

    [Fact]
    public void Fill_SimpleApi_CaseInsensitive_ByDefault()
    {
        // Arrange - Template has {{NAME}} but data has "name"
        var template = "Hello {{NAME}}!";

        // Act
        var result = template.Fill(new { name = "World" });

        // Assert - Case-insensitive by default
        Assert.Equal("Hello World!", result);
    }

    [Fact]
    public void Fill_SimpleApi_CaseSensitive_WhenEnabled()
    {
        // Arrange
        var template = "Hello {{NAME}}!";

        // Act
        var result = template.Fill(new { name = "World" }, caseSensitive: true);

        // Assert - NAME not found, replaced with empty string
        Assert.Equal("Hello !", result);
    }

    [Fact]
    public void Fill_SimpleApi_WithNullReplacement()
    {
        // Arrange
        var template = "Value: {{value}}";

        // Act
        var result = template.Fill(new { value = (string?)null }, nullReplacementString: "N/A");

        // Assert
        Assert.Equal("Value: N/A", result);
    }

    [Fact]
    public void Fill_SimpleApi_WithValueConverter()
    {
        // Arrange
        var template = "Date: {{date}}";
        var testDate = new DateTime(2024, 1, 15);

        // Act
        var result = template.Fill(
            new { date = testDate },
            valueConverter: (name, value) => 
                value is DateTime dt ? dt.ToString("yyyy-MM-dd") : value?.ToString() ?? "");

        // Assert
        Assert.Equal("Date: 2024-01-15", result);
    }

    [Fact]
    public void Fill_SimpleApi_WithCustomRegex()
    {
        // Arrange - Using [[ ]] delimiters instead of {{ }}
        var template = "Hello [[name]]!";
        var customRegex = @"(?<open_marker>\[\[)(?<param>.*?)?(?<close_marker>\]\])";

        // Act
        var result = template.Fill(
            new { name = "World" },
            queryParamsRegex: customRegex);

        // Assert
        Assert.Equal("Hello World!", result);
    }

    [Fact]
    public void Fill_SimpleApi_StillSupportsDbQueryParamsList()
    {
        // Arrange - The simple API should still work with IEnumerable<DbQueryParams>
        var template = "Hello {{name}}!";
        var queryParamsList = new List<DbQueryParams>
        {
            new() { DataModel = new { name = "World" } }
        };

        // Act
        var result = template.Fill(queryParamsList);

        // Assert
        Assert.Equal("Hello World!", result);
    }

    [Fact]
    public void Fill_SimpleApi_HtmlTemplate()
    {
        // Arrange - Real-world HTML templating scenario
        var htmlTemplate = "<h1>Hello {{name}}</h1><p>You have {{count}} messages.</p>";

        // Act
        var result = htmlTemplate.Fill(new { name = "John", count = 5 });

        // Assert
        Assert.Equal("<h1>Hello John</h1><p>You have 5 messages.</p>", result);
    }

    [Fact]
    public void Fill_SimpleApi_EmailTemplate()
    {
        // Arrange - Email template scenario
        var emailTemplate = """
            Dear {{customerName}},

            Your order {{orderId}} has been shipped.
            Estimated delivery: {{deliveryDate}}

            Thank you for your business!
            """;

        // Act
        var result = emailTemplate.Fill(new 
        { 
            customerName = "Jane Doe",
            orderId = "ORD-12345",
            deliveryDate = "January 20, 2024"
        });

        // Assert
        var expected = """
            Dear Jane Doe,

            Your order ORD-12345 has been shipped.
            Estimated delivery: January 20, 2024

            Thank you for your business!
            """;
        Assert.Equal(expected, result);
    }

    #endregion

    #region Placeholder Resolution Tests (the SQL path's rules)

    // Matches only {s{var}} (a settings-style marker)
    private const string SettingsOnlyRegex = @"(?<open_marker>\{s\{)(?<param>.*?)?(?<close_marker>\}\})";

    // Matches only {pq{var}} (a previous-query-style marker)
    private const string PreviousQueryOnlyRegex = @"(?<open_marker>\{pq\{)(?<param>.*?)?(?<close_marker>\}\})";

    private static List<DbQueryParams> Sets(params (string Regex, object Model)[] sets) =>
        sets.Select(s => new DbQueryParams { DataModel = s.Model, QueryParamsRegex = s.Regex }).ToList();

    [Theory]
    [InlineData("{{id}} / {j{id}}", "BODY / BODY")]
    [InlineData("{j{id}} / {{id}}", "BODY / BODY")]
    [InlineData("{j{id}} {j{id}} {{id}} {{ID}}", "BODY BODY BODY BODY")]
    public void Fill_OneNameUnderTwoMarkersOfOnePattern_FillsEveryMarker(string template, string expected)
    {
        var result = template.Fill(Sets((JsonOrGenericRegex, new { id = "BODY" })));

        Assert.Equal(expected, result);
    }

    [Fact]
    public void Fill_OneNameUnderTwoMarkersOfOnePattern_NoValue_EveryMarkerGetsTheNullReplacement()
    {
        var result = "{{id}} / {j{id}}".Fill(Sets((JsonOrGenericRegex, new { other = 1 })), nullReplacementString: "NULL");

        Assert.Equal("NULL / NULL", result);
    }

    [Theory]
    [InlineData("{{id}} / {j{id}} / {qs{id}}", "QS / BODY / QS")]
    [InlineData("{j{id}} / {{id}} / {qs{id}}", "BODY / QS / QS")]
    public void Fill_SeveralSets_TheLatestSetWins_WhateverOrderTheMarkersAreWrittenIn(string template, string expected)
    {
        var sets = Sets((JsonOrGenericRegex, new { id = "BODY" }), (QueryStringOrGenericRegex, new { id = "QS" }));

        Assert.Equal(expected, template.Fill(sets));
    }

    [Fact]
    public void Fill_NullInTheLatestSet_EveryMarkerTheEarlierSetMatchesTakesItsValue()
    {
        var sets = Sets(
            (JsonOrGenericRegex, new { id = "BODY" }),
            (QueryStringOrGenericRegex, new Dictionary<string, object?> { ["id"] = null }));

        var result = "{{id}} / {qs{id}} / {j{id}}".Fill(sets, nullReplacementString: "NULL");

        Assert.Equal("BODY / NULL / BODY", result);
    }

    [Theory]
    [InlineData("{{a}} {{b}}", "{{b}} X")]
    [InlineData("{{b}} {{a}}", "X {{b}}")]
    public void Fill_AValueIsNotReadAgainByItsOwnSet(string template, string expected)
    {
        var result = template.Fill(new { a = "{{b}}", b = "X" });

        Assert.Equal(expected, result);
    }

    [Fact]
    public void Fill_AValueIsNotReadAgainByAnotherSet()
    {
        // The previous-query set is the latest in the list, so its value goes in first. The settings
        // set must not then fill the marker text that value carries.
        var sets = Sets(
            (SettingsOnlyRegex, new { key = "SECRET" }),
            (PreviousQueryOnlyRegex, new { doc = """{"note": "{s{key}}"}""" }));

        var result = """{"body": {pq{doc}}, "auth": "{s{key}}"}""".Fill(sets);

        Assert.Equal("""{"body": {"note": "{s{key}}"}, "auth": "SECRET"}""", result);
    }

    [Fact]
    public void Fill_TheNullReplacementLeavesAValueAlone()
    {
        var result = "{{a}} {{missing}}".Fill(new { a = "price {{missing}} EUR" }, nullReplacementString: "NULL");

        Assert.Equal("price {{missing}} EUR NULL", result);
    }

    [Fact]
    public void Fill_AValueWithAnOpenMarker_DoesNotJoinALaterPlaceholder()
    {
        // The auth set is the latest, so its value goes in first; it must not pair with the {{name}} after it.
        var sets = Sets((DefaultRegex, new { name = "B" }), (AuthOnlyRegex, new { t = "{{" }));

        var result = "{auth{t}} - {{name}}".Fill(sets);

        Assert.Equal("{{ - B", result);
    }

    [Fact]
    public void Fill_CaseSensitive_ASpellingWithoutAValue_IsNotFilledFromAnotherSpelling()
    {
        Assert.Equal("x ", "{{name}} {{NAME}}".Fill(new { name = "x" }, caseSensitive: true));
        Assert.Equal(" x", "{{NAME}} {{name}}".Fill(new { name = "x" }, caseSensitive: true));
        Assert.Equal("x x", "{{name}} {{NAME}}".Fill(new { name = "x" }));
    }

    [Fact]
    public void Fill_CaseSensitive_TwoSpellingsWithTwoValues_EachKeepsItsOwn()
    {
        var model = new Dictionary<string, object> { ["name"] = "x", ["NAME"] = "y" };

        Assert.Equal("x y", "{{name}} {{NAME}}".Fill(model, caseSensitive: true));
    }

    [Fact]
    public void Fill_CaseSensitive_OneMarkerPattern_ASpellingWithoutAValue_IsNotFilledFromAnotherSpelling()
    {
        const string colonRegex = @"(?<open_marker>:)(?<param>\w+)(?<close_marker>)";

        var result = ":date :DATE".Fill(new { date = "d" }, nullReplacementString: "N", caseSensitive: true, queryParamsRegex: colonRegex);

        Assert.Equal("d N", result);
    }

    [Theory]
    [InlineData(":date, :date_from", "D, F")]
    [InlineData(":date_from, :date", "F, D")]
    public void Fill_OneMarkerPattern_ALongerNameKeepsItsOwnValue(string template, string expected)
    {
        const string colonRegex = @"(?<open_marker>:)(?<param>\w+)(?<close_marker>)";

        var result = template.Fill(new { date = "D", date_from = "F" }, queryParamsRegex: colonRegex);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void Fill_OneMarkerPattern_ANullLongerName_DoesNotTakeTheShorterNamesPlace()
    {
        const string colonRegex = @"(?<open_marker>:)(?<param>\w+)(?<close_marker>)";

        var result = ":date, :date_from".Fill(new { date = "D" }, nullReplacementString: "NULL", queryParamsRegex: colonRegex);

        Assert.Equal("D, NULL", result);
    }

    [Fact]
    public void Fill_PatternWithoutMarkerGroups_ReplacesOnlyItsMatches()
    {
        const string bracketRegex = @"\[(?<param>\w+)\]";

        var result = "[id] and id".Fill(new { id = "7" }, queryParamsRegex: bracketRegex);

        Assert.Equal("7 and id", result);
    }

    [Fact]
    public void Fill_ANullSetInTheList_IsSkipped()
    {
        var sets = new List<DbQueryParams> { null!, new() { DataModel = new { name = "W" }, QueryParamsRegex = DefaultRegex } };

        Assert.Equal("Hello W", "Hello {{name}}".Fill(sets));
    }

    [Fact]
    public void Fill_ReadmeExample_GivesTheDocumentedText()
    {
        var body = System.Text.Json.JsonDocument.Parse("""
            { "first name": "Ann", "e-mail": "ann@example.com", "age": 31 }
            """).RootElement;
        var queryString = new Dictionary<string, object> { ["city"] = "Lisbon" };
        var sources = Sets((JsonOrGenericRegex, body), (QueryStringOrGenericRegex, queryString));

        var message = "Hi {{first name}}, you are {{age}}. City: {qs{city}}".Fill(sources);

        Assert.Equal("Hi Ann, you are 31. City: Lisbon", message);
    }

    [Fact]
    public void Fill_APatternThatCouldReadAPlaceholdersSlot_FillsOnlyTheText()
    {
        const string digits = @"(?<param>\b\d+\b)";

        Assert.Equal("A ", "1 0".Fill(new Dictionary<string, object> { ["1"] = "A" }, queryParamsRegex: digits));
        Assert.Equal("X seven", "{{x}} 7".Fill(Sets((digits, new Dictionary<string, object> { ["7"] = "seven" }), (DefaultRegex, new { x = "X" }))));
        Assert.Equal("X hi", "{{x}} hello".Fill(Sets((@"(?<param>\w+)", new { hello = "hi" }), (DefaultRegex, new { x = "X" }))));
        Assert.Equal("Hi Ann, 50% off shoes", "Hi {{name}}, 50% off %item%".Fill(Sets(
            (@"(?<open_marker>[^\w\s])(?<param>\w+)(?<close_marker>[^\w\s])", new { item = "shoes" }),
            (DefaultRegex, new { name = "Ann" }))));
    }

    [Fact]
    public void Fill_AStrayOpenMarker_KeepsTheTextAndThePlaceholdersAfterIt()
    {
        var sets = Sets(
            (@"(?<open_marker>\{\{|\{h\{)(?<param>.*?)?(?<close_marker>\}\})", new { other = 1 }),
            (JsonOrGenericRegex, new { name = "Ann" }),
            (QueryStringOrGenericRegex, new { q = "qv" }));

        var result = """{"a": "{{name}}", "help": "type {{ then {{name}}", "b": "{{missing}}", "c": "{qs{q}}"}""".Fill(sets);

        Assert.Equal("""{"a": "Ann", "help": "type {{ then Ann", "b": "", "c": "qv"}""", result);
    }

    [Theory]
    [InlineData("Ann", "Dear Ann, type {{ to insert a field. Regards, Ann. Code: N/A")]
    [InlineData(null, "Dear N/A, type {{ to insert a field. Regards, N/A. Code: N/A")]
    public void Fill_AStrayOpenMarker_DoesNotHideANullPlaceholder(string? name, string expected)
    {
        var model = new Dictionary<string, object?> { ["name"] = name };

        var result = "Dear {{name}}, type {{ to insert a field. Regards, {{name}}. Code: {{code}}".Fill(model, nullReplacementString: "N/A");

        Assert.Equal(expected, result);
    }

    [Fact]
    public void Fill_ANullPlaceholder_IsReplacedWhereverItsTextAppears_AsOneWithAValueIs()
    {
        const string notAfterX = @"(?<![x])(?<open_marker>\{\{)(?<param>.*?)?(?<close_marker>\}\})";

        Assert.Equal("a N xN", "a {{code}} x{{code}}".Fill(new { other = 1 }, nullReplacementString: "N", queryParamsRegex: notAfterX));
        Assert.Equal("a C xC", "a {{code}} x{{code}}".Fill(new { code = "C" }, queryParamsRegex: notAfterX));

        // One marker: the pattern skips "-:x", but its text is replaced as long as no name character goes on after it.
        const string colonAfterSpace = @"(?<=[\s(,=])(?<open_marker>:)(?<param>\w+)(?<close_marker>)";
        Assert.Equal("a N, b-N, c-:xy", "a :x, b-:x, c-:xy".Fill(new { other = 1 }, nullReplacementString: "N", queryParamsRegex: colonAfterSpace));
        Assert.Equal("a X, b-X, c-:xy", "a :x, b-:x, c-:xy".Fill(new { x = "X" }, nullReplacementString: "N", queryParamsRegex: colonAfterSpace));
    }

    [Fact]
    public void Fill_TextWithEveryPrivateUseCharacter_Throws_AndWithOneMissing_Fills()
    {
        var all = new string(Enumerable.Range(0xE000, 0x1900).Select(c => (char)c).ToArray());

        Assert.Throws<ArgumentException>(() => ("{{a}}" + all).Fill(new { a = 1 }));
        var oneMissing = all.Remove(100, 1);
        Assert.Equal("1" + oneMissing, ("{{a}}" + oneMissing).Fill(new { a = 1 }));
    }

    [Fact]
    public void Fill_ValueConverter_IsCalledOncePerNameAndSource()
    {
        var calls = new List<string>();
        var sets = Sets((JsonOrGenericRegex, new { id = "BODY" }), (QueryStringOrGenericRegex, new { id = "QS" }));

        var result = "{qs{id}} {j{id}} {{id}} {{ID}}".Fill(sets, valueConverter: (name, value) => { calls.Add($"{name}={value}"); return $"<{value}>"; });

        Assert.Equal("<QS> <BODY> <QS> <QS>", result);
        Assert.Equal(new[] { "id=QS", "id=BODY" }, calls);
    }

    [Theory]
    [MemberData(nameof(AdoNetExtTests.SourcePlaceRows), MemberType = typeof(AdoNetExtTests))]
    public void Fill_ASetWithAnEarlierSetsPattern_DoesNotMoveThatSetUp(string extraId, string a, string b, string c)
    {
        var result = "{{id}} {j{id}} {qs{id}}".Fill(AdoNetExtTests.SourcePlaceSets(extraId), nullReplacementString: "NULL");

        Assert.Equal($"{a} {b} {c}", result);
    }

    [Fact]
    public void Fill_SetsWithOnePattern_TheLaterValueFillsEveryMarker()
    {
        var sets = Sets((JsonOrGenericRegex, new { x = "old" }), (JsonOrGenericRegex, new { x = "new" }));

        Assert.Equal("new|new", "{{x}}|{j{x}}".Fill(sets, nullReplacementString: "NULL"));
    }

    [Fact]
    public void Fill_APatternWithSpacesBetweenMarkerAndName_FillsThePlaceholder()
    {
        const string spaced = @"(?<open_marker>\{\{)\s*(?<param>\w+)\s*(?<close_marker>\}\})";

        Assert.Equal("Hello Ann, id ", "Hello {{ name }}, id {{ id }}".Fill(new { name = "Ann" }, queryParamsRegex: spaced));
        Assert.Equal("Hello Ann, id ", "Hello {{ name }}, id {{ id }}".Fill(Sets((spaced, new { name = "Ann" }), (spaced, new { other = 1 }))));
        Assert.Equal("Hello Ann, id 7", "Hello {{ name }}, id {{ id }}".Fill(Sets(
            (spaced, new { name = "Ann" }),
            (@"(?<open_marker>\{\{|\{qs\{)\s*(?<param>\w+)\s*(?<close_marker>\}\})", new { id = 7 }))));
    }

    [Fact]
    public void Fill_OneMarkerPattern_ANameThatGoesOnIntoAnotherName_KeepsItsOwnValue()
    {
        const string dashed = @"(?<open_marker>:)(?<param>[\w-]+)(?<close_marker>)";
        const string dotted = @"(?<open_marker>:)(?<param>[\w.]+)(?<close_marker>)";

        Assert.Equal("D, N", ":date, :date-from".Fill(new { date = "D" }, nullReplacementString: "N", queryParamsRegex: dashed));
        Assert.Equal("Hi Ann (u42)", "Hi :user.name (:user)".Fill(Sets(
            (dotted, new Dictionary<string, object> { ["user.name"] = "Ann" }),
            (dotted, new { user = "u42" }))));
        Assert.Equal("Hi Ann (u42)", "Hi :user.name (:user)".Fill(Sets(
            (dotted, new Dictionary<string, object> { ["user.name"] = "Ann" }),
            (DefaultRegex, new { other = 1 }),
            (dotted, new { user = "u42" }))));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Fill_AMarkerMadeOfDigits_IsNotReadInsideAnotherPlaceholdersSlot(bool withValue)
    {
        // 120 placeholders fill slots 0 to 119, and slot 101 is written with the digits "101".
        const string digits = @"(?<open_marker>1)(?<param>0)(?<close_marker>1)";
        var names = Enumerable.Range(0, 120).ToDictionary(i => $"n{i}", i => (object)$"v{i}");
        var template = string.Concat(names.Keys.Select(name => "{{" + name + "}}")) + " 101";
        var digitValues = withValue ? new Dictionary<string, object> { ["0"] = "X" } : new Dictionary<string, object>();

        var result = template.Fill(Sets((digits, digitValues), (DefaultRegex, names)), nullReplacementString: "N");

        Assert.Equal(string.Concat(names.Values) + (withValue ? " X" : " N"), result);
    }

    [Fact]
    public void Fill_APatternThatTakesTheCharacterBeforeItsMarker_FillsBesideAnotherPlaceholder()
    {
        const string notEscaped = @"(?:^|[^\\])(?<open_marker>\{\{)(?<param>.*?)(?<close_marker>\}\})";
        const string hostMarker = @"(?<open_marker>\{e\{)(?<param>.*?)?(?<close_marker>\}\})";
        const string colon = @"(?:^|[^:])(?<open_marker>:)(?<param>\w+)(?<close_marker>)";

        Assert.Equal("https://api.example/v1/items?q=1", "https://{e{host}}{{path}}?q=1".Fill(Sets(
            (notEscaped, new { path = "/v1/items" }),
            (hostMarker, new { host = "api.example" }))));
        Assert.Equal("SELECT 12", "SELECT {{a}}:b".Fill(Sets((colon, new { b = 2 }), (DefaultRegex, new { a = 1 }))));
    }

    [Fact]
    public void Fill_APlaceholderAStrayMarkerPairsAcross_IsFilledFromItsOwnSource()
    {
        // The query-string set is read first and replaces {qs{q}}; the stray "{{" then pairs past its slot
        // with the "}}" of {j{name}} (or of {{k}}), which the pattern matched on its own in the template.
        var sets = Sets((JsonOrGenericRegex, new { name = "Ann" }), (QueryStringOrGenericRegex, new { q = "x" }));
        Assert.Equal("q=x; note: {{ is literal; again x; name=Ann",
            "q={qs{q}}; note: {{ is literal; again {qs{q}}; name={j{name}}".Fill(sets));

        const string route = @"(?<open_marker>\{\{|\{r\{)(?<param>.*?)?(?<close_marker>\}\})";
        sets = Sets((JsonOrGenericRegex, new { k = "K" }), (route, new { id = 5 }));
        Assert.Equal("5 {{ 5 5 K", "{{id}} {{ {r{id}} {r{id}} {{k}}".Fill(sets));
    }

    [Fact]
    public void Fill_AStrayOpenMarkerBeforeAPlaceholderUsedTwice_KeepsItsText_WithOrWithoutAValue()
    {
        Assert.Equal("{{ x A y A", "{{ x {{a}} y {{a}}".Fill(new { a = "A" }));
        Assert.Equal("{{ x  y ", "{{ x {{a}} y {{a}}".Fill(new { other = 1 }));
        Assert.Equal("{{ x NULL y NULL", "{{ x {{a}} y {{a}}".Fill(new { other = 1 }, nullReplacementString: "NULL"));
    }

    [Fact]
    public void Fill_APlaceholderSpelledWithSpaces_AlsoFillsItsPlainSpelling()
    {
        const string spaced = @"(?<open_marker>\{\{)\s*(?<param>.*?)\s*(?<close_marker>\}\})";

        Assert.Equal("x ID y '{{' z ID w", "x {{ id }} y '{{' z {{id}} w".Fill(new { id = "ID" }, nullReplacementString: "<null>", queryParamsRegex: spaced));
    }

    [Fact]
    public void Fill_ASetBetweenSetsWithOnePattern_TakesOnlyPlaceholdersItsPatternMatchesWhereTheyAreWritten()
    {
        const string colon = @"(?<open_marker>:)(?<param>\w+)";
        const string afterSpace = @"(?<=\s)(?<open_marker>:)(?<param>\w+)";
        const string notACast = @"(?<![:\w])(?<open_marker>:)(?<param>\w+)(?<close_marker>)";

        Assert.Equal("WHERE id=defaults", "WHERE id=:id".Fill(Sets(
            (colon, new { id = "defaults" }), (afterSpace, new { id = "request" }), (colon, new { other = 1 }))));
        Assert.Equal("WHERE id = request", "WHERE id = :id".Fill(Sets(
            (colon, new { id = "defaults" }), (afterSpace, new { id = "request" }), (colon, new { other = 1 }))));
        Assert.Equal("SELECT a:1", "SELECT a::x".Fill(Sets(
            (colon, new { x = 1 }), (notACast, new { x = 2 }), (colon, new { y = 0 }))));
    }

    [Fact]
    public void Fill_APlaceholderASetTakes_IsConvertedOnce()
    {
        var calls = 0;
        var sets = Sets((QueryStringOrGenericRegex, new { id = "A" }), (JsonOrGenericRegex, new { id = "B" }), (QueryStringOrGenericRegex, new { other = 1 }));

        var result = "{{id}} {j{id}}".Fill(sets, valueConverter: (name, value) => $"{value}#{++calls}");

        Assert.Equal("B#1 B#1", result);
    }

    [Theory]
    [InlineData("%ab%c%c%", @"(?<open_marker>%)(?<param>\w+)(?<close_marker>%)", "c")]
    [InlineData("|Da|3|3|", @"(?<open_marker>\|)(?<param>.*?)?(?<close_marker>\|)", "3")]
    public void Fill_SymmetricDelimiters_WithoutValues_ReplaceAsWithValues(string template, string pattern, string expected)
    {
        Assert.Equal(expected, template.Fill(new { other = 1 }, queryParamsRegex: pattern));
    }

    [Fact]
    public void Fill_ASetWithoutBothMarkersBetweenSetsWithOnePattern_TakesOnlyTheUsesItsPatternMatches()
    {
        const string colon = @"(?<open_marker>:)(?<param>\w+)";

        Assert.Equal("SELECT 20 AS a, 10-1 AS b", "SELECT :d AS a, :d-1 AS b".Fill(Sets(
            (colon, new { d = 10 }), (@"(?<open_marker>:)(?<param>[\w-]+)", new { d = 20 }), (colon, new { x = 0 }))));
        Assert.Equal("id = body OR owner = qs", "id = $id OR owner = $id".Fill(Sets(
            (@"\$(?<param>\w+)", new { id = "body" }), (@"(?<=owner\s=\s)\$(?<param>\w+)", new { id = "qs" }), (@"\$(?<param>\w+)", new { other = "x" }))));
    }

    [Fact]
    public void Fill_NullPlaceholdersOfSymmetricDelimiters_AreReplacedAsWithValues()
    {
        const string pipes = @"(?<open_marker>\|\|)(?<param>.*?)?(?<close_marker>\|\|)";

        Assert.Equal("||||c<N> |<N><N>", "||||c||?|| |||c||||c||".Fill(Sets((pipes, new { other = 1 })), nullReplacementString: "<N>"));
    }

    [Fact]
    public void Fill_ASetBetweenSetsWithOnePattern_WhoseOnlyUseTheirOwnLaterSourceTakes_TakesNothing()
    {
        // The third set (the body pattern again) reads "a {{x" over the query-string set's only {{x}} first.
        Assert.Equal("SELECT a3, {qs{b a1", "SELECT {j{a {{x}}, {qs{b {{x}}".Fill(Sets(
            (JsonOrGenericRegex, new { x = "a1" }),
            (QueryStringOrGenericRegex, new { x = "b2" }),
            (JsonOrGenericRegex, new Dictionary<string, object> { ["a {{x"] = "a3" }))));

        // The {q{ set reads the longer {q{{x}} where the shared pattern reads {{x}}, so those keep the first set's
        // value; and its text pass covers both places the second set reads {{x}}, so that set takes nothing.
        const string notAfterBang = @"(?:^|[^!])(?<open_marker>\{\{)(?<param>\w+)(?<close_marker>\}\})";
        const string qNotAfterBang = @"(?:^|[^!])(?<open_marker>\{q\{)(?<param>.*?)(?<close_marker>\}\})";
        Assert.Equal("SELECT {qa1, !{qa1, !a1", "SELECT {q{{x}}, !{q{{x}}, !{{x}}".Fill(Sets(
            (DefaultRegex, new { x = "a1" }),
            (notAfterBang, new { x = "b2" }),
            (qNotAfterBang, new Dictionary<string, object> { ["{x"] = "q3" }),
            (DefaultRegex, new { z = 0 }))));
    }

    [Fact]
    public void Fill_ASetBetweenSetsWithOnePattern_TakesTheUsesALaterSetsPassLeavesAlone()
    {
        // The bare set replaces only its own match, ({{id}}). Its text, {{id}}, occurs at the second use too, but
        // nothing replaces it there, so the body set between the two default-pattern sets takes that use.
        const string bareInParentheses = @"(?<=\()\{\{(?<param>\w+)\}\}";
        Assert.Equal("SELECT (nm), body", "SELECT ({{id}}), {{id}}".Fill(Sets(
            (DefaultRegex, new { id = "hdr" }),
            (JsonOrGenericRegex, new { id = "body" }),
            (bareInParentheses, new { id = "nm" }),
            (DefaultRegex, new { z = "route" }))));

        // With a null value for the name, the bare set leaves ({{id}}) to the sets before it, so the body set takes it
        // there too.
        Assert.Equal("SELECT (body)", "SELECT ({{id}})".Fill(Sets(
            (DefaultRegex, new { id = "hdr" }),
            (JsonOrGenericRegex, new { id = "body" }),
            (bareInParentheses, new Dictionary<string, object> { ["id"] = null!, ["other"] = "o" }),
            (DefaultRegex, new { z = "route" }))));

        // The colon set's text, :x, isn't replaced where a name character follows it (inside {{a:xy}}), so the
        // second set (the default pattern written another way) takes {{a:xy}}.
        const string colon = @"(?:^|[^:])(?<open_marker>:)(?<param>\w+)(?<close_marker>)";
        Assert.Equal("SELECT b2, c3", "SELECT {{a:xy}}, :x".Fill(Sets(
            (DefaultRegex, new Dictionary<string, object> { ["a:xy"] = "a1" }),
            (DefaultRegex + "(?:)", new Dictionary<string, object> { ["a:xy"] = "b2" }),
            (colon, new { x = "c3" }),
            (DefaultRegex, new { z = 0 }))));

        // Inside {{x:a}} nothing follows :a but the close marker, so the colon set's text pass replaces it there,
        // before the second set reads; that set's pass never reaches {{x:a}}, and the first set's value stays.
        const string colonAfterSpace = @"(?<=\s)(?<open_marker>:)(?<param>\w+)";
        Assert.Equal("SELECT a1, c3", "SELECT {{x:a}}, :a".Fill(Sets(
            (DefaultRegex, new Dictionary<string, object> { ["x:a"] = "a1" }),
            (JsonOrGenericRegex, new Dictionary<string, object> { ["x:a"] = "b2" }),
            (colonAfterSpace, new { a = "c3" }),
            (DefaultRegex, new { z = 0 }))));

        // A spaced placeholder's pass also replaces its plain spelling ({{x}} for {{ x }}), inside {q{{x}} too. So
        // the second set's pass never reaches {q{{x}}, and the first set's value stays.
        const string q = @"(?<open_marker>\{\{|\{q\{)(?<param>.*?)?(?<close_marker>\}\})";
        const string spaced = @"(?:^|[^!q])(?<open_marker>\{\{)\s*(?<param>\w+)\s*(?<close_marker>\}\})";
        Assert.Equal("SELECT a1, q3", "SELECT {q{{x}}, {{ x }}".Fill(Sets(
            (q, new Dictionary<string, object> { ["{x"] = "a1" }),
            (q + "(?:)", new Dictionary<string, object> { ["{x"] = "b2" }),
            (spaced, new { x = "q3" }),
            (q, new { z = 0 }))));
    }

    [Fact]
    public void Fill_ASetBetweenSetsWithOnePattern_WhosePatternTakesTheCharacterBeforeItsMarker_TakesEveryUseOfItsText()
    {
        // These patterns take a character next to their markers, so as written they read only some uses of a text;
        // the set's pass still replaces that text everywhere, so it takes every use.
        const string notEscaped = @"(?:^|[^\\])(?<open_marker>\{\{)(?<param>.*?)(?<close_marker>\}\})";
        Assert.Equal("V4V3V3", "{{x}}{{ab}}{{ab}}".Fill(Sets(
            (DefaultRegex, new { ab = "V0" }),
            (notEscaped, new { ab = "V3" }),
            (DefaultRegex, new { x = "V4" }))));

        const string betweenSpaces = @"(?:^|\s)(?<open_marker>\{\{)(?<param>\w+)(?<close_marker>\}\})(?:\s|$)";
        Assert.Equal("Dear T V1 A B V1", "Dear {{title}} {{name}} {{a}} {{b}} {{name}}".Fill(Sets(
            (DefaultRegex, new { name = "V0", a = "A", b = "B" }),
            (betweenSpaces, new { name = "V1" }),
            (DefaultRegex, new { title = "T" }))));

        // The spaced pattern on both sides of the pair binds {{ id }}; the set between reads it as " id ".
        const string spaced = @"(?<open_marker>\{\{)\s*(?<param>\w+)\s*(?<close_marker>\}\})";
        Assert.Equal("V2V1V1", "{{a}}{{ id }}{{ id }}".Fill(Sets(
            (spaced, new { id = "V0" }),
            (notEscaped, new Dictionary<string, object> { [" id "] = "V1" }),
            (spaced, new { a = "V2" }))));
    }

    [Fact]
    public async Task Fill_ManyUsesOfAOneMarkerNameAndOfALongerOne_DoesNotSlowDownWithTheirNumber()
    {
        // :id goes on into :id_parent: each use of :id is checked against the longer names once each, not once per
        // use of them (which took about 15 seconds here).
        const string colonAfterSpace = @"(?<=[\s(,])(?<open_marker>:)(?<param>\w+)(?<close_marker>)";
        var template = string.Concat(Enumerable.Repeat("UPDATE t SET p = :id_parent WHERE id=:id OR id = :id;\n", 40000));
        var run = Task.Run(() => template.Fill(new { id = 1, id_parent = 2 }, queryParamsRegex: colonAfterSpace));
        Assert.Same(run, await Task.WhenAny(run, Task.Delay(TimeSpan.FromSeconds(10))));

        Assert.StartsWith("UPDATE t SET p = 2 WHERE id=1 OR id = 1;\nUPDATE t SET p = 2 WHERE id=1 OR id = 1;", await run);
    }

    [Fact]
    public async Task Fill_ManyLongPlaceholdersWithoutAValue_DoNotSlowDownWithTheirLength()
    {
        // 800 stray '{{' paired with a far '}}', each a NULL placeholder of its own length: ordering them (a text
        // inside another one goes first) costs one search per pair, not one lookup per part of each text (which
        // took about half a minute here).
        var template = string.Concat(Enumerable.Range(0, 800).Select(i => "'{{' || '" + new string('z', 100 + i) + "}}', "));
        var run = Task.Run(() => template.Fill(new { title = "T" }, "<N>"));
        Assert.Same(run, await Task.WhenAny(run, Task.Delay(TimeSpan.FromSeconds(10))));

        Assert.Equal(string.Concat(Enumerable.Repeat("'<N>', ", 800)), await run);
    }

    [Fact]
    public async Task Fill_ManyNamesAndASetBetweenSetsWithOnePattern_DoesNotSlowDownWithTheirNumber()
    {
        // 2,000 names, each in three sets, the first set's pattern listed again last: the places the later sets take
        // are worked out once per set, not once per placeholder and match (which took about a minute here).
        var names = Enumerable.Range(0, 2000).ToList();
        var template = string.Concat(names.Select(i => $"UPDATE t SET a = {{{{x{i}}}}} WHERE id = {i};\n"));
        var run = Task.Run(() => template.Fill(Sets(
            (DefaultRegex, names.ToDictionary(i => "x" + i, i => (object)"1")),
            (JsonOrGenericRegex, names.ToDictionary(i => "x" + i, i => (object)"2")),
            (QueryStringOrGenericRegex, names.ToDictionary(i => "x" + i, i => (object)"3")),
            (DefaultRegex, new { other = "x" }))));
        Assert.Same(run, await Task.WhenAny(run, Task.Delay(TimeSpan.FromSeconds(10))));

        Assert.StartsWith("UPDATE t SET a = 3 WHERE id = 0;\nUPDATE t SET a = 3 WHERE id = 1;", await run);
    }

    [Fact]
    public void Fill_ValueConverter_IsCalledOncePerName()
    {
        var calls = 0;

        var result = "{{a}} {{a}} {{A}}".Fill(new { a = 1 }, valueConverter: (name, value) => { calls++; return $"<{value}>"; });

        Assert.Equal("<1> <1> <1>", result);
        Assert.Equal(1, calls);
    }

    #endregion
}
