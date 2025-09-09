using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Com.H.Data.Common
{
    /// <summary>
    /// Database-focused query parameters with support for prepared statements and flexible regex-based parameter parsing.
    /// Supports multiple delimiter types via regex OR logic for database queries.
    /// </summary>
    public class DbQueryParams
    {
        /// <summary>
        /// DataModel could be a class or an anonymous object
        /// Or it could be an IDictionary<string, object>
        /// </summary>
        public object? DataModel { get; set; }
        
        /// <summary>
        /// Flexible regex pattern supporting multiple delimiter types via OR logic.
        /// Example: @"((?<open_marker>\{\{)(?<param>.*?)(?<close_marker>\}\}))" for {{ }} delimiters
        /// Can be extended to: @"((?<open_marker>\{\{)(?<param>.*?)(?<close_marker>\}\}))|...(other patterns)"
        /// </summary>
        public string QueryParamsRegex { get; set; } = @"(?<open_marker>\{\{)(?<param>.*?)?(?<close_marker>\}\})";

    }
}
