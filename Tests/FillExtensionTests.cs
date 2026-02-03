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
        // With ReduceToUnique(true), the LAST item in the list should win
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
    public void Fill_CaseSensitive_WhenIgnoreCaseFalse_TreatedAsNull()
    {
        // Arrange - Template has {{NAME}} but data model has "name" (lowercase)
        // With ignoreCase: false, dictionary lookup is case-sensitive
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
        var result = template.Fill(queryParams, ignoreCase: false);

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
        var result = template.Fill(queryParams, nullReplacementString: "[NOT_FOUND]", ignoreCase: false);

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
}
