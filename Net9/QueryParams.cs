using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Com.H.Data.Common
{
    public class QueryParams
    {
        /// <summary>
        /// DataModel could be a class or an anonymous object
        /// Or it could be an IDictionary<string, object>
        /// </summary>
        public object? DataModel { get; set; }
        public string QueryParamsRegex { get; set; } = @"(?<open_marker>\{\{)(?<param>.*?)?(?<close_marker>\}\})";

        //public string DataTypeRegex { get; set; } = @"(?<open_marker>\{type\{)(?<type>.*?)\{(?<param>.*?)?(?<close_marker>\}\}\})";

        //private Regex dataTypeRegexCompiled = null!;

        //public Regex DataTypeRegexCompiled
        //{
        //    get
        //    {
        //        if (dataTypeRegexCompiled != null) return dataTypeRegexCompiled;
        //        return dataTypeRegexCompiled = new Regex(DataTypeRegex, RegexOptions.Compiled);
        //    }
        //}


    }
}
