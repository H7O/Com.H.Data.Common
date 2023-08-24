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
        private static readonly JsonDocumentOptions _jsonOptions = new JsonDocumentOptions() { MaxDepth = 1 };
        public static IDictionary<string, object>? GetDataModelParameters(this object dataModel, bool descending = false)
        {
            if (dataModel == null) return null;
            Dictionary<string, object> result = new();
            foreach (var item in dataModel.EnsureEnumerable())
            {
                if (((object?)item) == null) continue;
                if (typeof(IDictionary<string, object>).IsAssignableFrom(item.GetType()))
                {
                    foreach (var x in ((IDictionary<string, object>)item))
                    {
                        if (result.ContainsKey(x.Key) && !descending) continue;
                        result[x.Key] = x.Value;
                    }
                    continue;
                }
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

                foreach (var x in ((object)item).GetType().GetProperties())
                {
                    if (result.ContainsKey(x.Name) && !descending) continue;
                    result[x.Name] = x.GetValue(item, null);
                }
            }
            return result;
        }
        //public static string ReplaceQueryParameterMarkers(
        //    this string query,
        //    string srcOpenMarker,
        //    string srcCloseMarker,
        //    string dstOpenMarker,
        //    string dstCloseMarker)
        //{
        //    if (string.IsNullOrEmpty(query)) return query;
        //    var regexPattern = srcOpenMarker + QueryParams.RegexPattern + srcCloseMarker;
        //    var paramList = Regex.Matches(query, regexPattern)
        //        .Cast<Match>()
        //        .Select(x => x.Groups["param"].Value)
        //        .Where(x => !string.IsNullOrEmpty(x))
        //        .Select(x => x).Distinct().ToList();

        //    foreach (var item in paramList)
        //    {
        //        query = query.Replace(srcOpenMarker + item + srcCloseMarker,
        //            dstOpenMarker + item + dstCloseMarker);
        //    }

        //    return query;
        //}
    }
}
