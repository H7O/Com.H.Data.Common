using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Com.H.Data.Common
{
    /// <summary>
    /// Provides extension methods for data model parameter extraction and query parameter management.
    /// Supports multiple parameter source types including anonymous objects, dictionaries, JSON strings, and JsonElement.
    /// </summary>
    public static class DataExtensions
    {
        private static readonly JsonDocumentOptions _jsonOptions = new() { MaxDepth = 64 }; // Increased from 1 to handle nested structures
        
        /// <summary>
        /// Extracts parameters from various data model types into a dictionary.
        /// Supports anonymous objects, dictionaries (string-object or string-string), JsonElement, JSON strings, and regular objects.
        /// Nested JSON/XML structures are returned as raw JSON/XML text strings.
        /// </summary>
        /// <param name="dataModel">The data model to extract parameters from. Can be an anonymous object, Dictionary&lt;string,object&gt;, 
        /// Dictionary&lt;string,string&gt;, JsonElement, JSON string, or any object with properties.</param>
        /// <param name="descending">If true, later values override earlier ones when duplicate keys are encountered. Default is false.</param>
        /// <param name="caseSensitive">If true, uses case-sensitive key comparison. If false (default), uses case-insensitive comparison.</param>
        /// <returns>A dictionary containing parameter names as keys and their values, or null if dataModel is null</returns>
        /// <example>
        /// <code>
        /// // From anonymous object (case-insensitive by default)
        /// var params1 = new { name = "John", age = 30 }.GetDataModelParameters();
        /// 
        /// // From JsonElement
        /// var json = JsonDocument.Parse("{\"name\":\"Jane\",\"age\":25}").RootElement;
        /// var params2 = json.GetDataModelParameters();
        /// 
        /// // Case-sensitive lookup
        /// var params3 = new { Name = "Bob" }.GetDataModelParameters(caseSensitive: true);
        /// </code>
        /// </example>
        public static IDictionary<string, object>? GetDataModelParameters(
            this object dataModel, 
            bool descending = false,
            bool caseSensitive = false)
        {
            if (dataModel == null) return null;
            var comparer = caseSensitive ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase;
            Dictionary<string, object> result = new(comparer);
            foreach (var item in dataModel.EnsureEnumerable())
            {
                if (((object?)item) == null) continue;
                #region check for string object pair
                if (typeof(IDictionary<string, object>).IsAssignableFrom(item.GetType()))
                {
                    foreach (var x in ((IDictionary<string, object>)item))
                    {
                        if (result.ContainsKey(x.Key) && !descending) continue;
                        result[x.Key] = x.Value;
                    }
                    continue;
                }
                if (typeof(IEnumerable<KeyValuePair<string, object>>).IsAssignableFrom(item.GetType()))
                {
                    foreach (var x in ((IEnumerable<KeyValuePair<string, object>>)item))
                    {
                        if (result.ContainsKey(x.Key) && !descending) continue;
                        result[x.Key] = x.Value;
                    }
                    continue;
                }
                #endregion

                #region check for string string pair
                if (typeof(IDictionary<string, string>).IsAssignableFrom(item.GetType()))
                {
                    foreach (var x in ((IDictionary<string, string>)item))
                    {
                        if (result.ContainsKey(x.Key) && !descending) continue;
                        result[x.Key] = x.Value;
                    }
                    continue;
                }
                if (typeof(IEnumerable<KeyValuePair<string, string>>).IsAssignableFrom(item.GetType()))
                {
                    foreach (var x in ((IEnumerable<KeyValuePair<string, string>>)item))
                    {
                        if (result.ContainsKey(x.Key) && !descending) continue;
                        result[x.Key] = x.Value;
                    }
                    continue;
                }
                #endregion

                #region check for JsonElement
                if (typeof(JsonElement).IsAssignableFrom(item.GetType()))
                {
                    JsonElement json = (JsonElement)item;
                    if (json.ValueKind == JsonValueKind.Object)
                    {
                        foreach (var x in json.EnumerateObject())
                        {
                            if (result.ContainsKey(x.Name) && !descending) continue;
                            switch (x.Value.ValueKind)
                            {
                                case JsonValueKind.False:
                                    result[x.Name] = false; break;
                                case JsonValueKind.True:
                                    result[x.Name] = true; break;
                                case JsonValueKind.Number:
                                    result[x.Name] = x.Value.GetDouble(); break;
                                case JsonValueKind.String:
                                    result[x.Name] = x.Value.GetString()!; break;
                                case JsonValueKind.Null:
                                    result[x.Name] = null!; break;
                                case JsonValueKind.Array:
                                case JsonValueKind.Object:
                                    // For nested structures, return the raw JSON text as a string
                                    result[x.Name] = x.Value.GetRawText();
                                    break;
                                default:
                                    result[x.Name] = x.Value.ToString();
                                    break;
                            }
                        }
                    }
                    continue;
                }
                #endregion

                #region check for string
                if (typeof(string) == item.GetType())
                {
                    try
                    {
                        var json = JsonDocument.Parse(item, _jsonOptions).RootElement;
                        // var json = System.Text.Json.JsonSerializer.Deserialize<JsonElement>(item);
                        if (json.ValueKind == JsonValueKind.Object)
                        {
                            foreach (var x in json.EnumerateObject())
                            {
                                if (result.ContainsKey(x.Name) && !descending) continue;
                                switch (x.Value.ValueKind)
                                {
                                    case JsonValueKind.False:
                                        result[x.Name] = false; break;
                                    case JsonValueKind.True:
                                        result[x.Name] = true; break;
                                    case JsonValueKind.Number:
                                        result[x.Name] = x.Value.GetDouble(); break;
                                    case JsonValueKind.String:
                                        result[x.Name] = x.Value.GetString()!; break;
                                    case JsonValueKind.Null:
                                        result[x.Name] = null!; break;
                                    case JsonValueKind.Array:
                                    case JsonValueKind.Object:
                                        // For nested structures, return the raw JSON text as a string
                                        result[x.Name] = x.Value.GetRawText();
                                        break;
                                    default:
                                        result[x.Name] = x.Value.ToString();
                                        break;
                                }
                            }
                        }
                    }
                    catch { }
                    continue;
                }
                #endregion

                foreach (var x in ((object)item).GetType().GetProperties())
                {
                    if (result.ContainsKey(x.Name) && !descending) continue;
                    result[x.Name] = x.GetValue(item, null);
                }
            }
            return result;
        }

        /// <summary>
        /// Reduces a list of DbQueryParams to unique QueryParamsRegex values and merges their data models.
        /// For QueryParams with the same regex pattern, their data models are combined into one.
        /// When duplicate keys exist across data models with the same regex, the behavior depends on the reverse parameter.
        /// </summary>
        /// <param name="queryParamsList">The list of query parameters to reduce</param>
        /// <param name="reverse">If true, processes in reverse order and later values override earlier ones for duplicate keys. 
        /// If false, earlier values are preserved for duplicate keys.</param>
        /// <param name="caseSensitive">If true, uses case-sensitive key comparison. If false (default), uses case-insensitive comparison.</param>
        /// <returns>A list of DbQueryParams with unique regex patterns and merged data models</returns>
        /// <example>
        /// <code>
        /// var params1 = new DbQueryParams { 
        ///     QueryParamsRegex = @"\{\{.*?\}\}", 
        ///     DataModel = new { name = "John" } 
        /// };
        /// var params2 = new DbQueryParams { 
        ///     QueryParamsRegex = @"\{\{.*?\}\}", 
        ///     DataModel = new { age = 30 } 
        /// };
        /// 
        /// var merged = new[] { params1, params2 }.ReduceToUnique();
        /// // Result: One DbQueryParams with DataModel containing both name and age
        /// </code>
        /// </example>
        public static List<DbQueryParams> ReduceToUnique(
            this IEnumerable<DbQueryParams> queryParamsList,
            bool reverse = false,
            bool caseSensitive = false)
        {
            if (queryParamsList == null) return [];
            var comparer = caseSensitive ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase;
            var result = new List<DbQueryParams>();
            // A null entry is skipped.
            foreach (var group in (reverse ? queryParamsList.Reverse() : queryParamsList)
                .Where(x => x is not null)
                .GroupBy(x => x.QueryParamsRegex))
            {
                var mergedDataModels = new Dictionary<string, object>(comparer);

                foreach (var queryParams in group)
                {
                    foreach (var dataModelItem in queryParams.DataModel?.GetDataModelParameters(reverse, caseSensitive)
                        ?? new Dictionary<string, object>())
                    {
                        if (dataModelItem.Key is null) continue;
                        mergedDataModels.TryAdd(dataModelItem.Key, dataModelItem.Value);
                    }
                }
                result.Add(new DbQueryParams
                {
                    QueryParamsRegex = group.Key,
                    DataModel = mergedDataModels
                });

            }
            return result;

        }

        /// <summary>
        /// Replaces parameter placeholders in a template string with values from the provided parameters.
        /// Designed for non-SQL templating scenarios such as HTML templates, email content, configuration files, etc.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>Important:</b> This method performs direct string replacement and is intended for general templating purposes.
        /// For SQL queries, use <see cref="AdoNetExt.ExecuteQueryAsync(System.Data.Common.DbCommand, string, object?, string, bool, System.Threading.CancellationToken)"/> 
        /// or related methods which provide proper SQL parameterization to prevent SQL injection.
        /// </para>
        /// <para>
        /// <b>Parameter Types:</b> The queryParams parameter accepts multiple types:
        /// <list type="bullet">
        /// <item><description>Anonymous objects: <c>new { name = "John", age = 30 }</c></description></item>
        /// <item><description>Dictionary&lt;string, object&gt;</description></item>
        /// <item><description>IEnumerable&lt;DbQueryParams&gt; for advanced scenarios with multiple regex patterns</description></item>
        /// <item><description>JsonElement or JSON string</description></item>
        /// <item><description>Any object with properties</description></item>
        /// </list>
        /// </para>
        /// <para>
        /// <b>Case Sensitivity:</b> By default, parameter name lookup is case-insensitive (caseSensitive = false).
        /// Set caseSensitive to true for strict case matching when needed: {{NAME}} is then a different
        /// placeholder from {{name}}, and is filled only from a NAME value.
        /// </para>
        /// <para>
        /// <b>How placeholders are filled:</b> by the rules the SQL methods bind them with. Each placeholder
        /// is replaced once, and its value is never read again: text inside a value that looks like a
        /// placeholder ({{other}}, or another set's marker) stays as it is. One name written with different
        /// markers of one pattern ({{id}} and {j{id}} under a pattern that accepts both) gets one value.
        /// With several IEnumerable&lt;DbQueryParams&gt; sets whose patterns match the same placeholder where it
        /// is written, the latest set in the list with a non-null value under that name fills it. Among sets
        /// with the exact same pattern string, a later set's value for a name decides, even when it is null;
        /// a set with another pattern keeps its own place in the list, so one listed between them may take
        /// one spelling of a name ({{id}}) while the other ({j{id}}) keeps theirs. It takes a placeholder
        /// where its own pattern reads that same placeholder: with both markers, every use of its text, as its
        /// own pass would; with one marker or none, only the uses the shared pattern matches there too (not the
        /// uses it recovers from the text as written after a stray marker). It binds the plain spelling of its
        /// spaced placeholder ({{id}} for {{ id }}) only where its own pass still has that placeholder, or where
        /// they have a value for that spelling: not through one it takes from them under another name where they
        /// have none, nor through one a stray marker pairs past. Where
        /// it reads a longer or shorter one there (:x-1 against :x), the earlier set's value is used. A placeholder that no set has a value for
        /// becomes nullReplacementString. A placeholder that no set's pattern matches stays as it is.
        /// </para>
        /// <para>
        /// <b>Where the text is replaced:</b> when the pattern has an open and a close marker, the text a
        /// placeholder takes up ({{ name }} under a pattern that allows spaces) is replaced wherever it
        /// appears, once the pattern has matched it on its own somewhere in the text as written. A stray open
        /// marker before a placeholder's only use pairs with that use's close marker, and the whole span
        /// between them is then a placeholder of its own; when no set has a value for it, another set's stray
        /// open marker can take a use of it that way too. If the span holds a placeholder another set has
        /// filled, the span usually stays as written instead, and a placeholder the pattern never matched on
        /// its own there stays unfilled. With one marker (":name"), the text is replaced
        /// wherever it doesn't go on into a longer name: a letter, digit or underscore after it, or a longer
        /// placeholder the pattern matched (":date" leaves ":date_from" alone, and ":date-from" under a
        /// pattern that allows "-"). A pattern with neither marker replaces only its matches. A placeholder
        /// without a value is replaced the same way (the SQL methods bind NULL only where the pattern matches).
        /// Unless <paramref name="queryParams"/> is null or an empty IEnumerable&lt;DbQueryParams&gt; (the text is
        /// then returned as it is), text that contains every private-use character (U+E000 to U+F8FF) throws an
        /// ArgumentException: one is needed to mark the placeholders.
        /// </para>
        /// </remarks>
        /// <param name="input">The template string containing parameter placeholders (e.g., "Hello {{name}}")</param>
        /// <param name="queryParams">The parameters object. Can be an anonymous object, Dictionary, IEnumerable&lt;DbQueryParams&gt;, 
        /// JsonElement, JSON string, or any object with properties matching placeholder names.</param>
        /// <param name="nullReplacementString">The string to use when a parameter value is null or not found. Defaults to empty string.</param>
        /// <param name="valueConverter">Optional custom converter function. Receives the parameter name and value, returns the string representation.
        /// If null, uses the default ToString() conversion.</param>
        /// <param name="caseSensitive">If true, uses case-sensitive parameter name matching. If false (default), uses case-insensitive matching.</param>
        /// <param name="queryParamsRegex">Regex pattern for parameter delimiters. Default: @"(?&lt;open_marker&gt;\{\{)(?&lt;param&gt;.*?)?(?&lt;close_marker&gt;\}\})" for {{ }} delimiters.
        /// Only used when queryParams is not IEnumerable&lt;DbQueryParams&gt;.</param>
        /// <returns>The template string with all matching placeholders replaced by their corresponding values</returns>
        /// <example>
        /// <code>
        /// // Simple usage with anonymous object (recommended for most cases)
        /// var result = "Hello {{name}}!".Fill(new { name = "World" });
        /// // Result: "Hello World!"
        /// 
        /// // HTML template
        /// var html = "&lt;h1&gt;Hello {{name}}&lt;/h1&gt;&lt;p&gt;You have {{count}} messages.&lt;/p&gt;";
        /// var result2 = html.Fill(new { name = "John", count = 5 });
        /// // Result: "&lt;h1&gt;Hello John&lt;/h1&gt;&lt;p&gt;You have 5 messages.&lt;/p&gt;"
        /// 
        /// // Case-insensitive matching (default) - {{NAME}} matches "name" key
        /// var result3 = "Hello {{NAME}}!".Fill(new { name = "World" });
        /// // Result: "Hello World!"
        /// 
        /// // Case-sensitive matching - {{NAME}} does NOT match "name" key
        /// var result4 = "Hello {{NAME}}!".Fill(new { name = "World" }, caseSensitive: true);
        /// // Result: "Hello !" (NAME not found, replaced with empty string)
        /// 
        /// // Case-sensitive matching - {{NAME}} does NOT match "name" key
        /// // With custom value converter for date formatting
        /// var result5 = "Event date: {{date}}".Fill(
        ///     new { date = new DateTime(2024, 1, 15) },
        ///     valueConverter: (name, value) =&gt; 
        ///         value is DateTime dt ? dt.ToString("MMMM dd, yyyy") : value?.ToString() ?? "");
        /// // Result: "Event date: January 15, 2024"
        /// 
        /// // Advanced usage with IEnumerable&lt;DbQueryParams&gt; for multiple regex patterns
        /// var template = "Auth: {auth{token}}, Data: {{name}}";
        /// var queryParamsList = new List&lt;DbQueryParams&gt;
        /// {
        ///     new() { DataModel = new { token = "secret" }, QueryParamsRegex = @"(?&lt;open_marker&gt;\{auth\{)(?&lt;param&gt;.*?)?(?&lt;close_marker&gt;\}\})" },
        ///     new() { DataModel = new { name = "John" } }
        /// };
        /// var result6 = template.Fill(queryParamsList);
        /// // Result: "Auth: secret, Data: John"
        /// </code>
        /// </example>
        
        public static string Fill(
            this string input,
            object? queryParams = null,
            string nullReplacementString = "",
            Func<string, object?, string>? valueConverter = null,
            bool caseSensitive = false,
            string queryParamsRegex = @"(?<open_marker>\{\{)(?<param>.*?)?(?<close_marker>\}\})"
            )
        {
            if (queryParams is not null)
            {
                if (queryParams is not IEnumerable<DbQueryParams>)
                {
                    queryParams = new List<DbQueryParams>()
                    {
                        new()
                        {
                            DataModel = queryParams,
                            QueryParamsRegex = queryParamsRegex
                        }
                    };
                }
            }
            return FillMain(
                input,
                queryParams as IEnumerable<DbQueryParams>,
                nullReplacementString,
                valueConverter,
                caseSensitive
            );
        }

        private static string FillMain(
            this string input,
            IEnumerable<DbQueryParams>? queryParamsList,
            string nullReplacementString = "",
            Func<string, object?, string>? valueConverter = null,
            bool caseSensitive = false)
        {
            if (string.IsNullOrEmpty(input)) return input ?? "";
            if (queryParamsList == null || !queryParamsList.Any()) return input;

            // The placeholders are found and resolved as on the SQL path: each becomes a slot, and the
            // values are written into the slots only at the end, so no pattern ever reads a value.
            var slots = AdoNetExt.ResolveMarkers(input, queryParamsList, replaceText: true, replaceNullText: true,
                caseSensitive, out var slotText, out var slotDelimiter);
            var textOfSlot = new string?[slots.Count];
            return AdoNetExt.FillSlots(slotText, slotDelimiter, slot =>
            {
                // Converted once per slot (a name in one set), however many times it appears.
                if (textOfSlot[slot] is null)
                {
                    var (name, _, value) = slots[slot];
                    textOfSlot[slot] = (value is null
                        ? nullReplacementString
                        : valueConverter is not null
                            ? valueConverter(name, value)
                            : value.ToString()) ?? "";
                }
                return textOfSlot[slot]!;
            });
        }
    }
}
