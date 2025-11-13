using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Com.H.Data.Common
{
    /// <summary>
    /// Represents query parameters with flexible regex-based delimiter patterns for prepared statements.
    /// Supports multiple parameter source types and customizable parameter delimiters in SQL queries.
    /// Used internally by ExecuteQuery methods to parse and bind parameters to database commands.
    /// </summary>
    /// <remarks>
    /// This class allows you to define custom parameter delimiters using regex patterns.
    /// The default pattern uses {{ }} delimiters, but you can customize it to use [[ ]], || ||, or any other delimiter pattern.
    /// Multiple DbQueryParams with different delimiter patterns can be combined, allowing different parameter syntaxes in the same query.
    /// </remarks>
    /// <example>
    /// <code>
    /// // Default {{ }} delimiters
    /// var params1 = new DbQueryParams 
    /// { 
    ///     DataModel = new { name = "John", age = 30 },
    ///     QueryParamsRegex = @"(?&lt;open_marker&gt;\{\{)(?&lt;param&gt;.*?)?(?&lt;close_marker&gt;\}\})"
    /// };
    /// 
    /// // Custom [[ ]] delimiters
    /// var params2 = new DbQueryParams 
    /// { 
    ///     DataModel = new { city = "New York" },
    ///     QueryParamsRegex = @"(?&lt;open_marker&gt;\[\[)(?&lt;param&gt;.*?)?(?&lt;close_marker&gt;\]\])"
    /// };
    /// </code>
    /// </example>
    public class DbQueryParams
    {
        /// <summary>
        /// Gets or sets the data model containing parameter values.
        /// Can be an anonymous object, Dictionary&lt;string, object&gt;, regular object with properties,
        /// JsonElement, or JSON string. Property/key names must match parameter names in the query.
        /// </summary>
        /// <example>
        /// <code>
        /// // Anonymous object
        /// DataModel = new { name = "John", age = 30 };
        /// 
        /// // Dictionary
        /// DataModel = new Dictionary&lt;string, object&gt; { { "name", "Jane" }, { "age", 25 } };
        /// 
        /// // JsonElement
        /// DataModel = JsonDocument.Parse("{\"name\":\"Bob\",\"age\":35}").RootElement;
        /// 
        /// // JSON string
        /// DataModel = "{\"name\":\"Alice\",\"age\":28}";
        /// 
        /// // Custom class
        /// public class Person { public string Name { get; set; } public int Age { get; set; } }
        /// DataModel = new Person { Name = "Charlie", Age = 40 };
        /// </code>
        /// </example>
        public object? DataModel { get; set; }
        
        /// <summary>
        /// Gets or sets the regex pattern for identifying parameter placeholders in SQL queries.
        /// Must contain named groups: open_marker, param, and close_marker.
        /// Default pattern: @"(?&lt;open_marker&gt;\{\{)(?&lt;param&gt;.*?)?(?&lt;close_marker&gt;\}\})" for {{ }} delimiters.
        /// </summary>
        /// <remarks>
        /// The regex pattern must define three named capture groups:
        /// - open_marker: The opening delimiter (e.g., {{ or [[ or |)
        /// - param: The parameter name
        /// - close_marker: The closing delimiter (e.g., }} or ]] or |)
        /// </remarks>
        /// <example>
        /// <code>
        /// // Default {{ }} delimiters
        /// QueryParamsRegex = @"(?&lt;open_marker&gt;\{\{)(?&lt;param&gt;.*?)?(?&lt;close_marker&gt;\}\})";
        /// // Query: "SELECT * FROM Users WHERE name = {{name}}"
        /// 
        /// // Square brackets [[ ]] delimiters
        /// QueryParamsRegex = @"(?&lt;open_marker&gt;\[\[)(?&lt;param&gt;.*?)?(?&lt;close_marker&gt;\]\])";
        /// // Query: "SELECT * FROM Users WHERE name = [[name]]"
        /// 
        /// // Pipe | delimiters
        /// QueryParamsRegex = @"(?&lt;open_marker&gt;\|)(?&lt;param&gt;.*?)?(?&lt;close_marker&gt;\|)";
        /// // Query: "SELECT * FROM Users WHERE name = |name|"
        /// </code>
        /// </example>
        public string QueryParamsRegex { get; set; } = @"(?<open_marker>\{\{)(?<param>.*?)?(?<close_marker>\}\})";

    }
}
