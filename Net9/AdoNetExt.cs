using System.Data;
using System.Data.Common;
using System.Dynamic;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;


namespace Com.H.Data.Common
{



    public static class AdoNetExt
    {
        public static string DefaultParameterPrefix { get; set; } = "@";
        public static string DefaultParameterTemplate { get;set; } = "{{DefaultParameterPrefix}}vxv_{{ParameterCount}}_{{ParameterName}}";

        private readonly static string _cleanVariableNamesRegex = @"[-\s\.\(\)\[\]\{\}\:\;\,\?\!\#\$\%\^\&\*\+\=\|\\\/\~\`\´\'\""\<\>\=\?\ ]";
        private static readonly Regex _cleanVariableNamesRegexCompiled = new(_cleanVariableNamesRegex, RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline);

        private static string defaultDataTypeRegex = @"(?<open_marker>\{type\{)(?<type>.*?)\{(?<param>.*?)?(?<close_marker>\}\}\})";
        public static string DefaultDataTypeRegex
        {
            get
            {
                return defaultDataTypeRegex;
            }
            set
            {
                defaultDataTypeRegex = value;
                defaultDataTypeRegexCompiled = null!;
            }
        }
                

        private static Regex defaultDataTypeRegexCompiled = null!;

        private static Regex DefaultDataTypeRegexCompiled
        {
            get
            {
                if (defaultDataTypeRegexCompiled != null) return defaultDataTypeRegexCompiled;
                return defaultDataTypeRegexCompiled = new Regex(DefaultDataTypeRegex, RegexOptions.Compiled);
            }
        }


        internal readonly static DataMapper _mapper = new();

        public static DbConnection CreateDbConnection(this string connStr, string providerName = "Microsoft.Data.SqlClient")
        {
            var csb = new DbConnectionStringBuilder { ConnectionString = connStr };

            if (string.IsNullOrEmpty(providerName)
                &&
                csb.ContainsKey("provider")
                )
            {
                providerName = csb["provider"].ToString() ?? "";
            }

            if (string.IsNullOrWhiteSpace(providerName))
            {
                throw new ArgumentNullException(nameof(providerName),
                    "Provider name cannot be null or empty."
                    + Environment.NewLine
                    + " Make sure to define the correct provider in your connection string."
                    + Environment.NewLine
                    + @" E.g. ""Provider=Microsoft.Data.SqlClient; Data Source=MySqlServer\MSSQL1;User ID=Admin;Password=;`"""
                    );
            }
            try
            {
                var factory = DbProviderFactories.GetFactory(providerName);
                var connection = factory.CreateConnection() ?? throw new Exception($"Failed to create a using '{providerName}' provider."
                        + " Make sure to define the correct provider in your connection string."
                        + @" E.g. ""Provider=Microsoft.Data.SqlClient; Data Source=MySqlServer\MSSQL1;User ID=Admin;Password=;`""");
                if (csb.ContainsKey("provider")) csb.Remove("provider");
                connection.ConnectionString = csb.ConnectionString;
                return connection;
            }
            catch (Exception ex)
            {
                var errorMsg = $"Perhaps you're missing registering your provider at the start of the application."
                    + Environment.NewLine
                    + @" E.g., DbProviderFactories.RegisterFactory(""Microsoft.Data.SqlClient"", Microsoft.Data.SqlClient.SqlClientFactory.Instance);"
                    + Environment.NewLine
                    + @" Note: The above example should be done only once during the lifetime of the application."
                    + Environment.NewLine + Environment.NewLine
                    + $@" Inner exception: {ex.Message}";
                ;
                throw new Exception(errorMsg, ex);
            }
        }
        /// <summary>
        /// Ensure the connection is open, and if not, opens it while ensuring that the connection is not in executing state.
        /// Otherwise waits for the connection to finish executing, then opens it.
        /// </summary>
        /// <param name="conn"></param>
        /// <param name="cToken"></param>
        /// <returns></returns>
        /// <exception cref="ArgumentNullException"></exception>
        public static async Task EnsureOpenAsync(this DbConnection conn, CancellationToken cToken = default)
        {
            ArgumentNullException.ThrowIfNull(conn);
            if (conn.State != System.Data.ConnectionState.Open)
            {
                // if the connection is in executing state, wait for it to finish
                while (conn.State == System.Data.ConnectionState.Executing
                    || conn.State == System.Data.ConnectionState.Fetching
                    || conn.State == System.Data.ConnectionState.Connecting
                    )
                {
                    await Task.Delay(100, cToken);
                }

                // check if cToken is cancelled
                cToken.ThrowIfCancellationRequested();

                await conn.OpenAsync(cToken);
            }
        }

        /// <summary>
        /// Ensures the connection is closed, and if not, closes it while ensuring that the connection is not in executing state.
        /// Otherwise waits for the connection to finish executing, then closes it.
        /// </summary>
        /// <param name="conn"></param>
        /// <param name="cToken"></param>
        /// <returns></returns>
        /// <exception cref="ArgumentNullException"></exception>
        public static async Task EnsureClosedAsync(this DbConnection conn, CancellationToken cToken = default)
        {
            ArgumentNullException.ThrowIfNull(conn);
            if (conn.State != System.Data.ConnectionState.Closed)
            {
                // if the connection is in executing state, wait for it to finish
                while (conn.State == System.Data.ConnectionState.Executing
                    || conn.State == System.Data.ConnectionState.Fetching
                    || conn.State == System.Data.ConnectionState.Connecting)
                {
                    await Task.Delay(100, cToken);
                }

                // check if cToken is cancelled
                cToken.ThrowIfCancellationRequested();

                await conn.CloseAsync();
            }
        }

        private static async Task EnsureClosedAsync(this DbDataReader reader)
        {
            if (reader == null) return;
            if (reader.IsClosed) return;
            await reader.CloseAsync();
        }

        public static IDictionary<string, object?>? ToDictionary(this DbParameterCollection? dbParameterCollection)
        {
            if (dbParameterCollection == null) return null;
            var result = new Dictionary<string, object?>();
            foreach (DbParameter item in dbParameterCollection)
            {
                result.Add(item.ParameterName, item.Value);
            }
            return result;
        }


        #region implementation

        #region main implementation
        
        /// <summary>
        /// Main implementation that executes queries and returns resource-safe DbAsyncQueryResult.
        /// This is the core method used by all ExecuteQueryAsync extension methods.
        /// </summary>
        internal static async Task<DbAsyncQueryResult<dynamic>> ExecuteQueryAsyncMain(
        this DbCommand dbc,
        string query,
        IEnumerable<DbQueryParams>? queryParamList = null,
        bool closeConnectionOnExit = false,
        CancellationToken cToken = default
        )
        {
            ArgumentNullException.ThrowIfNull(dbc);
            if (string.IsNullOrEmpty(query)) throw new ArgumentNullException(nameof(query));
            var conn = dbc.Connection ?? throw new Exception("DbCommand.Connection is null");

            #region get data types from the query
            var dataTypeList = DefaultDataTypeRegexCompiled.Matches(query)
                .Cast<Match>()
                .Reverse()
                .Select(x => new
                {
                    ParamName = x.Groups["param"].Value,
                    Type = x.Groups["type"].Value,
                    OpenMarker = x.Groups["open_marker"].Value,
                    CloseMarker = x.Groups["close_marker"].Value
                })
                .Where(x => !string.IsNullOrEmpty(x.ParamName)
                    && !string.IsNullOrEmpty(x.Type));
            
            bool hasDataTypes = dataTypeList.Any();

            Dictionary<string, dynamic> dataTypeDict = null!;
            if (hasDataTypes)
            {
                dataTypeDict = [];
                foreach (var item in dataTypeList)
                {
                    dataTypeDict.TryAdd(item.ParamName, item);
                }
            }

            // iterate through the data types found in the query
            // and replace the data type placeholders with the `ParamName` value
            foreach (var item in dataTypeList)
            {
                query = query.Replace(
                    item.OpenMarker
                    + item.Type + "{"
                    + item.ParamName
                    + item.CloseMarker
                    , item.ParamName,
                    StringComparison.OrdinalIgnoreCase
                    );
            }
            #endregion

            DbDataReader reader;
            DbCommand command = dbc;
            
            await conn.EnsureOpenAsync(cToken);
            cToken.ThrowIfCancellationRequested();

            // Process parameters
            if (queryParamList is not null)
            {
                int count = 0;
                foreach (var queryParam in queryParamList.ReduceToUnique(true))
                {
                    count++;
                    var dataModelProperties = queryParam.DataModel?.GetDataModelParameters(true);
                    var matchingQueryVars =
                        Regex.Matches(query, queryParam.QueryParamsRegex)
                        .Cast<Match>()
                        .Select(x => new
                        {
                            Name = x.Groups["param"].Value,
                            OpenMarker = x.Groups["open_marker"].Value,
                            CloseMarker = x.Groups["close_marker"].Value
                        })
                        .Where(x => !string.IsNullOrEmpty(x.Name))
                        .Distinct().ToList();

                    foreach (var matchingQueryVar in matchingQueryVars)
                    {
                        object? matchingDataModelPropertyValue = null;
                        dataModelProperties?.TryGetValue(matchingQueryVar.Name, out matchingDataModelPropertyValue);

                        var sqlParamName = DefaultParameterTemplate.Replace("{{DefaultParameterPrefix}}", DefaultParameterPrefix)
                            .Replace("{{ParameterCount}}", count.ToString(CultureInfo.InvariantCulture))
                            .Replace("{{ParameterName}}", _cleanVariableNamesRegexCompiled.Replace(matchingQueryVar.Name, "_"));

                        query = query.Replace(
                                    matchingQueryVar.OpenMarker
                                    + matchingQueryVar.Name
                                    + matchingQueryVar.CloseMarker
                                    , sqlParamName,
                                    StringComparison.OrdinalIgnoreCase
                                    );

                        var p = command.CreateParameter();
                        p.ParameterName = sqlParamName;
                        p.Value = matchingDataModelPropertyValue ?? DBNull.Value;
                        command.Parameters.Add(p);
                    }
                }
            }

            command.CommandText = query;
            reader = await command.ExecuteReaderAsync(cToken);
            cToken.ThrowIfCancellationRequested();

            var results = CreateAsyncEnumerableFromReader(reader, hasDataTypes, dataTypeDict, cToken);
            return new DbAsyncQueryResult<dynamic>(results, reader, conn, closeConnectionOnExit);
        }

        internal static async IAsyncEnumerable<dynamic> CreateAsyncEnumerableFromReader(
            DbDataReader reader, 
            bool hasDataTypes, 
            Dictionary<string, dynamic>? dataTypeDict,
            [EnumeratorCancellation] CancellationToken cToken = default)
        {
            if (reader?.HasRows == true)
            {
                bool cont = true;
                while (cont)
                {
                    cont = await reader.ReadAsync(cToken);
                    cToken.ThrowIfCancellationRequested();
                    if (!cont) break;

                    ExpandoObject result = new();

                    if (reader.FieldCount == 1
                        // check if there is no column name
                        && string.IsNullOrEmpty(reader.GetName(0)))

                    {
                        var value = reader.GetValue(0);
                        if (value is DBNull) yield return null!;
                        yield return value;

                    }


                    foreach (var item in Enumerable.Range(0, reader.FieldCount)
                            .Select(x => new { Name = reader.GetName(x), Value = reader.GetValue(x) }))
                    {
                        // ensure null of the correct type is returned
                        if (item.Value is DBNull)
                        {
                            result.TryAdd(item.Name, null);
                        }
                        else
                        {
                            if (hasDataTypes 
                                && dataTypeDict != null
                                && dataTypeDict.TryGetValue(item.Name, out dynamic? value)
                                && !string.IsNullOrEmpty(item.Value as string)
                                )
                            {
                                
                                switch (value.Type)
                                {
                                    case "json":
                                        result.TryAdd(item.Name, (object)(item.Value as string)!.ParseJson());
                                        break;
                                    case "xml":
                                        result.TryAdd(item.Name, (object) (item.Value as string)!.ParseXml());
                                        break;
                                    default:
                                        result.TryAdd(item.Name, item.Value);
                                        break;
                                }
                            }
                            else
                                result.TryAdd(item.Name, item.Value);
                        }
                    }

                    yield return result;
                }
            }
        }




        #endregion

        #region DbCommand

        /// <summary>
        /// Executes a query and returns an async result with automatic resource management.
        /// Can be used directly where IAsyncEnumerable&lt;dynamic&gt; is expected.
        /// Must be disposed (use 'using' statement).
        /// </summary>
        public static async Task<DbAsyncQueryResult<dynamic>> ExecuteQueryAsync(
            this DbCommand dbc,
            string query,
            object? queryParams = null,
            string queryParamsRegex = @"(?<open_marker>\{\{)(?<param>.*?)?(?<close_marker>\}\})",
            bool closeConnectionOnExit = false,
            CancellationToken cToken = default
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

            return await ExecuteQueryAsyncMain(
                dbc, query, (IEnumerable<DbQueryParams>?)queryParams, closeConnectionOnExit, cToken);
        }

        /// <summary>
        /// Executes a query and returns a sync result with automatic resource management.
        /// Can be used directly where IEnumerable&lt;dynamic&gt; is expected.
        /// Must be disposed (use 'using' statement).
        /// </summary>
        public static DbQueryResult<dynamic> ExecuteQuery(
            this DbCommand dbc,
            string query,
            object? queryParams = null,
            string queryParamsRegex = @"(?<open_marker>\{\{)(?<param>.*?)?(?<close_marker>\}\})",
            bool closeConnectionOnExit = false,
            CancellationToken cToken = default
            )
        {
            var asyncResult = ExecuteQueryAsync(dbc, query, queryParams, queryParamsRegex, closeConnectionOnExit, cToken)
                .GetAwaiter().GetResult();
            
            return new DbQueryResult<dynamic>(asyncResult.AsAsyncEnumerable(), asyncResult.Reader, asyncResult.Connection, closeConnectionOnExit);
        }

        public static async Task ExecuteCommandAsync(
            this DbCommand dbc,
            string query,
            object? queryParams = null,
            string queryParamsRegex = @"(?<open_marker>\{\{)(?<param>.*?)?(?<close_marker>\}\})",
            bool closeConnectionOnExit = false,
            CancellationToken cToken = default
            )
        {
            var result = await ExecuteQueryAsync(dbc, query, queryParams, queryParamsRegex, closeConnectionOnExit, cToken);
            await foreach (var _ in result) ; // Consume the enumerable to execute the command
        }

        public static void ExecuteCommand(
            this DbCommand dbc,
            string query,
            object? queryParams = null,
            string queryParamsRegex = @"(?<open_marker>\{\{)(?<param>.*?)?(?<close_marker>\}\})",
            bool closeConnectionOnExit = false,
            CancellationToken cToken = default
            )
        {
            ExecuteCommandAsync(dbc, query, queryParams, queryParamsRegex, closeConnectionOnExit, cToken)
                .GetAwaiter().GetResult();
        }

        /// <summary>
        /// Executes a query and returns an async result with typed objects and automatic resource management.
        /// Can be used directly where IAsyncEnumerable&lt;T&gt; is expected.
        /// Automatically disposes resources when goes out of scope (via GC), or when used in a 'using' statement, or directly disposed via DisposeAsync().
        /// </summary>
        public static async Task<DbAsyncQueryResult<T>> ExecuteQueryAsync<T>(
            this DbCommand dbc,
            string query,
            object? queryParams = null,
            string queryParamsRegex = @"(?<open_marker>\{\{)(?<param>.*?)?(?<close_marker>\}\})",
            bool closeConnectionOnExit = false,
            CancellationToken cToken = default
            )
        {
            var dynamicResult = await ExecuteQueryAsync(dbc, query, queryParams, queryParamsRegex, closeConnectionOnExit, cToken);
            var typedAsyncEnumerable = ConvertToType<T>(dynamicResult.AsAsyncEnumerable());
            
            return new DbAsyncQueryResult<T>(
                typedAsyncEnumerable,
                dynamicResult.Reader,
                dynamicResult.Connection,
                closeConnectionOnExit
            );
        }

        /// <summary>
        /// Executes a query and returns a sync result with typed objects and automatic resource management.
        /// Can be used directly where IEnumerable&lt;T&gt; is expected.
        /// Automatically disposes resources when goes out of scope (via GC), or when used in a 'using' statement, or directly disposed via Dispose().
        /// </summary>
        public static DbQueryResult<T> ExecuteQuery<T>(
            this DbCommand dbc,
            string query,
            object? queryParams = null,
            string queryParamsRegex = @"(?<open_marker>\{\{)(?<param>.*?)?(?<close_marker>\}\})",
            bool closeConnectionOnExit = false,
            CancellationToken cToken = default
            )
        {
            var asyncResult = ExecuteQueryAsync<T>(dbc, query, queryParams, queryParamsRegex, closeConnectionOnExit, cToken)
                .GetAwaiter().GetResult();
            
            return new DbQueryResult<T>(asyncResult.AsAsyncEnumerable(), asyncResult.Reader, asyncResult.Connection, closeConnectionOnExit);
        }



        #endregion

        #region DbConnection

        /// <summary>
        /// Executes a query and returns an async result with automatic resource management.
        /// Can be used directly where IAsyncEnumerable&lt;dynamic&gt; is expected.
        /// Must be disposed (use 'using' statement).
        /// </summary>
        public static async Task<DbAsyncQueryResult<dynamic>> ExecuteQueryAsync(
            this DbConnection con,
            string query,
            object? queryParams = null,
            string queryParamsRegex = @"(?<open_marker>\{\{)(?<param>.*?)?(?<close_marker>\}\})",
            int? commandTimeout = null,
            bool closeConnectionOnExit = false,
            CancellationToken cToken = default
            )
        {
            using (DbCommand dbc = con.CreateCommand())
            {
                if (commandTimeout.HasValue)
                    dbc.CommandTimeout = commandTimeout.Value;
                return await ExecuteQueryAsync(dbc, query, queryParams, queryParamsRegex, closeConnectionOnExit, cToken);
            }
        }

        /// <summary>
        /// Executes a query and returns a sync result with automatic resource management.
        /// Can be used directly where IEnumerable&lt;dynamic&gt; is expected.
        /// Automatically disposes resources when goes out of scope (via GC), or when used in a 'using' statement, or directly disposed via Dispose().
        /// </summary>
        public static DbQueryResult<dynamic> ExecuteQuery(
            this DbConnection con,
            string query,
            object? queryParams = null,
            string queryParamsRegex = @"(?<open_marker>\{\{)(?<param>.*?)?(?<close_marker>\}\})",
            int? commandTimeout = null,
            bool closeConnectionOnExit = false,
            CancellationToken cToken = default
            )
        {
            var asyncResult = ExecuteQueryAsync(con, query, queryParams, queryParamsRegex, commandTimeout, closeConnectionOnExit, cToken)
                .GetAwaiter().GetResult();
            
            return new DbQueryResult<dynamic>(asyncResult.AsAsyncEnumerable(), asyncResult.Reader, asyncResult.Connection, closeConnectionOnExit);
        }

        public static async Task ExecuteCommandAsync(
            this DbConnection con,
            string query,
            object? queryParams = null,
            string queryParamsRegex = @"(?<open_marker>\{\{)(?<param>.*?)?(?<close_marker>\}\})",
            int? commandTimeout = null,
            bool closeConnectionOnExit = false,
            CancellationToken cToken = default
            )
        {
            var result = await ExecuteQueryAsync(con, query, queryParams, queryParamsRegex, commandTimeout, closeConnectionOnExit, cToken);
            await foreach (var _ in result) ; // Consume the enumerable to execute the command
        }

        public static void ExecuteCommand(
            this DbConnection con,
            string query,
            object? queryParams = null,
            string queryParamsRegex = @"(?<open_marker>\{\{)(?<param>.*?)?(?<close_marker>\}\})",
            int? commandTimeout = null,
            bool closeConnectionOnExit = false,
            CancellationToken cToken = default
            )
        {
            ExecuteCommandAsync(con, query, queryParams, queryParamsRegex, commandTimeout, closeConnectionOnExit, cToken)
                .GetAwaiter().GetResult();
        }

        /// <summary>
        /// Executes a query and returns an async result with typed objects and automatic resource management.
        /// Can be used directly where IAsyncEnumerable&lt;T&gt; is expected.
        /// </summary>
        public static async Task<DbAsyncQueryResult<T>> ExecuteQueryAsync<T>(
            this DbConnection con,
            string query,
            object? queryParams = null,
            string queryParamsRegex = @"(?<open_marker>\{\{)(?<param>.*?)?(?<close_marker>\}\})",
            int? commandTimeout = null,
            bool closeConnectionOnExit = false,
            CancellationToken cToken = default
            )
        {
            using (DbCommand dbc = con.CreateCommand())
            {
                if (commandTimeout.HasValue)
                    dbc.CommandTimeout = commandTimeout.Value;
                return await ExecuteQueryAsync<T>(dbc, query, queryParams, queryParamsRegex, closeConnectionOnExit, cToken);
            }
        }

        /// <summary>
        /// Executes a query and returns a sync result with typed objects and automatic resource management.
        /// Can be used directly where IEnumerable&lt;T&gt; is expected.
        /// Must be disposed (use 'using' statement).
        /// </summary>
        public static DbQueryResult<T> ExecuteQuery<T>(
            this DbConnection con,
            string query,
            object? queryParams = null,
            string queryParamsRegex = @"(?<open_marker>\{\{)(?<param>.*?)?(?<close_marker>\}\})",
            int? commandTimeout = null,
            bool closeConnectionOnExit = false,
            CancellationToken cToken = default
            )
        {
            var asyncResult = ExecuteQueryAsync<T>(con, query, queryParams, queryParamsRegex, commandTimeout, closeConnectionOnExit, cToken)
                .GetAwaiter().GetResult();
            
            return new DbQueryResult<T>(asyncResult.AsAsyncEnumerable(), asyncResult.Reader, asyncResult.Connection, closeConnectionOnExit);
        }
        #endregion

        #region connection string

        /// <summary>
        /// Executes a query and returns an async result with automatic resource management.
        /// Can be used directly where IAsyncEnumerable&lt;dynamic&gt; is expected.
        /// Must be disposed (use 'using' statement). Connection will be automatically closed when disposed.
        /// </summary>
        public static async Task<DbAsyncQueryResult<dynamic>> ExecuteQueryAsync(
            this string connectionString,
            string query,
            object? queryParams = null,
            string queryParamsRegex = @"(?<open_marker>\{\{)(?<param>.*?)?(?<close_marker>\}\})",
            int? commandTimeout = null,
            bool closeConnectionOnExit = true, // Default to true for connection string since we're creating the connection
            CancellationToken cToken = default
            )
        {
            using (DbConnection con = CreateDbConnection(connectionString))
            {
                using (DbCommand dbc = con.CreateCommand())
                {
                    if (commandTimeout.HasValue)
                        dbc.CommandTimeout = commandTimeout.Value;
                    return await ExecuteQueryAsync(dbc, query, queryParams, queryParamsRegex, closeConnectionOnExit, cToken);
                }
            }
        }

        /// <summary>
        /// Executes a query and returns a sync result with automatic resource management.
        /// Can be used directly where IEnumerable&lt;dynamic&gt; is expected.
        /// Must be disposed (use 'using' statement). Connection will be automatically closed when disposed.
        /// </summary>
        public static DbQueryResult<dynamic> ExecuteQuery(
            this string connectionString,
            string query,
            object? queryParams = null,
            string queryParamsRegex = @"(?<open_marker>\{\{)(?<param>.*?)?(?<close_marker>\}\})",
            int? commandTimeout = null,
            bool closeConnectionOnExit = true, // Default to true for connection string since we're creating the connection
            CancellationToken cToken = default
            )
        {
            var asyncResult = ExecuteQueryAsync(connectionString, query, queryParams, queryParamsRegex, commandTimeout, closeConnectionOnExit, cToken)
                .GetAwaiter().GetResult();
            
            return new DbQueryResult<dynamic>(asyncResult.AsAsyncEnumerable(), asyncResult.Reader, asyncResult.Connection, closeConnectionOnExit);
        }

        public static async Task ExecuteCommandAsync(
            this string connectionString,
            string query,
            object? queryParams = null,
            string queryParamsRegex = @"(?<open_marker>\{\{)(?<param>.*?)?(?<close_marker>\}\})",
            int? commandTimeout = null,
            bool closeConnectionOnExit = true,
            CancellationToken cToken = default
            )
        {
            var result = await ExecuteQueryAsync(connectionString, query, queryParams, queryParamsRegex, commandTimeout, closeConnectionOnExit, cToken);
            await foreach (var _ in result) ; // Consume the enumerable to execute the command
        }

        public static void ExecuteCommand(
            this string connectionString,
            string query,
            object? queryParams = null,
            string queryParamsRegex = @"(?<open_marker>\{\{)(?<param>.*?)?(?<close_marker>\}\})",
            int? commandTimeout = null,
            bool closeConnectionOnExit = true,
            CancellationToken cToken = default
            )
        {
            ExecuteCommandAsync(connectionString, query, queryParams, queryParamsRegex, commandTimeout, closeConnectionOnExit, cToken)
                .GetAwaiter().GetResult();
        }

        /// <summary>
        /// Executes a query and returns an async result with typed objects and automatic resource management.
        /// Can be used directly where IAsyncEnumerable&lt;T&gt; is expected.
        /// Must be disposed (use 'using' statement). Connection will be automatically closed when disposed.
        /// </summary>
        public static async Task<DbAsyncQueryResult<T>> ExecuteQueryAsync<T>(
            this string connectionString,
            string query,
            object? queryParams = null,
            string queryParamsRegex = @"(?<open_marker>\{\{)(?<param>.*?)?(?<close_marker>\}\})",
            int? commandTimeout = null,
            bool closeConnectionOnExit = true, // Default to true for connection string since we're creating the connection
            CancellationToken cToken = default
            )
        {
            using (DbConnection con = CreateDbConnection(connectionString))
            {
                using (DbCommand dbc = con.CreateCommand())
                {
                    if (commandTimeout.HasValue)
                        dbc.CommandTimeout = commandTimeout.Value;
                    return await ExecuteQueryAsync<T>(dbc, query, queryParams, queryParamsRegex, closeConnectionOnExit, cToken);
                }
            }
        }

        /// <summary>
        /// Executes a query and returns a sync result with typed objects and automatic resource management.
        /// Can be used directly where IEnumerable&lt;T&gt; is expected.
        /// Must be disposed (use 'using' statement). Connection will be automatically closed when disposed.
        /// </summary>
        public static DbQueryResult<T> ExecuteQuery<T>(
            this string connectionString,
            string query,
            object? queryParams = null,
            string queryParamsRegex = @"(?<open_marker>\{\{)(?<param>.*?)?(?<close_marker>\}\})",
            int? commandTimeout = null,
            bool closeConnectionOnExit = true, // Default to true for connection string since we're creating the connection
            CancellationToken cToken = default
            )
        {
            var asyncResult = ExecuteQueryAsync<T>(connectionString, query, queryParams, queryParamsRegex, commandTimeout, closeConnectionOnExit, cToken)
                .GetAwaiter().GetResult();
            
            return new DbQueryResult<T>(asyncResult.AsAsyncEnumerable(), asyncResult.Reader, asyncResult.Connection, closeConnectionOnExit);
        }

        #endregion

        #endregion






        /// <summary>
        /// Converts dynamic async enumerable to typed async enumerable
        /// </summary>
        internal static async IAsyncEnumerable<T> ConvertToType<T>(IAsyncEnumerable<dynamic> source)
        {
            await foreach (var item in source)
            {
                var converted = _mapper.Map<T>(item);
                if (converted is not null)
                    yield return converted;
            }
        }

        #region embedded extensions imported from Com.H.x packages



        #region exception messaging

        private static string GenerateError(
            this Exception ex,
            DbCommand? command,
            string query,
            IDictionary<string, object?>? queryParams
            )
        {
            string errMsg = "Error executing query:";

            if (command is not null)
                errMsg +=
                $"{Environment.NewLine}-----------{Environment.NewLine}"
                + $"Parameters:{Environment.NewLine}"
                + string.Join(Environment.NewLine,
                command.Parameters.Cast<DbParameter>().Select(x => $"{x.ParameterName} = {x.Value}"));
            else if (queryParams is not null)
                errMsg +=
                $"{Environment.NewLine}-----------{Environment.NewLine}"
                + $"Parameters:{Environment.NewLine}"
                + string.Join(Environment.NewLine,
                queryParams.Select(x => $"{x.Key} = {x.Value}"));

            if (command is not null)
                errMsg += $"{Environment.NewLine}-----{Environment.NewLine}Query{Environment.NewLine}"
                + command.CommandText + $"{Environment.NewLine}-------{Environment.NewLine}";
            else if (query is not null)
                errMsg += $"{Environment.NewLine}-----{Environment.NewLine}Query{Environment.NewLine}"
                + query + $"{Environment.NewLine}-------{Environment.NewLine}";

            errMsg += $"Error msg:{Environment.NewLine}"
                + ex.Message;
            return errMsg;

        }
        #endregion


        #endregion


    }
}
