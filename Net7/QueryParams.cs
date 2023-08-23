﻿namespace Com.H.Data.Common
{
    public class QueryParams
    {
        /// <summary>
        /// DataModel could be a class or an anonymous object
        /// Or it could be an IDictionary<string, object>
        /// </summary>
        public object? DataModel { get; set; }
        public string QueryParamsRegex { get; set; } = @"\{\{(?<param>.*?)?\}\}";

    }
}
