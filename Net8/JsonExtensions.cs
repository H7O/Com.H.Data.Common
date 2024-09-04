using System;
using System.Collections.Generic;
using System.Dynamic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Com.H.Data.Common
{
    internal static class JsonExtensions
    {
        /// <summary>
        /// Parses JSON text into dynamic object
        /// </summary>
        /// <param name="jsonText"></param>
        /// <returns></returns>
        public static dynamic ParseJson(this string jsonText)
            => AsDynamic(JsonSerializer.Deserialize<dynamic>(jsonText));

        /// <summary>
        /// Converts a JsonElement to dynamic object
        /// </summary>
        /// <param name="jsonElement"></param>
        /// <returns></returns>
        public static dynamic? AsDynamic(this JsonElement jsonElement)
            =>
                jsonElement.ValueKind switch
                {
                    JsonValueKind.Array => jsonElement.EnumerateArray().Select(x => x.AsDynamic()),
                    JsonValueKind.Object => jsonElement.EnumerateObject()
                        .Aggregate(new ExpandoObject(),
                        (i, n) =>
                        {
                            i.TryAdd(n.Name, (object?)
                                (n.Value.ValueKind switch
                                {
                                    JsonValueKind.Array
                                    or JsonValueKind.Object => AsDynamic(n.Value),
                                    _ => n.Value.GetString()
                                })
                            );
                            return i;
                        }),
                    _ => (string?)null
                };



    }
}
