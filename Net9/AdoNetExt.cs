using System.Collections.Concurrent;
using System.Data;
using System.Data.Common;
using System.Dynamic;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;


namespace Com.H.Data.Common
{

    /// <summary>
    /// Provides ADO.NET extension methods for executing database queries with dynamic results.
    /// Supports parameterized queries with flexible delimiter patterns, nested JSON/XML parsing,
    /// and automatic resource management through DbQueryResult and DbAsyncQueryResult wrappers.
    /// </summary>
    public static class AdoNetExt
    {
        /// <summary>
        /// Gets or sets the default parameter prefix used as a fallback for unrecognized database providers.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The library automatically detects the parameter prefix based on the <see cref="System.Data.Common.DbConnection"/> type.
        /// This property is only used when the provider type is not recognized by <see cref="GetParameterPrefix"/>.
        /// </para>
        /// <para>
        /// Default value is "@" which works for most databases (SQL Server, PostgreSQL, MySQL, SQLite, etc.).
        /// </para>
        /// </remarks>
        /// <example>
        /// Override for a custom or unrecognized provider:
        /// <code>
        /// Com.H.Data.Common.AdoNetExt.DefaultParameterPrefix = ":";
        /// </code>
        /// </example>
        public static string DefaultParameterPrefix { get; set; } = "@";
        
        /// <summary>
        /// Gets or sets the template used for generating unique parameter names in prepared statements.
        /// Supports placeholders: {{DefaultParameterPrefix}}, {{ParameterCount}}, {{ParameterName}}.
        /// </summary>
        public static string DefaultParameterTemplate { get; set; } = "{{DefaultParameterPrefix}}vxv_{{ParameterCount}}_{{ParameterName}}";

        /// <summary>
        /// Cache for parameter prefixes by connection type.
        /// Key is the connection Type since parameter prefix is determined by ADO.NET provider, not connection instance.
        /// </summary>
        private static readonly ConcurrentDictionary<Type, string> _prefixCache = new();

        /// <summary>
        /// Gets the SQL parameter prefix for the specified database connection.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The prefix is automatically detected based on the connection type and cached for performance.
        /// After the first lookup for a provider type, subsequent calls are simple dictionary lookups.
        /// </para>
        /// <para>
        /// Common prefixes:
        /// <list type="bullet">
        /// <item><description>'@' for SQL Server, PostgreSQL (Npgsql), MySQL, SQLite, DB2, Firebird</description></item>
        /// <item><description>':' for Oracle</description></item>
        /// <item><description>'?' for ODBC, OleDb (positional parameters)</description></item>
        /// </list>
        /// </para>
        /// <para>
        /// If the provider type is not recognized, falls back to <see cref="DefaultParameterPrefix"/>.
        /// </para>
        /// </remarks>
        /// <param name="connection">The database connection.</param>
        /// <returns>The parameter prefix string (e.g., "@", ":", "?").</returns>
        /// <exception cref="ArgumentNullException">Thrown when connection is null.</exception>
        public static string GetParameterPrefix(DbConnection connection)
        {
            ArgumentNullException.ThrowIfNull(connection);

            return _prefixCache.GetOrAdd(
                connection.GetType(),
                type => InferPrefixFromType(type) ?? DefaultParameterPrefix
            );
        }

        /// <summary>
        /// Infers the SQL parameter prefix from the connection type's full name.
        /// </summary>
        /// <param name="connectionType">The type of the database connection.</param>
        /// <returns>The inferred parameter prefix, or null if the provider is not recognized.</returns>
        private static string? InferPrefixFromType(Type connectionType)
        {
            var typeName = connectionType.FullName ?? "";

            // Oracle uses ':'
            if (typeName.Contains("Oracle", StringComparison.OrdinalIgnoreCase))
                return ":";

            // PostgreSQL (Npgsql) uses '@'
            if (typeName.Contains("Npgsql", StringComparison.OrdinalIgnoreCase))
                return "@";

            // MySQL uses '@'
            if (typeName.Contains("MySql", StringComparison.OrdinalIgnoreCase))
                return "@";

            // SQLite uses '@'
            if (typeName.Contains("Sqlite", StringComparison.OrdinalIgnoreCase))
                return "@";

            // SQL Server uses '@' (covers Microsoft.Data.SqlClient and System.Data.SqlClient)
            if (typeName.Contains("SqlConnection", StringComparison.OrdinalIgnoreCase))
                return "@";

            // ODBC uses positional '?'
            if (typeName.Contains("Odbc", StringComparison.OrdinalIgnoreCase))
                return "?";

            // OleDb uses positional '?'
            if (typeName.Contains("OleDb", StringComparison.OrdinalIgnoreCase))
                return "?";

            // DB2 uses '@'
            if (typeName.Contains("DB2", StringComparison.OrdinalIgnoreCase))
                return "@";

            // Firebird uses '@'
            if (typeName.Contains("Firebird", StringComparison.OrdinalIgnoreCase) ||
                typeName.Contains("FbConnection", StringComparison.OrdinalIgnoreCase))
                return "@";

            return null; // Unknown provider - will fall back to DefaultParameterPrefix
        }

        private readonly static string _cleanVariableNamesRegex = @"[-\s\.\(\)\[\]\{\}\:\;\,\?\!\#\$\%\^\&\*\+\=\|\\\/\~\`\´\'\""\<\>\=\?\ ]";
        private static readonly Regex _cleanVariableNamesRegexCompiled = new(_cleanVariableNamesRegex, RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline);

        private static string defaultDataTypeRegex = @"(?<open_marker>\{type\{)(?<type>.*?)\{(?<param>.*?)?(?<close_marker>\}\}\})";
        
        /// <summary>
        /// Gets or sets the regex pattern for parsing data type hints in column names.
        /// Used to automatically parse JSON or XML data returned from queries.
        /// Default pattern: {type{json{columnName}}} or {type{xml{columnName}}}
        /// </summary>
        /// <example>
        /// Usage in SQL query:
        /// <code>
        /// SELECT name, (SELECT * FROM orders FOR JSON PATH) AS {type{json{orders}}}
        /// </code>
        /// </example>
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

        /// <summary>
        /// Creates a DbConnection from a connection string and provider name.
        /// Automatically registers and instantiates the appropriate database provider.
        /// </summary>
        /// <param name="connStr">The database connection string</param>
        /// <param name="providerName">The database provider name (e.g., "Microsoft.Data.SqlClient"). Can also be specified in the connection string with "Provider=" key.</param>
        /// <returns>A new DbConnection instance</returns>
        /// <exception cref="ArgumentNullException">Thrown when provider name is null or empty</exception>
        /// <exception cref="Exception">Thrown when provider factory cannot be created or provider is not registered</exception>
        /// <example>
        /// <code>
        /// var conn = "Server=myserver;Database=mydb;".CreateDbConnection("Microsoft.Data.SqlClient");
        /// </code>
        /// </example>
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
        /// Ensures the database connection is open. If closed, opens it asynchronously.
        /// Waits for any ongoing operations (Executing, Fetching, Connecting) to complete before opening.
        /// </summary>
        /// <param name="conn">The database connection to ensure is open</param>
        /// <param name="cToken">Cancellation token for async operation</param>
        /// <returns>A task representing the asynchronous operation</returns>
        /// <exception cref="ArgumentNullException">Thrown when connection is null</exception>
        /// <exception cref="OperationCanceledException">Thrown when cancellation is requested</exception>
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
        /// Ensures the database connection is closed. If open, closes it asynchronously.
        /// Waits for any ongoing operations (Executing, Fetching, Connecting) to complete before closing.
        /// </summary>
        /// <param name="conn">The database connection to ensure is closed</param>
        /// <param name="cToken">Cancellation token for async operation</param>
        /// <returns>A task representing the asynchronous operation</returns>
        /// <exception cref="ArgumentNullException">Thrown when connection is null</exception>
        /// <exception cref="OperationCanceledException">Thrown when cancellation is requested</exception>
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

        /// <summary>
        /// Converts a DbParameterCollection to a dictionary for easier parameter inspection and debugging.
        /// </summary>
        /// <param name="dbParameterCollection">The parameter collection to convert</param>
        /// <returns>A dictionary with parameter names as keys and parameter values as values, or null if collection is null</returns>
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
                // Get the parameter prefix for this connection type (cached after first lookup)
                var parameterPrefix = GetParameterPrefix(conn);
                var nullParams = new List<(string varName, string sqlParamName)>();
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

                        var sqlParamName = DefaultParameterTemplate.Replace("{{DefaultParameterPrefix}}", parameterPrefix)
                            .Replace("{{ParameterCount}}", count.ToString(CultureInfo.InvariantCulture))
                            .Replace("{{ParameterName}}", _cleanVariableNamesRegexCompiled.Replace(matchingQueryVar.Name, "_"));

                        if (matchingDataModelPropertyValue is null)
                        {
                            nullParams.Add((matchingQueryVar.OpenMarker + matchingQueryVar.Name + matchingQueryVar.CloseMarker, sqlParamName));
                            continue;
                        }

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

                foreach (var nullParam in nullParams)
                {
                    query = query.Replace(
                                nullParam.varName
                                , nullParam.sqlParamName,
                                StringComparison.OrdinalIgnoreCase
                                );
                    var p = command.CreateParameter();
                    p.ParameterName = nullParam.sqlParamName;
                    p.Value = DBNull.Value;
                    command.Parameters.Add(p);
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
                                        result.TryAdd(item.Name, (object)(item.Value as string)!.ParseXml());
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
        /// Executes a SQL query asynchronously and returns a disposable result that implements IAsyncEnumerable&lt;dynamic&gt;.
        /// Supports parameterized queries with flexible delimiters (default: {{ }}), automatic JSON/XML parsing,
        /// and proper resource management. Must be disposed using 'await using' or 'using' statement.
        /// </summary>
        /// <param name="dbc">The database command to execute</param>
        /// <param name="query">SQL query text. Use {{paramName}} syntax for parameters.</param>
        /// <param name="queryParams">Parameters object (anonymous object, Dictionary, JsonElement, JSON string, or custom object with matching properties)</param>
        /// <param name="queryParamsRegex">Regex pattern for parameter delimiters. Default: @"(?&lt;open_marker&gt;\{\{)(?&lt;param&gt;.*?)?(?&lt;close_marker&gt;\}\})" for {{ }} delimiters</param>
        /// <param name="closeConnectionOnExit">If true, closes the connection when the result is disposed</param>
        /// <param name="cToken">Cancellation token for async operation</param>
        /// <returns>A DbAsyncQueryResult that implements IAsyncEnumerable&lt;dynamic&gt; and IAsyncDisposable</returns>
        /// <exception cref="ArgumentNullException">Thrown when command or query is null</exception>
        /// <exception cref="Exception">Thrown when connection is null or query execution fails</exception>
        /// <example>
        /// <code>
        /// await using var result = await command.ExecuteQueryAsync(
        ///     "SELECT * FROM Users WHERE name = {{name}}", 
        ///     new { name = "John" });
        /// await foreach (var row in result)
        /// {
        ///     Console.WriteLine(row.name);
        /// }
        /// </code>
        /// </example>
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
        /// Executes a SQL query synchronously and returns a disposable result that implements IEnumerable&lt;dynamic&gt;.
        /// Supports parameterized queries with flexible delimiters (default: {{ }}), automatic JSON/XML parsing,
        /// and proper resource management. Must be disposed using 'using' statement.
        /// </summary>
        /// <param name="dbc">The database command to execute</param>
        /// <param name="query">SQL query text. Use {{paramName}} syntax for parameters.</param>
        /// <param name="queryParams">Parameters object (anonymous object, Dictionary, JsonElement, JSON string, or custom object with matching properties)</param>
        /// <param name="queryParamsRegex">Regex pattern for parameter delimiters. Default: @"(?&lt;open_marker&gt;\{\{)(?&lt;param&gt;.*?)?(?&lt;close_marker&gt;\}\})" for {{ }} delimiters</param>
        /// <param name="closeConnectionOnExit">If true, closes the connection when the result is disposed</param>
        /// <param name="cToken">Cancellation token for async operation</param>
        /// <returns>A DbQueryResult that implements IEnumerable&lt;dynamic&gt; and IDisposable</returns>
        /// <exception cref="ArgumentNullException">Thrown when command or query is null</exception>
        /// <exception cref="Exception">Thrown when connection is null or query execution fails</exception>
        /// <example>
        /// <code>
        /// using var result = command.ExecuteQuery(
        ///     "SELECT * FROM Users WHERE name = {{name}}", 
        ///     new { name = "John" });
        /// foreach (var row in result)
        /// {
        ///     Console.WriteLine(row.name);
        /// }
        /// </code>
        /// </example>
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

        /// <summary>
        /// Executes a non-query command asynchronously (INSERT, UPDATE, DELETE, etc.) without returning data.
        /// Consumes the entire result set to ensure the command executes completely.
        /// </summary>
        /// <param name="dbc">The database command to execute</param>
        /// <param name="query">SQL command text (INSERT, UPDATE, DELETE, etc.)</param>
        /// <param name="queryParams">Parameters object (anonymous object, Dictionary, JsonElement, JSON string, or custom object with matching properties)</param>
        /// <param name="queryParamsRegex">Regex pattern for parameter delimiters. Default: @"(?&lt;open_marker&gt;\{\{)(?&lt;param&gt;.*?)?(?&lt;close_marker&gt;\}\})" for {{ }} delimiters</param>
        /// <param name="closeConnectionOnExit">If true, closes the connection after execution</param>
        /// <param name="cToken">Cancellation token for async operation</param>
        /// <returns>A task representing the asynchronous operation</returns>
        /// <example>
        /// <code>
        /// await command.ExecuteCommandAsync(
        ///     "INSERT INTO Users (name, email) VALUES ({{name}}, {{email}})", 
        ///     new { name = "John", email = "john@example.com" });
        /// </code>
        /// </example>
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

        /// <summary>
        /// Executes a non-query command synchronously (INSERT, UPDATE, DELETE, etc.) without returning data.
        /// Consumes the entire result set to ensure the command executes completely.
        /// </summary>
        /// <param name="dbc">The database command to execute</param>
        /// <param name="query">SQL command text (INSERT, UPDATE, DELETE, etc.)</param>
        /// <param name="queryParams">Parameters object (anonymous object, Dictionary, JsonElement, JSON string, or custom object with matching properties)</param>
        /// <param name="queryParamsRegex">Regex pattern for parameter delimiters. Default: @"(?&lt;open_marker&gt;\{\{)(?&lt;param&gt;.*?)?(?&lt;close_marker&gt;\}\})" for {{ }} delimiters</param>
        /// <param name="closeConnectionOnExit">If true, closes the connection after execution</param>
        /// <param name="cToken">Cancellation token for async operation</param>
        /// <example>
        /// <code>
        /// command.ExecuteCommand(
        ///     "INSERT INTO Users (name, email) VALUES ({{name}}, {{email}})", 
        ///     new { name = "John", email = "john@example.com" });
        /// </code>
        /// </example>
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
        /// Executes a SQL query asynchronously and returns a typed result that implements IAsyncEnumerable&lt;T&gt;.
        /// Automatically maps query results to the specified type T using property name matching.
        /// Supports parameterized queries with flexible delimiters (default: {{ }}), automatic JSON/XML parsing,
        /// and proper resource management. Must be disposed using 'await using' or 'using' statement.
        /// </summary>
        /// <typeparam name="T">The type to map query results to. Can be a class with properties matching column names.</typeparam>
        /// <param name="dbc">The database command to execute</param>
        /// <param name="query">SQL query text. Use {{paramName}} syntax for parameters.</param>
        /// <param name="queryParams">Parameters object (anonymous object, Dictionary, JsonElement, JSON string, or custom object with matching properties)</param>
        /// <param name="queryParamsRegex">Regex pattern for parameter delimiters. Default: @"(?&lt;open_marker&gt;\{\{)(?&lt;param&gt;.*?)?(?&lt;close_marker&gt;\}\})" for {{ }} delimiters</param>
        /// <param name="closeConnectionOnExit">If true, closes the connection when the result is disposed</param>
        /// <param name="cToken">Cancellation token for async operation</param>
        /// <returns>A DbAsyncQueryResult&lt;T&gt; that implements IAsyncEnumerable&lt;T&gt; and IAsyncDisposable</returns>
        /// <exception cref="ArgumentNullException">Thrown when command or query is null</exception>
        /// <exception cref="Exception">Thrown when connection is null or query execution fails</exception>
        /// <example>
        /// <code>
        /// public class User { public int Id { get; set; } public string Name { get; set; } }
        /// 
        /// await using var result = await command.ExecuteQueryAsync&lt;User&gt;(
        ///     "SELECT id, name FROM Users WHERE id = {{id}}", 
        ///     new { id = 1 });
        /// await foreach (var user in result)
        /// {
        ///     Console.WriteLine(user.Name);
        /// }
        /// </code>
        /// </example>
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
        /// Executes a SQL query synchronously and returns a typed result that implements IEnumerable&lt;T&gt;.
        /// Automatically maps query results to the specified type T using property name matching.
        /// Supports parameterized queries with flexible delimiters (default: {{ }}), automatic JSON/XML parsing,
        /// and proper resource management. Must be disposed using 'using' statement.
        /// </summary>
        /// <typeparam name="T">The type to map query results to. Can be a class with properties matching column names.</typeparam>
        /// <param name="dbc">The database command to execute</param>
        /// <param name="query">SQL query text. Use {{paramName}} syntax for parameters.</param>
        /// <param name="queryParams">Parameters object (anonymous object, Dictionary, JsonElement, JSON string, or custom object with matching properties)</param>
        /// <param name="queryParamsRegex">Regex pattern for parameter delimiters. Default: @"(?&lt;open_marker&gt;\{\{)(?&lt;param&gt;.*?)?(?&lt;close_marker&gt;\}\})" for {{ }} delimiters</param>
        /// <param name="closeConnectionOnExit">If true, closes the connection when the result is disposed</param>
        /// <param name="cToken">Cancellation token for async operation</param>
        /// <returns>A DbQueryResult&lt;T&gt; that implements IEnumerable&lt;T&gt; and IDisposable</returns>
        /// <exception cref="ArgumentNullException">Thrown when command or query is null</exception>
        /// <exception cref="Exception">Thrown when connection is null or query execution fails</exception>
        /// <example>
        /// <code>
        /// public class User { public int Id { get; set; } public string Name { get; set; } }
        /// 
        /// using var result = command.ExecuteQuery&lt;User&gt;(
        ///     "SELECT id, name FROM Users WHERE id = {{id}}", 
        ///     new { id = 1 });
        /// foreach (var user in result)
        /// {
        ///     Console.WriteLine(user.Name);
        /// }
        /// </code>
        /// </example>
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
        /// Executes a SQL query asynchronously on a DbConnection and returns a disposable result that implements IAsyncEnumerable&lt;dynamic&gt;.
        /// Creates and manages a DbCommand internally. Supports parameterized queries with flexible delimiters (default: {{ }}),
        /// automatic JSON/XML parsing, and proper resource management. Must be disposed using 'await using' or 'using' statement.
        /// </summary>
        /// <param name="con">The database connection to execute the query on</param>
        /// <param name="query">SQL query text. Use {{paramName}} syntax for parameters.</param>
        /// <param name="queryParams">Parameters object (anonymous object, Dictionary, JsonElement, JSON string, or custom object with matching properties)</param>
        /// <param name="queryParamsRegex">Regex pattern for parameter delimiters. Default: @"(?&lt;open_marker&gt;\{\{)(?&lt;param&gt;.*?)?(?&lt;close_marker&gt;\}\})" for {{ }} delimiters</param>
        /// <param name="commandTimeout">Command timeout in seconds. If null, uses the default timeout.</param>
        /// <param name="closeConnectionOnExit">If true, closes the connection when the result is disposed</param>
        /// <param name="cToken">Cancellation token for async operation</param>
        /// <returns>A DbAsyncQueryResult that implements IAsyncEnumerable&lt;dynamic&gt; and IAsyncDisposable</returns>
        /// <exception cref="ArgumentNullException">Thrown when connection or query is null</exception>
        /// <exception cref="Exception">Thrown when query execution fails</exception>
        /// <example>
        /// <code>
        /// using var connection = new SqlConnection(connectionString);
        /// await using var result = await connection.ExecuteQueryAsync(
        ///     "SELECT * FROM Users WHERE age &gt; {{minAge}}", 
        ///     new { minAge = 18 });
        /// await foreach (var row in result)
        /// {
        ///     Console.WriteLine($"{row.name} - {row.age}");
        /// }
        /// </code>
        /// </example>
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
        /// Executes a SQL query synchronously on a DbConnection and returns a disposable result that implements IEnumerable&lt;dynamic&gt;.
        /// Creates and manages a DbCommand internally. Supports parameterized queries with flexible delimiters (default: {{ }}),
        /// automatic JSON/XML parsing, and proper resource management. Must be disposed using 'using' statement.
        /// </summary>
        /// <param name="con">The database connection to execute the query on</param>
        /// <param name="query">SQL query text. Use {{paramName}} syntax for parameters.</param>
        /// <param name="queryParams">Parameters object (anonymous object, Dictionary, JsonElement, JSON string, or custom object with matching properties)</param>
        /// <param name="queryParamsRegex">Regex pattern for parameter delimiters. Default: @"(?&lt;open_marker&gt;\{\{)(?&lt;param&gt;.*?)?(?&lt;close_marker&gt;\}\})" for {{ }} delimiters</param>
        /// <param name="commandTimeout">Command timeout in seconds. If null, uses the default timeout.</param>
        /// <param name="closeConnectionOnExit">If true, closes the connection when the result is disposed</param>
        /// <param name="cToken">Cancellation token for async operation</param>
        /// <returns>A DbQueryResult that implements IEnumerable&lt;dynamic&gt; and IDisposable</returns>
        /// <exception cref="ArgumentNullException">Thrown when connection or query is null</exception>
        /// <exception cref="Exception">Thrown when query execution fails</exception>
        /// <example>
        /// <code>
        /// using var connection = new SqlConnection(connectionString);
        /// using var result = connection.ExecuteQuery(
        ///     "SELECT * FROM Users WHERE age &gt; {{minAge}}", 
        ///     new { minAge = 18 });
        /// foreach (var row in result)
        /// {
        ///     Console.WriteLine($"{row.name} - {row.age}");
        /// }
        /// </code>
        /// </example>
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

        /// <summary>
        /// Executes a non-query command asynchronously on a DbConnection (INSERT, UPDATE, DELETE, etc.) without returning data.
        /// Creates and manages a DbCommand internally. Consumes the entire result set to ensure the command executes completely.
        /// </summary>
        /// <param name="con">The database connection to execute the command on</param>
        /// <param name="query">SQL command text (INSERT, UPDATE, DELETE, etc.)</param>
        /// <param name="queryParams">Parameters object (anonymous object, Dictionary, JsonElement, JSON string, or custom object with matching properties)</param>
        /// <param name="queryParamsRegex">Regex pattern for parameter delimiters. Default: @"(?&lt;open_marker&gt;\{\{)(?&lt;param&gt;.*?)?(?&lt;close_marker&gt;\}\})" for {{ }} delimiters</param>
        /// <param name="commandTimeout">Command timeout in seconds. If null, uses the default timeout.</param>
        /// <param name="closeConnectionOnExit">If true, closes the connection after execution</param>
        /// <param name="cToken">Cancellation token for async operation</param>
        /// <returns>A task representing the asynchronous operation</returns>
        /// <example>
        /// <code>
        /// using var connection = new SqlConnection(connectionString);
        /// await connection.ExecuteCommandAsync(
        ///     "UPDATE Users SET email = {{email}} WHERE id = {{id}}", 
        ///     new { id = 1, email = "newemail@example.com" });
        /// </code>
        /// </example>
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

        /// <summary>
        /// Executes a non-query command synchronously on a DbConnection (INSERT, UPDATE, DELETE, etc.) without returning data.
        /// Creates and manages a DbCommand internally. Consumes the entire result set to ensure the command executes completely.
        /// </summary>
        /// <param name="con">The database connection to execute the command on</param>
        /// <param name="query">SQL command text (INSERT, UPDATE, DELETE, etc.)</param>
        /// <param name="queryParams">Parameters object (anonymous object, Dictionary, JsonElement, JSON string, or custom object with matching properties)</param>
        /// <param name="queryParamsRegex">Regex pattern for parameter delimiters. Default: @"(?&lt;open_marker&gt;\{\{)(?&lt;param&gt;.*?)?(?&lt;close_marker&gt;\}\})" for {{ }} delimiters</param>
        /// <param name="commandTimeout">Command timeout in seconds. If null, uses the default timeout.</param>
        /// <param name="closeConnectionOnExit">If true, closes the connection after execution</param>
        /// <param name="cToken">Cancellation token for async operation</param>
        /// <example>
        /// <code>
        /// using var connection = new SqlConnection(connectionString);
        /// connection.ExecuteCommand(
        ///     "UPDATE Users SET email = {{email}} WHERE id = {{id}}", 
        ///     new { id = 1, email = "newemail@example.com" });
        /// </code>
        /// </example>
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
        /// Executes a SQL query asynchronously on a DbConnection and returns a typed result that implements IAsyncEnumerable&lt;T&gt;.
        /// Creates and manages a DbCommand internally. Automatically maps query results to the specified type T using property name matching.
        /// Supports parameterized queries with flexible delimiters (default: {{ }}), automatic JSON/XML parsing,
        /// and proper resource management. Must be disposed using 'await using' or 'using' statement.
        /// </summary>
        /// <typeparam name="T">The type to map query results to. Can be a class with properties matching column names.</typeparam>
        /// <param name="con">The database connection to execute the query on</param>
        /// <param name="query">SQL query text. Use {{paramName}} syntax for parameters.</param>
        /// <param name="queryParams">Parameters object (anonymous object, Dictionary, JsonElement, JSON string, or custom object with matching properties)</param>
        /// <param name="queryParamsRegex">Regex pattern for parameter delimiters. Default: @"(?&lt;open_marker&gt;\{\{)(?&lt;param&gt;.*?)?(?&lt;close_marker&gt;\}\})" for {{ }} delimiters</param>
        /// <param name="commandTimeout">Command timeout in seconds. If null, uses the default timeout.</param>
        /// <param name="closeConnectionOnExit">If true, closes the connection when the result is disposed</param>
        /// <param name="cToken">Cancellation token for async operation</param>
        /// <returns>A DbAsyncQueryResult&lt;T&gt; that implements IAsyncEnumerable&lt;T&gt; and IAsyncDisposable</returns>
        /// <exception cref="ArgumentNullException">Thrown when connection or query is null</exception>
        /// <exception cref="Exception">Thrown when query execution fails</exception>
        /// <example>
        /// <code>
        /// public class User { public int Id { get; set; } public string Name { get; set; } }
        /// 
        /// using var connection = new SqlConnection(connectionString);
        /// await using var result = await connection.ExecuteQueryAsync&lt;User&gt;(
        ///     "SELECT id, name FROM Users WHERE age &gt; {{minAge}}", 
        ///     new { minAge = 18 });
        /// await foreach (var user in result)
        /// {
        ///     Console.WriteLine(user.Name);
        /// }
        /// </code>
        /// </example>
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
        /// Executes a SQL query synchronously on a DbConnection and returns a typed result that implements IEnumerable&lt;T&gt;.
        /// Creates and manages a DbCommand internally. Automatically maps query results to the specified type T using property name matching.
        /// Supports parameterized queries with flexible delimiters (default: {{ }}), automatic JSON/XML parsing,
        /// and proper resource management. Must be disposed using 'using' statement.
        /// </summary>
        /// <typeparam name="T">The type to map query results to. Can be a class with properties matching column names.</typeparam>
        /// <param name="con">The database connection to execute the query on</param>
        /// <param name="query">SQL query text. Use {{paramName}} syntax for parameters.</param>
        /// <param name="queryParams">Parameters object (anonymous object, Dictionary, JsonElement, JSON string, or custom object with matching properties)</param>
        /// <param name="queryParamsRegex">Regex pattern for parameter delimiters. Default: @"(?&lt;open_marker&gt;\{\{)(?&lt;param&gt;.*?)?(?&lt;close_marker&gt;\}\})" for {{ }} delimiters</param>
        /// <param name="commandTimeout">Command timeout in seconds. If null, uses the default timeout.</param>
        /// <param name="closeConnectionOnExit">If true, closes the connection when the result is disposed</param>
        /// <param name="cToken">Cancellation token for async operation</param>
        /// <returns>A DbQueryResult&lt;T&gt; that implements IEnumerable&lt;T&gt; and IDisposable</returns>
        /// <exception cref="ArgumentNullException">Thrown when connection or query is null</exception>
        /// <exception cref="Exception">Thrown when query execution fails</exception>
        /// <example>
        /// <code>
        /// public class User { public int Id { get; set; } public string Name { get; set; } }
        /// 
        /// using var connection = new SqlConnection(connectionString);
        /// using var result = connection.ExecuteQuery&lt;User&gt;(
        ///     "SELECT id, name FROM Users WHERE age &gt; {{minAge}}", 
        ///     new { minAge = 18 });
        /// foreach (var user in result)
        /// {
        ///     Console.WriteLine(user.Name);
        /// }
        /// </code>
        /// </example>
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
