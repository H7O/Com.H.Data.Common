using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Com.H.Data.Common
{

    public static class DataExtensions
    {
        private static readonly JsonDocumentOptions _jsonOptions = new() { MaxDepth = 1 };
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
        /// Reduce the list to have QueryParams with unique QueryParamsRegex
        /// and merge the DataModel of the QueryParams with the same QueryParamsRegex
        /// reduce the list to have QueryParams with unique QueryParamsRegex
        /// into one DataModel. The DataModel of the QueryParams with the same QueryParamsRegex
        /// will be merged into the first QueryParams in the list.
        /// If a DataModel has the same key as the DataModel of the first QueryParams in the list,
        /// the value of the DataModel of the first QueryParams in the list will be replaced
        /// by the value of the last DataModel.
        /// </summary>
        /// <param name="queryParamsList"></param>
        /// <param name="reverse"></param>
        /// <returns></returns>
        public static List<DbQueryParams> ReduceToUnique(
            this IEnumerable<DbQueryParams> queryParamsList,
            bool reverse = false)
        {
            if (queryParamsList == null) return [];
            var result = new List<DbQueryParams>();
            foreach (var group in (reverse?queryParamsList.Reverse():queryParamsList)
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
