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
        /// <returns>A dictionary containing parameter names as keys and their values, or null if dataModel is null</returns>
        /// <example>
        /// <code>
        /// // From anonymous object
        /// var params1 = new { name = "John", age = 30 }.GetDataModelParameters();
        /// 
        /// // From JsonElement
        /// var json = JsonDocument.Parse("{\"name\":\"Jane\",\"age\":25}").RootElement;
        /// var params2 = json.GetDataModelParameters();
        /// 
        /// // From JSON string
        /// var params3 = "{\"name\":\"Bob\",\"age\":35}".GetDataModelParameters();
        /// </code>
        /// </example>
        public static IDictionary<string, object>? GetDataModelParameters(this object dataModel, bool descending = false)
        {
            if (dataModel == null) return null;
            Dictionary<string, object> result = [];
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
                                    result[x.Name] = x.Value.GetString(); break;
                                case JsonValueKind.Null:
                                    result[x.Name] = null; break;
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
                                        result[x.Name] = x.Value.GetString(); break;
                                    case JsonValueKind.Null:
                                        result[x.Name] = null; break;
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
            bool reverse = false)
        {
            if (queryParamsList == null) return [];
            var result = new List<DbQueryParams>();
            foreach (var group in (reverse ? queryParamsList.Reverse() : queryParamsList)
                .GroupBy(x => x.QueryParamsRegex))
            {
                var mergedDataModels = new Dictionary<string, object>();

                foreach (var queryParams in group)
                {
                    if (queryParams is null) continue;
                    foreach (var dataModelItem in queryParams.DataModel?.GetDataModelParameters(reverse)
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
    }
}
