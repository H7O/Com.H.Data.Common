namespace Com.H.Data.Common
{

    internal static class DataExtensions
    {
        public static IDictionary<string, object>? GetDataModelParameters(this object dataModel, bool descending = false)
        {
            if (dataModel == null) return null;
            Dictionary<string, object> result = new();
            foreach (var item in dataModel.EnsureEnumerable())
            {
                if (item == null) continue;
                if (typeof(IDictionary<string, object>).IsAssignableFrom(item.GetType()))
                {
                    foreach (var x in ((IDictionary<string, object>)item))
                    {
                        if (result.ContainsKey(x.Key) && !descending) continue;
                        result[x.Key] = x.Value;
                    }
                    continue;
                }
                foreach(var x in ((object)item).GetType().GetProperties())
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
