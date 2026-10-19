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
        /// <remarks>
        /// {{ParameterName}} is the placeholder's name with every character other than an ASCII letter,
        /// a digit or an underscore replaced by an underscore, so a placeholder can be named with any
        /// characters ({{first name}}, {{e-mail}}, {{prénom}}); a name longer than MaxParameterNameLength
        /// becomes lp_vxv_long_name_1, lp_vxv_long_name_2 and so on. When two generated names come out the same,
        /// compared ignoring case ({{first name}} and {{first-name}}), the second gets a numbered suffix
        /// (_2, _3) at its end. Placeholders whose names differ only in case are one parameter.
        /// {{ParameterCount}} and {{ParameterName}} can be left out: the suffixes then tell the parameters
        /// apart. Keep {{DefaultParameterPrefix}}, the prefix (@, :) that makes the name a parameter, and
        /// something after it (one of the two, or text such as "p"): the prefix alone is not a name.
        /// </remarks>
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
            if (connection is null) throw new ArgumentNullException(nameof(connection));

            return _prefixCache.GetOrAdd(
                connection.GetType(),
                type => InferPrefixFromType(type) ?? DefaultParameterPrefix
            );
        }

        /// <summary>
        /// Clears the cached parameter prefixes. Primarily useful for testing scenarios.
        /// </summary>
        public static void ClearPrefixCache() => _prefixCache.Clear();

        /// <summary>
        /// Infers the SQL parameter prefix from the connection type's full name.
        /// </summary>
        /// <param name="connectionType">The type of the database connection.</param>
        /// <returns>The inferred parameter prefix, or null if the provider is not recognized.</returns>
        private static string? InferPrefixFromType(Type connectionType)
        {
            var typeName = connectionType.FullName ?? "";

            // Ordered by popularity — most common providers are checked first.
            // Note: each type is only resolved once and then cached, so this ordering
            // is primarily for code readability and organization.

            // SQL Server uses '@' (covers Microsoft.Data.SqlClient and System.Data.SqlClient)
            if (typeName.Contains("SqlConnection", StringComparison.OrdinalIgnoreCase))
                return "@";

            // PostgreSQL (Npgsql) uses '@' (also covers CockroachDB, TimescaleDB, YugabyteDB, CrateDB)
            if (typeName.Contains("Npgsql", StringComparison.OrdinalIgnoreCase))
                return "@";

            // MySQL / MariaDB uses '@'
            if (typeName.Contains("MySql", StringComparison.OrdinalIgnoreCase))
                return "@";

            // SQLite uses '@'
            if (typeName.Contains("Sqlite", StringComparison.OrdinalIgnoreCase))
                return "@";

            // Oracle uses ':'
            if (typeName.Contains("Oracle", StringComparison.OrdinalIgnoreCase))
                return ":";

            // ODBC uses '?' (positional parameters)
            if (typeName.Contains("Odbc", StringComparison.OrdinalIgnoreCase))
                return "?";

            // OleDb uses '?' (positional parameters)
            if (typeName.Contains("OleDb", StringComparison.OrdinalIgnoreCase))
                return "?";

            // DB2 uses '@'
            if (typeName.Contains("DB2", StringComparison.OrdinalIgnoreCase))
                return "@";

            // Snowflake uses ':'
            if (typeName.Contains("Snowflake", StringComparison.OrdinalIgnoreCase))
                return ":";

            // DuckDB uses '$'
            if (typeName.Contains("DuckDB", StringComparison.OrdinalIgnoreCase))
                return "$";

            // Firebird uses '@'
            if (typeName.Contains("Firebird", StringComparison.OrdinalIgnoreCase) ||
                typeName.Contains("FbConnection", StringComparison.OrdinalIgnoreCase))
                return "@";

            // ClickHouse uses '@'
            if (typeName.Contains("ClickHouse", StringComparison.OrdinalIgnoreCase))
                return "@";

            // SingleStore (MemSQL) uses '@'
            if (typeName.Contains("SingleStore", StringComparison.OrdinalIgnoreCase))
                return "@";

            // Teradata uses '@'
            if (typeName.Contains("Teradata", StringComparison.OrdinalIgnoreCase))
                return "@";

            // Vertica uses '@'
            if (typeName.Contains("Vertica", StringComparison.OrdinalIgnoreCase))
                return "@";

            // SAP HANA uses '@'
            if (typeName.Contains("Hana", StringComparison.OrdinalIgnoreCase))
                return "@";

            // Informix uses '@'
            if (typeName.Contains("Informix", StringComparison.OrdinalIgnoreCase))
                return "@";

            // SAP SQL Anywhere uses '@'
            if (typeName.Contains("SQLAnywhere", StringComparison.OrdinalIgnoreCase))
                return "@";

            return null; // Unknown provider - will fall back to DefaultParameterPrefix
        }

        /// <summary>
        /// Determines whether the given parameter prefix indicates positional parameters (e.g., '?' for ODBC/OleDb)
        /// where parameter order matters and each occurrence in the query requires a separate DbParameter.
        /// </summary>
        public static bool IsPositionalParameterPrefix(string parameterPrefix) => parameterPrefix == "?";

        // A generated parameter name keeps only the characters every database accepts in one: a marker
        // named "first name", "e-mail", "@type", "price (€)" or "prénom" still gets a valid name.
        private static readonly Regex _notParameterNameCharacter = new("[^A-Za-z0-9_]", RegexOptions.Compiled);

        /// <summary>
        /// Maximum allowed length for the cleaned parameter name portion of a SQL parameter.
        /// Names exceeding this length are replaced with a short deterministic name to avoid
        /// database identifier length limits (e.g., PostgreSQL's 63-character limit).
        /// </summary>
        public static int MaxParameterNameLength { get; set; } = 30;

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
#if NETSTANDARD2_0
                // DbProviderFactories isn't exposed by netstandard2.0's reference assemblies,
                // but it exists at runtime on .NET Framework 4.6.1+ and .NET Core 2.1+.
                // Resolve via reflection so consumers on those runtimes can still use this helper.
                var factoriesType =
                    Type.GetType("System.Data.Common.DbProviderFactories, System.Data, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089")
                    ?? Type.GetType("System.Data.Common.DbProviderFactories, System.Data.Common");
                var getFactoryMethod = factoriesType?.GetMethod(
                        "GetFactory", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static,
                        null, new[] { typeof(string) }, null)
                    ?? throw new PlatformNotSupportedException(
                        "DbProviderFactories is not available on this runtime. "
                        + "Create the DbConnection directly (e.g., new SqlConnection(connectionString)) instead.");
                var factory = (DbProviderFactory)getFactoryMethod.Invoke(null, new object[] { providerName })!;
#else
                var factory = DbProviderFactories.GetFactory(providerName);
#endif
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
            if (conn is null) throw new ArgumentNullException(nameof(conn));
            if (conn.State != System.Data.ConnectionState.Open)
            {
                // if the connection is in executing state, wait for it to finish
                while (conn.State == System.Data.ConnectionState.Executing
                    || conn.State == System.Data.ConnectionState.Fetching
                    || conn.State == System.Data.ConnectionState.Connecting
                    )
                {
                    await Task.Delay(100, cToken).ConfigureAwait(false);
                }

                // check if cToken is cancelled
                cToken.ThrowIfCancellationRequested();

                await conn.OpenAsync(cToken).ConfigureAwait(false);
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
            if (conn is null) throw new ArgumentNullException(nameof(conn));
            if (conn.State != System.Data.ConnectionState.Closed)
            {
                // if the connection is in executing state, wait for it to finish
                while (conn.State == System.Data.ConnectionState.Executing
                    || conn.State == System.Data.ConnectionState.Fetching
                    || conn.State == System.Data.ConnectionState.Connecting)
                {
                    await Task.Delay(100, cToken).ConfigureAwait(false);
                }

                // check if cToken is cancelled
                cToken.ThrowIfCancellationRequested();

                await conn.CloseAsync().ConfigureAwait(false);
            }
        }

        private static async Task EnsureClosedAsync(this DbDataReader reader)
        {
            if (reader == null) return;
            if (reader.IsClosed) return;
            await reader.CloseAsync().ConfigureAwait(false);
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
            if (dbc is null) throw new ArgumentNullException(nameof(dbc));
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

            await conn.EnsureOpenAsync(cToken).ConfigureAwait(false);
            cToken.ThrowIfCancellationRequested();

            // Process parameters
            if (queryParamList is not null)
            {
                // Get the parameter prefix for this connection type (cached after first lookup)
                var parameterPrefix = GetParameterPrefix(conn);
                bool isPositional = IsPositionalParameterPrefix(parameterPrefix);

                if (isPositional)
                {
                    var (transformedQuery, orderedParams) = BuildPositionalParameters(query, queryParamList);
                    query = transformedQuery;

                    // Add DbParameters in left-to-right order
                    // ParameterName is set to a sequential placeholder because some providers
                    // (e.g., Microsoft.Data.Sqlite) require it even for positional binding.
                    // True positional drivers (ODBC, OleDb) ignore the name and bind by ordinal.
                    int paramIndex = 0;
                    foreach (var (_, value) in orderedParams)
                    {
                        var p = command.CreateParameter();
                        p.ParameterName = $"p{paramIndex++}";
                        p.Value = value ?? DBNull.Value;
                        command.Parameters.Add(p);
                    }
                }
                else
                {
                    // Named parameter path (SQL Server, PostgreSQL, Oracle, SQLite, etc.)
                    var slots = ResolveMarkers(query, queryParamList, replaceText: true, replaceNullText: false, caseSensitive: false, out var slotText, out var slotDelimiter);
                    var sqlParamNames = new string[slots.Count];
                    // Compared ignoring case: SQL Server and others treat @Email and @email as one name.
                    var usedSqlParamNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    int longParamCount = 0;
                    for (int i = 0; i < slots.Count; i++)
                    {
                        var cleanedParamName = slots[i].Name.Length > MaxParameterNameLength
                            ? $"lp_vxv_long_name_{++longParamCount}"
                            : _notParameterNameCharacter.Replace(slots[i].Name, "_");

                        // Two markers can still clean to the same name ("first name" and "first-name"),
                        // and a template without {{ParameterName}} gives every marker of a set the same
                        // name, so a name already taken gets a numbered suffix at its end.
                        var generatedName = SqlParameterName(parameterPrefix, slots[i].Source, cleanedParamName);
                        var sqlParamName = generatedName;
                        for (int n = 2; !usedSqlParamNames.Add(sqlParamName); n++)
                            sqlParamName = generatedName + "_" + n.ToString(CultureInfo.InvariantCulture);
                        sqlParamNames[i] = sqlParamName;

                        var p = command.CreateParameter();
                        p.ParameterName = sqlParamName;
                        p.Value = slots[i].Value ?? DBNull.Value;
                        command.Parameters.Add(p);
                    }
                    query = FillSlots(slotText, slotDelimiter, slot => sqlParamNames[slot]);
                }
            }

            command.CommandText = query;
            reader = await command.ExecuteReaderAsync(cToken).ConfigureAwait(false);
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
            // True while the caller holds a row: an enumerator disposed then stopped early.
            bool atRow = false;
            // The batch was read to its end without an exception.
            bool readToTheEnd = false;
            try
            {
                if (reader?.HasRows == true)
                {
                    bool cont = true;
                    while (cont)
                    {
                        cont = await reader.ReadAsync(cToken).ConfigureAwait(false);
                        cToken.ThrowIfCancellationRequested();
                        if (!cont) break;

                        ExpandoObject result = new();

                        if (reader.FieldCount == 1
                            // check if there is no column name
                            && string.IsNullOrEmpty(reader.GetName(0)))

                        {
                            // A single column with no name (SQL Server's COUNT(*) or
                            // SCOPE_IDENTITY() without an alias) is returned as its bare value.
                            // This used to fall through to the object below as well, so every row
                            // came back twice (the value, then an object with an empty key), and
                            // three times when NULL (null, DBNull, the object).
                            var value = reader.GetValue(0);
                            atRow = true;
                            yield return value is DBNull ? null! : value;
                            atRow = false;
                            continue;
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

                        atRow = true;
                        yield return result;
                        atRow = false;
                    }
                }

                // Every row of the first result set has been read. Move through the remaining
                // result sets before closing, so an error raised after the first one reaches the
                // caller: SQL Server's THROW or RAISERROR after a SELECT, or a later statement that
                // fails. Closing the reader instead discards them (SqlClient cleans up the rest of
                // the batch without raising its errors), and the caller sees a success. Their rows
                // are skipped: only the first result set is returned. A caller that stops reading
                // early never gets here; on providers whose Close reads the rest of the batch
                // (Npgsql, System.Data.Odbc), the error is thrown from disposal instead.
                if (reader != null)
                {
                    while (await reader.NextResultAsync(cToken).ConfigureAwait(false))
                    {
                        cToken.ThrowIfCancellationRequested();
                    }
                }
                readToTheEnd = true;
            }
            finally
            {
                // Auto-close the reader when the iterator finishes — whether via normal
                // completion (ReadAsync returned false), exception, or enumerator disposal
                // (e.g., foreach breaking out early, or a LINQ operator like First() that
                // stops after one match). This frees the reader slot on the connection
                // immediately, so a subsequent ExecuteQuery on the same connection works
                // on strict providers (SQL Server without MARS, Oracle, PostgreSQL, etc.)
                // without requiring the caller to explicitly dispose the result wrapper.
                // The wrapper's Dispose is still safe to call afterwards — Close is idempotent.
                if (reader == null || reader.IsClosed)
                {
                }
                else if (!atRow && !readToTheEnd)
                {
                    // An exception is on its way to the caller (the batch's error, say). Closing can
                    // throw a later error in its place (System.Data.Odbc reads the rest of the batch
                    // when it closes), so the reader is closed quietly.
                    await CloseQuietlyAsync(reader).ConfigureAwait(false);
                }
                else
                {
                    try
                    {
                        await reader.CloseAsync().ConfigureAwait(false);
                    }
                    catch
                    {
                        // Some providers (Npgsql, System.Data.Odbc) read the rest of the batch when the
                        // reader closes and throw its error here, for the caller. System.Data.Odbc stays
                        // open after such a Close, which keeps its command from running another query.
                        await CloseQuietlyAsync(reader).ConfigureAwait(false);
                        throw;
                    }
                }
            }
        }

        /// <summary>
        /// Closes the reader without throwing, when an error is already on its way to the caller: closing
        /// can throw a later error in its place. Some providers (System.Data.Odbc) read the rest of the
        /// batch when they close, throw, and stay open; a second Close then closes them.
        /// </summary>
        private static async Task CloseQuietlyAsync(DbDataReader reader)
        {
            for (int attempt = 0; attempt < 2 && !reader.IsClosed; attempt++)
            {
                try
                {
                    await reader.CloseAsync().ConfigureAwait(false);
                }
                catch
                {
                }
            }
        }




        /// <summary>
        /// Transforms a query with named placeholders (e.g., {{name}}) into a positional query using '?'
        /// and returns the parameter values in left-to-right occurrence order.
        /// Each occurrence of a placeholder produces a separate entry, so the same parameter
        /// appearing multiple times in the query produces multiple entries with the same value.
        /// Placeholders take their values by the named path's rules: when several parameter sets'
        /// patterns match the same placeholder, it is replaced once, with the value of the latest of those
        /// sets that has a non-null one. Among sets with the exact same pattern string, a later set's value
        /// for a name decides, even when it is null; a set with another pattern keeps its own place in the
        /// list. Only the patterns' matches are replaced, though: the named path also replaces the
        /// text of a placeholder that has a value where its pattern doesn't match it (a use after a stray
        /// "{{" that the pattern pairs with it, or one a pattern skips), so such a use can differ between
        /// the two. A NULL placeholder is replaced only where its pattern matches it, on both paths. And a set
        /// listed between two with the exact same pattern string takes nothing here where another set with a value
        /// has a placeholder that overlaps one of its pattern's matches without being the same one (a stray "{{"
        /// paired past one, ":date-x" where it reads ":date"): the earlier set's value is used. That set counts if
        /// it is listed after this one and the last set with its exact pattern string is listed no later than the
        /// last of the two. The placeholders of a set whose last set with its exact pattern string is listed after
        /// the last of the two are slots before any of the three is read, so they don't count; other sets'
        /// placeholders are still text then. The overlap is the one in the query as it then stands.
        /// </summary>
        /// <param name="query">The SQL query with placeholders.</param>
        /// <param name="queryParamList">The parameter sets with regex patterns and data models.</param>
        /// <returns>A tuple of (transformedQuery, orderedParameters) where orderedParameters contains
        /// (paramName, value) pairs in query-occurrence order.</returns>
        public static (string TransformedQuery, List<(string Name, object? Value)> OrderedParameters)
            BuildPositionalParameters(string query, IEnumerable<DbQueryParams> queryParamList)
        {
            var slots = ResolveMarkers(query, queryParamList, replaceText: false, replaceNullText: false, caseSensitive: false, out var slotText, out var slotDelimiter);
            var orderedParams = new List<(string Name, object? Value)>();
            var transformedQuery = FillSlots(slotText, slotDelimiter, slot =>
            {
                orderedParams.Add((slots[slot].Name, slots[slot].Value));
                return "?";
            });
            return (transformedQuery, orderedParams);
        }

        /// <summary>
        /// Finds the value of every placeholder in the query and replaces each placeholder with a slot.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The parameter sets are read last to first. Sets with the exact same pattern string are read as one,
        /// in the place of the last of them: its value for a name is that of the latest of them that has the
        /// name, even a null one. A placeholder takes the value of the latest set whose pattern matches it and
        /// that has a non-null value under its name (compared ignoring case, unless
        /// <paramref name="caseSensitive"/>; the SQL paths always ignore it). A set without one leaves the
        /// placeholder for the set before it: several sets' patterns can match the same placeholder ({{name}}
        /// for both a JSON body and a query string, say), and each placeholder is replaced only once. Where a
        /// merged set's value comes from one of its earlier sets, a set listed after that one with a non-null
        /// value under the name its own pattern reads there, and whose pattern reads that placeholder where it
        /// is written, takes it, as far as that set's own pass would reach: with
        /// <paramref name="replaceText"/> and a placeholder with both markers, every use of its text, as long as
        /// its pass still gets a match of it in its own place in the list (a set listed after its source, with a
        /// value, takes a match first where one of its own matches overlaps it, or with
        /// <paramref name="replaceText"/> where its text pass replaces it: a text with both markers wherever it
        /// appears, where that set's own pass still reaches it, a one-marker text where it doesn't go on into a
        /// longer name, and a match of its own only where it still gets that match once the places taken first are
        /// slots, or recovers it from the query as written); otherwise the uses its pattern matches where this set's
        /// pattern matches them too, in the query as it stands (not the placeholders recovered from the query as
        /// written). Without <paramref name="replaceText"/> (the positional path), it takes nothing where a set
        /// listed after its source, with a value, in the merged set or in a merged set read after it, has a match
        /// in the query as it stands that overlaps one of its own matches (those that touch a slot included)
        /// without being the same placeholder: the slot written first can make it pair its markers differently. The placeholder has a slot of its own, which that set refills when it
        /// is read, and which is then made one with that set's own slot for the name; a slot the query no
        /// longer uses is dropped. A null entry in the list is
        /// skipped. A placeholder that no set has a value for becomes NULL, under the latest set whose pattern
        /// matched it.
        /// </para>
        /// <para>
        /// The placeholders of one set with one name share one slot, whichever marker each is written
        /// with, and so do names that differ only in case unless <paramref name="caseSensitive"/>. In the returned
        /// text each slot is written as its index between two <paramref name="slotDelimiter"/> characters:
        /// a private-use character the query doesn't contain, so text that looks like a slot can't come
        /// from the query. The patterns of the sets read after it skip any match that touches a slot
        /// (<see cref="MatchesOutsideSlots"/>). A text that holds every private-use character
        /// (U+E000 to U+F8FF) leaves none for the delimiter, and an ArgumentException is thrown.
        /// </para>
        /// <para>
        /// With <paramref name="replaceText"/> (the named path), for a pattern with an open and a close
        /// marker, a resolved placeholder's text (from its open marker to its close marker, and its plain
        /// spelling when the pattern takes that for it too) becomes its slot wherever it appears outside the
        /// slots already written, compared as names are; and so do the placeholders the pattern matches on its
        /// own in the query as written, which a stray marker may now pair differently. Otherwise, and for NULL placeholders, the part of each of the
        /// pattern's matches that <see cref="PlaceholderSpan(Match)"/> gives does; on the named path, the text of
        /// a resolved placeholder with one marker then also does wherever it doesn't go on into a longer name
        /// (<see cref="ReplaceOneMarkerText"/>). A NULL placeholder's text where the pattern skips it stays,
        /// unless <paramref name="replaceNullText"/>: then it is replaced as a resolved one's is.
        /// </para>
        /// <para>
        /// DataExtensions.Fill resolves its placeholders here too, with <paramref name="replaceText"/> and
        /// <paramref name="replaceNullText"/>, and writes the values into the slots at the end, so text
        /// inside a value is never read as a placeholder.
        /// </para>
        /// </remarks>
        internal static List<(string Name, int Source, object? Value)> ResolveMarkers(
            string query,
            IEnumerable<DbQueryParams> queryParamList,
            bool replaceText,
            bool replaceNullText,
            bool caseSensitive,
            out string slotText,
            out char slotDelimiter)
        {
            var comparer = caseSensitive ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase;
            var comparison = caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

            // The first private-use character the text doesn't contain, found in one pass: U+E000 when it has
            // none, as a query almost never does.
            var delimiter = '\uE000';
            bool hasPrivateUse = false;
            foreach (var c in query)
                if (c >= '\uE000' && c <= '\uF8FF')
                {
                    hasPrivateUse = true;
                    break;
                }
            if (hasPrivateUse)
            {
                var present = new bool['\uF8FF' - '\uE000' + 1];
                foreach (var c in query)
                    if (c >= '\uE000' && c <= '\uF8FF') present[c - '\uE000'] = true;
                int free = Array.IndexOf(present, false);
                if (free < 0)
                    throw new ArgumentException("The text contains every Unicode private-use character (U+E000 to U+F8FF), so none is left to mark its placeholders with.");
                delimiter = (char)('\uE000' + free);
            }
            string Slot(int index) => delimiter + index.ToString(CultureInfo.InvariantCulture) + delimiter;

            var slots = new List<(string Name, int Source, object? Value)>();

            // Whether a pattern takes a text for a placeholder of a name (see IsPlaceholderOf), worked out once each.
            var placeholderOf = new Dictionary<string, bool>(StringComparer.Ordinal);
            bool IsPlaceholderOfOnce(string text, string pattern, string name)
            {
                var key = pattern + "\0" + text + "\0" + name;
                if (!placeholderOf.TryGetValue(key, out var result))
                    placeholderOf[key] = result = IsPlaceholderOf(text, pattern, name, comparer);
                return result;
            }

            // The sources, grouped by exact pattern string, null entries skipped. A group is read in the place
            // of its last source (sources are read last to first), as one source: its value for a name is the
            // value of its latest source that has the name, even a null one. That source keeps its own place
            // for the name, though: see the claims below.
            var groups = new List<SourceGroup>();
            int listPosition = 0;
            foreach (var queryParam in queryParamList)
            {
                listPosition++;
                if (queryParam is null) continue;
                var group = groups.FirstOrDefault(x => string.Equals(x.Pattern, queryParam.QueryParamsRegex, StringComparison.Ordinal));
                if (group is null)
                    groups.Add(group = new SourceGroup(queryParam.QueryParamsRegex, comparer));
                group.Members.Add((listPosition, queryParam.DataModel?.GetDataModelParameters(true, caseSensitive)));
            }
            foreach (var group in groups)
            {
                group.Last = group.Members[group.Members.Count - 1].Position;
                for (int i = group.Members.Count - 1; i >= 0; i--)
                    foreach (var item in group.Members[i].Model ?? new Dictionary<string, object>())
                    {
                        if (item.Value is not null && group.Members[i].Position > group.LatestWithAValue)
                            group.LatestWithAValue = group.Members[i].Position;
                        if (group.Model.TryAdd(item.Key, item.Value))
                            group.SupplierOf[item.Key] = group.Members[i].Position;
                    }
            }
            groups.Sort((a, b) => b.Last.CompareTo(a.Last));

            // Per set, the placeholder names it had no value for, (for Fill) their text as written, and the
            // one-marker placeholders it matched, which a shorter one's text must not be taken from.
            var unresolved = new List<(string QueryParamsRegex, int Source, HashSet<string> Names,
                List<(string Text, string Name, bool OpenEmpty, bool CloseEmpty)> Texts, List<string> OneMarkerPlaceholders)>();
            // Placeholders a group filled from a source that a source listed after it, read later, may take.
            var claims = new List<SlotClaim>();
            // Placeholder texts known to be gone from the query (every change turns text into a slot).
            var absentTexts = new HashSet<string>(comparer);
            var originalQuery = query;

            // Group g takes the placeholders that a group read before it filled from a source listed
            // before g's source for the name, where its pattern matched them or by text where its own
            // pass still reaches them (see Claimants and PassReaches), under
            // the name it reads there; of several such groups, the one whose source is listed last.
            void TakeClaims(int g, SourceGroup group, int source)
            {
                foreach (var claim in claims)
                {
                    foreach (var (claimant, laterName) in claim.Claimants)
                    {
                        if (claimant != g) continue;
                        var supplier = group.SupplierOf[laterName];
                        if (supplier <= claim.Supplier) continue;
                        slots[claim.Slot] = (laterName, source, group.Model[laterName]);
                        claim.Supplier = supplier;
                    }
                }
            }

            for (int g = 0; g < groups.Count; g++)
            {
                var group = groups[g];
                int source = g + 1;
                var matches = MatchesOutsideSlots(query, group.Pattern!, delimiter, out bool skippedAMatch);
                // A group whose pattern matches nothing in the query as it stands, and skips no match, has
                // nothing to bind or replace: it only takes what a group read before it left to it.
                if (matches.Count == 0 && !skippedAMatch)
                {
                    TakeClaims(g, group, source);
                    continue;
                }
                var unresolvedNames = new HashSet<string>(comparer);
                var valueOfName = new Dictionary<string, object?>(comparer);
                // The slot of each placeholder name this set has resolved, and of each placeholder a set read after
                // it may take (keyed by the name and the sets that may take it; see SlotWithClaimants).
                var slotOfName = new Dictionary<string, int>(comparer);
                var slotOfClaimKey = new Dictionary<string, int>(comparer);
                // Only a value from a source before the group's last one can be taken by a set read later.
                bool mayBeClaimed = g + 1 < groups.Count && group.SupplierOf.Values.Any(x => x < group.Last);
                // The placeholders each set read later matches in the query as it stands when this one reads, with
                // the name that set reads there: by place, and (named path and Fill) by text for those with both
                // markers, whose text that set's own pass replaces wherever it appears.
                var laterReadings = new Dictionary<int, LaterSetReading>();
                var plainChecked = new HashSet<string>(StringComparer.Ordinal);
                var passReaches = new Dictionary<(int, List<(int, int)>), bool>();
                var pairsDifferently = new Dictionary<(int, int), bool>();
                var takenPlaces = new Dictionary<(int, int), PlaceSet>();
                var stillMatched = new Dictionary<(int, int), (HashSet<(int, int)> Spans, bool Skipped, HashSet<string> Texts)>();
                var originalTexts = new Dictionary<int, HashSet<string>>();
                var everySpanOf = new Dictionary<int, List<(int Start, int End)>>();
                var occurrencesOfText = new Dictionary<string, List<(int, int)>>(comparer);
                // The text of each placeholder this set resolved with both markers, as written.
                var resolvedText = new Dictionary<string, int>(comparer);
                bool everyResolvedMatchHasBothMarkers = true;
                // The same for placeholders with one marker, and which side has none.
                var oneMarkerText = new Dictionary<string, (int Slot, bool OpenEmpty, bool CloseEmpty)>(comparer);
                // The one-marker placeholders this reading matched, and the placeholders it had no value for.
                var oneMarkerPlaceholders = new List<string>();
                var oneMarkerPlaceholderSeen = new HashSet<string>(comparer);
                var unresolvedTexts = new List<(string Text, string Name, bool OpenEmpty, bool CloseEmpty)>();
                var unresolvedTextSeen = new HashSet<string>(comparer);

                object? ValueOf(string name)
                {
                    if (!valueOfName.TryGetValue(name, out var value))
                    {
                        group.Model.TryGetValue(name, out var found);
                        valueOfName[name] = value = found;
                        if (value is null) unresolvedNames.Add(name);
                    }
                    return value;
                }

                LaterSetReading LaterReading(int h)
                {
                    if (!laterReadings.TryGetValue(h, out var reading))
                    {
                        reading = new LaterSetReading(comparer);
                        var pattern = groups[h].Pattern;
                        if (pattern is not null)
                        {
                            var laterMatches = MatchesOutsideSlots(query, pattern, delimiter, out var skipped);
                            foreach (var m in laterMatches)
                            {
                                var param = m.Groups["param"];
                                if (param.Length == 0) continue;
                                var name = param.Value;
                                var open = m.Groups["open_marker"];
                                var close = m.Groups["close_marker"];
                                var (start, end) = PlaceholderSpan(m, open, close, param);
                                var openMarker = open.Value;
                                var closeMarker = close.Value;
                                bool bothMarkers = openMarker.Length > 0 && closeMarker.Length > 0;
                                reading.BySpan[(start, end)] = (name, bothMarkers);
                                // A bare name is replaced at its place only. A one-marker text is replaced where it
                                // doesn't go on into a longer name (see ReplaceOneMarkerText).
                                if (openMarker.Length == 0 && closeMarker.Length == 0) continue;
                                if (!bothMarkers)
                                {
                                    var oneMarker = openMarker + name + closeMarker;
                                    if (reading.OneMarkerTextSeen.Add(oneMarker))
                                    {
                                        reading.OneMarkerTexts.Add((oneMarker, name, openMarker.Length == 0, closeMarker.Length == 0));
                                        reading.OneMarkerPlaceholders.Add(oneMarker);
                                    }
                                    continue;
                                }
                                // Each text that set's pass would replace everywhere, with the matches that make it
                                // do so (its anchors): the match's own text, and the plain spelling of a spaced one.
                                var text = query.Substring(start, end - start);
                                AddAnchor(reading.ByText, text, name, (start, end));
                                var plain = openMarker + name + closeMarker;
                                if (!comparer.Equals(plain, text) && IsPlaceholderOfOnce(plain, pattern, name))
                                    AddAnchor(reading.ByText, plain, name, (start, end));
                            }
                            // And the placeholders its pattern matches in the query as written, when a stray marker
                            // now pairs differently there (see the original reading below).
                            // Its anchors are the places the text still occurs.
                            if (replaceText && skipped)
                                foreach (var placeholder in OriginalReading(originalQuery, pattern, comparer))
                                    if (!reading.ByText.ContainsKey(placeholder.Text))
                                    {
                                        var occurrences = OccurrencesOutsideSlots(query, placeholder.Text, comparison, delimiter);
                                        if (occurrences.Count > 0)
                                            reading.ByText[placeholder.Text] = (placeholder.Name, occurrences);
                                    }
                        }
                        laterReadings[h] = reading;
                    }
                    return reading;
                }

                // The sets read later that take a placeholder of the name from this group: each has a value for the
                // name it reads there, from a source listed after the one this group's value comes from, and its
                // pattern matches the placeholder where it is written. A set whose placeholder has both markers
                // replaces its text wherever it appears on the named path and in Fill, so there it takes every use
                // of that text (byText); otherwise it takes only the uses its pattern matches (at this place).
                List<(int Group, string Name)>? Claimants(string name, string text, int start, int end, bool byText)
                {
                    var supplier = group.SupplierOf[name];
                    if (!mayBeClaimed || supplier >= group.Last) return null;
                    List<(int Group, string Name)>? claimants = null;
                    for (int h = g + 1; h < groups.Count; h++)
                    {
                        if (groups[h].LatestWithAValue <= supplier) continue;
                        var reading = LaterReading(h);
                        string? laterName = null;
                        if (byText && reading.ByText.TryGetValue(text, out var atText) && HasValueFromAfter(h, atText.Name, supplier)
                            && PassReaches(h, atText.Name, text, atText.Anchors))
                            laterName = atText.Name;
                        else if (start >= 0 && reading.BySpan.TryGetValue((start, end), out var atSpan)
                            && !(replaceText && atSpan.BothMarkers) && HasValueFromAfter(h, atSpan.Name, supplier)
                            // A placeholder with both markers that h replaces by its text (positional path): h's
                            // pass must still be reached, as on the named path.
                            && (!atSpan.BothMarkers || PassReaches(h, atSpan.Name, text,
                                reading.ByText.TryGetValue(text, out var anchorsOfText) ? anchorsOfText.Anchors : new List<(int, int)> { (start, end) })))
                            laterName = atSpan.Name;
                        if (laterName is not null && groups[h].SupplierOf.TryGetValue(laterName, out var later)
                            && later > supplier && groups[h].Model[laterName] is not null
                            // On the positional path, which has no recovery from the query as written.
                            && (replaceText || !PairsDifferently(h, later)))
                            (claimants ??= new List<(int, string)>()).Add((h, laterName));
                    }
                    return claimants;
                }

                // Whether the sources listed after h's source for a name (at supplierOfH) would make h pair its
                // markers differently from the reading the claims use: one of their matches with a value, read
                // before h, overlaps one of h's matches without being the same placeholder (a stray marker paired
                // past it, a longer or shorter placeholder at that place), anywhere in the query as it stands when
                // this group reads. Those sources are in this group or in a group read after it (q from g on): a
                // group whose last source is listed after this group's last one is read before this group, so its
                // placeholders are slots in both readings. Such a match becomes a slot first, and h's reading can
                // change around it, so on the positional path h takes nothing, and the earlier source's value is
                // used. h's matches here include those that touch a slot already written: a stray marker paired past
                // one. (On the named path and in Fill, PassReaches covers this through StillMatched and h's
                // recovery from the query as written.)
                bool PairsDifferently(int h, int supplierOfH)
                {
                    if (pairsDifferently.TryGetValue((h, supplierOfH), out var cached)) return cached;
                    if (!everySpanOf.TryGetValue(h, out var spansOfH))
                    {
                        spansOfH = new List<(int Start, int End)>();
                        if (groups[h].Pattern is string patternOfH)
                            foreach (Match m in Regex.Matches(query, patternOfH))
                                if (m.Groups["param"].Length > 0)
                                    spansOfH.Add(PlaceholderSpan(m));
                        everySpanOf[h] = spansOfH;
                    }
                    bool result = false;
                    for (int q = g; q < groups.Count && !result; q++)
                    {
                        if (q == h || groups[q].LatestWithAValue <= supplierOfH) continue;
                        foreach (var at in LaterReading(q).BySpan)
                        {
                            if (!HasValueFromAfter(q, at.Value.Name, supplierOfH)) continue;
                            // h's matches don't overlap one another, so those overlapping this one are together,
                            // from the first that ends after its start.
                            for (int i = FirstEndingAfter(spansOfH, at.Key.Item1); i < spansOfH.Count && spansOfH[i].Item1 < at.Key.Item2; i++)
                                if (spansOfH[i] != at.Key)
                                {
                                    result = true;
                                    break;
                                }
                            if (result) break;
                        }
                    }
                    pairsDifferently[(h, supplierOfH)] = result;
                    return result;
                }

                // Whether set h's own pass, in its place in the list, still gets one of the matches that make it
                // replace a text everywhere (its anchors): a source listed after h's, with a value, whose pattern
                // matches over that place, is read before h and takes it first. A placeholder h recovers from the
                // query as written is anchored at the places its text still occurs, and is reached the same way.
                // An anchor must also survive the places taken first: written as slots, they can make a stray marker
                // pair past it (see StillMatched). On the named path and in Fill, h's pass still binds a text with
                // both markers wherever it appears when that reading still matches the text somewhere (a pattern that
                // takes a character before its marker can match it at another use then), or when that reading
                // skipped a match and h's pattern matches the text on its own in the query as written (its
                // recovery); neither binds a plain spelling.
                bool PassReaches(int h, string laterName, string text, List<(int, int)> anchors)
                {
                    if (passReaches.TryGetValue((h, anchors), out var cached)) return cached;
                    bool reached = PassReachesOnce(h, laterName, text, anchors);
                    passReaches[(h, anchors)] = reached;
                    return reached;
                }

                bool PassReachesOnce(int h, string laterName, string text, List<(int, int)> anchors)
                {
                    if (!groups[h].SupplierOf.TryGetValue(laterName, out var supplierOfH)) return false;
                    var taken = TakenPlaces(h, supplierOfH);
                    foreach (var (anchorStart, anchorEnd) in anchors)
                    {
                        if (taken.Overlaps(anchorStart, anchorEnd)) continue;
                        var still = StillMatched(h, supplierOfH);
                        if (still.Spans.Contains((anchorStart, anchorEnd))) return true;
                        if (replaceText
                            && anchorEnd - anchorStart == text.Length && string.Compare(query, anchorStart, text, 0, text.Length, comparison) == 0
                            && (still.Texts.Contains(text) || (still.Skipped && OriginalTexts(h).Contains(text))))
                            return true;
                    }
                    return false;
                }

                // The texts of the placeholders with both markers that h's pattern matches in the query as written.
                HashSet<string> OriginalTexts(int h)
                {
                    if (!originalTexts.TryGetValue(h, out var texts))
                    {
                        texts = new HashSet<string>(comparer);
                        foreach (var placeholder in OriginalReading(originalQuery, groups[h].Pattern!, comparer))
                            texts.Add(placeholder.Text);
                        originalTexts[h] = texts;
                    }
                    return texts;
                }

                // h's matches once the places taken before it (see TakenPlaces) are slots: each written as a slot of
                // its own length, a place of one character left as it is. And whether that reading skipped a match,
                // and the texts of its matches with both markers, which h's text pass replaces wherever they appear.
                (HashSet<(int, int)> Spans, bool Skipped, HashSet<string> Texts) StillMatched(int h, int supplierOfH)
                {
                    if (stillMatched.TryGetValue((h, supplierOfH), out var cached)) return cached;
                    var masked = query.ToCharArray();
                    foreach (var (start, end) in TakenPlaces(h, supplierOfH).Merged)
                    {
                        if (end - start < 2) continue;
                        masked[start] = masked[end - 1] = delimiter;
                        for (int i = start + 1; i < end - 1; i++) masked[i] = '0';
                    }
                    var matched = new HashSet<(int, int)>();
                    var texts = new HashSet<string>(comparer);
                    var maskedMatches = MatchesOutsideSlots(new string(masked), groups[h].Pattern!, delimiter, out var skipped);
                    foreach (var m in maskedMatches)
                    {
                        var (start, end) = PlaceholderSpan(m);
                        matched.Add((start, end));
                        if (m.Groups["open_marker"].Length > 0 && m.Groups["close_marker"].Length > 0)
                            texts.Add(query.Substring(start, end - start));
                    }
                    return stillMatched[(h, supplierOfH)] = (matched, skipped, texts);
                }

                // Whether set q has a value for the name from a source listed after the one at the place given, so
                // that source is read before it.
                bool HasValueFromAfter(int q, string name, int place) =>
                    groups[q].SupplierOf.TryGetValue(name, out var supplierOfQ) && supplierOfQ > place && groups[q].Model[name] is not null;

                // The places that the sources listed after h's source for a name (at supplierOfH) take before h reads:
                // their matches with a value, this group's own sources included and h left out, and on the named path
                // and in Fill, what their text passes replace: a text with both markers wherever it appears, where
                // that set's own pass still reaches it (see PassReaches; this group's own sources read here and
                // now), and a one-marker text where it doesn't go on into a longer name. A bare name is replaced at
                // its place only. Worked out once per set and source, and kept while this group reads (the query
                // doesn't change until it has). PassReaches asks TakenPlaces about a source listed later still, so
                // the questions end.
                PlaceSet TakenPlaces(int h, int supplierOfH)
                {
                    if (takenPlaces.TryGetValue((h, supplierOfH), out var cached)) return cached;
                    var places = new List<(int Start, int End)>();
                    for (int q = g; q < groups.Count; q++)
                    {
                        if (q == h || groups[q].LatestWithAValue <= supplierOfH) continue;
                        var reading = LaterReading(q);
                        foreach (var at in reading.BySpan)
                            if (HasValueFromAfter(q, at.Value.Name, supplierOfH))
                                places.Add(at.Key);
                        if (!replaceText) continue;
                        // A slot holds the delimiter and digits only, so a text of digits doesn't count.
                        foreach (var at in reading.ByText)
                            if (!IsDigits(at.Key) && HasValueFromAfter(q, at.Value.Name, supplierOfH)
                                && (q == g || PassReaches(q, at.Value.Name, at.Key, at.Value.Anchors)))
                                places.AddRange(EveryOccurrence(at.Key));
                        foreach (var text in reading.OneMarkerTexts)
                            if (HasValueFromAfter(q, text.Name, supplierOfH))
                                places.AddRange(OneMarkerTextPlaces(query, text.Text, text.OpenEmpty, text.CloseEmpty,
                                    comparison, delimiter, reading.OneMarkerPlaceholdersByLength ??= ByLength(reading.OneMarkerPlaceholders, comparison)));
                    }
                    var set = new PlaceSet(places);
                    takenPlaces[(h, supplierOfH)] = set;
                    return set;
                }

                // Every place the text occurs in the query, overlapping ones included.
                List<(int, int)> EveryOccurrence(string text)
                {
                    if (!occurrencesOfText.TryGetValue(text, out var occurrences))
                    {
                        occurrences = new List<(int, int)>();
                        for (int index = query.IndexOf(text, comparison); index >= 0; index = query.IndexOf(text, index + 1, comparison))
                            occurrences.Add((index, index + text.Length));
                        occurrencesOfText[text] = occurrences;
                    }
                    return occurrences;
                }

                int SlotWithClaimants(string name, object value, List<(int Group, string Name)>? claimants)
                {
                    if (claimants is not null)
                    {
                        var key = name + "\0" + string.Join("\0", claimants.Select(x => x.Group.ToString(CultureInfo.InvariantCulture) + ":" + x.Name));
                        if (!slotOfClaimKey.TryGetValue(key, out var claimable))
                        {
                            claimable = slots.Count;
                            slots.Add((name, source, value));
                            slotOfClaimKey[key] = claimable;
                            claims.Add(new SlotClaim(claimable, group.SupplierOf[name], claimants));
                        }
                        return claimable;
                    }
                    if (!slotOfName.TryGetValue(name, out var slot))
                    {
                        slot = slots.Count;
                        slots.Add((name, source, value));
                        slotOfName.Add(name, slot);
                    }
                    return slot;
                }

                // The slot a placeholder's text gets wherever this group's text passes replace it (named path and Fill).
                int TextSlot(string name, string text)
                {
                    var value = ValueOf(name);
                    return value is null ? -1 : SlotWithClaimants(name, value, Claimants(name, text, -1, -1, byText: replaceText));
                }

                // The slot of one of this group's matches: also taken by a set read later that matches it at its place.
                int MatchSlot(string name, string text, int start, int end)
                {
                    var value = ValueOf(name);
                    return value is null ? -1 : SlotWithClaimants(name, value, Claimants(name, text, start, end, byText: replaceText));
                }

                void Bind((string Text, string Name, bool OpenEmpty, bool CloseEmpty) placeholder, int slot)
                {
                    if (slot < 0)
                    {
                        if (replaceNullText && unresolvedTextSeen.Add(placeholder.Text))
                            unresolvedTexts.Add(placeholder);
                    }
                    else if (!placeholder.OpenEmpty && !placeholder.CloseEmpty)
                        resolvedText[placeholder.Text] = slot;
                    else
                    {
                        everyResolvedMatchHasBothMarkers = false;
                        oneMarkerText[placeholder.Text] = (slot, placeholder.OpenEmpty, placeholder.CloseEmpty);
                    }
                }

                // Each match's slot, and the slot its text gets where this group's text passes replace it; they
                // differ when a set read later takes this match at its place only (see Claimants).
                var slotOfMatch = new int[matches.Count];
                var textSlotOfMatch = new int[matches.Count];
                for (int i = 0; i < matches.Count; i++)
                {
                    var m = matches[i];
                    slotOfMatch[i] = textSlotOfMatch[i] = -1;
                    var name = m.Groups["param"].Value;
                    if (string.IsNullOrEmpty(name)) continue;
                    var openMarker = m.Groups["open_marker"].Value;
                    var closeMarker = m.Groups["close_marker"].Value;
                    var (start, end) = PlaceholderSpan(m);
                    var spanText = query.Substring(start, end - start);
                    int slot = slotOfMatch[i] = MatchSlot(name, spanText, start, end);
                    if (openMarker.Length == 0 && closeMarker.Length == 0)
                    {
                        // A bare name: only the pattern's match is replaced, below.
                        if (slot >= 0) everyResolvedMatchHasBothMarkers = false;
                        continue;
                    }
                    bool bothMarkers = openMarker.Length > 0 && closeMarker.Length > 0;
                    // With both markers, the text the placeholder takes up, so a pattern that allows text
                    // between a marker and the name ({{ name }}) has it replaced too.
                    var placeholder = (Text: bothMarkers ? spanText : openMarker + name + closeMarker,
                        Name: name, OpenEmpty: openMarker.Length == 0, CloseEmpty: closeMarker.Length == 0);
                    if (!bothMarkers && oneMarkerPlaceholderSeen.Add(placeholder.Text)) oneMarkerPlaceholders.Add(placeholder.Text);
                    var textSlot = textSlotOfMatch[i] = slot < 0 ? -1 : TextSlot(name, placeholder.Text);
                    Bind(placeholder, textSlot);

                    // And its plain spelling ({{name}} for {{ name }}), when the pattern takes that for a
                    // placeholder of the name too: a stray "{{" may hide that spelling's use.
                    var plain = openMarker + name + closeMarker;
                    if (replaceText && bothMarkers && !comparer.Equals(plain, spanText) && plainChecked.Add(plain + "\0" + name)
                        && !resolvedText.ContainsKey(plain) && !unresolvedTextSeen.Contains(plain) && !absentTexts.Contains(plain)
                        && IsPlaceholderOf(plain, group.Pattern!, name, comparer))
                    {
                        if (OccursOutsideSlots(query, plain, comparison, delimiter))
                            Bind((plain, name, false, false), TextSlot(name, plain));
                        else
                            absentTexts.Add(plain);
                    }
                }

                if (replaceText && skippedAMatch)
                {
                    // The placeholders the pattern matches in the query as written are bound by their text too
                    // (with both markers), so a stray "{{" that now pairs differently, past a placeholder a set
                    // read before this one replaced, can't hide them. Only such a pairing makes this reading
                    // skip a match, so without one there is nothing to recover.
                    foreach (var placeholder in OriginalReading(originalQuery, group.Pattern!, comparer))
                    {
                        if (resolvedText.ContainsKey(placeholder.Text) || unresolvedTextSeen.Contains(placeholder.Text)
                            || absentTexts.Contains(placeholder.Text))
                            continue;
                        if (ValueOf(placeholder.Name) is null && !replaceNullText) continue;
                        if (!OccursOutsideSlots(query, placeholder.Text, comparison, delimiter))
                        {
                            absentTexts.Add(placeholder.Text);
                            continue;
                        }
                        Bind(placeholder, TextSlot(placeholder.Name, placeholder.Text));
                    }
                }

                if (replaceText && everyResolvedMatchHasBothMarkers)
                {
                    // A match that a set read later takes at its place only is replaced there first, so the text
                    // pass below gives the other uses of its text this group's slot.
                    if (Enumerable.Range(0, matches.Count).Any(i => slotOfMatch[i] != textSlotOfMatch[i]))
                    {
                        var replaced = new System.Text.StringBuilder(query.Length);
                        int position = 0;
                        for (int i = 0; i < matches.Count; i++)
                        {
                            if (slotOfMatch[i] == textSlotOfMatch[i]) continue;
                            var (start, end) = PlaceholderSpan(matches[i]);
                            replaced.Append(query, position, start - position);
                            replaced.Append(Slot(slotOfMatch[i]));
                            position = end;
                        }
                        replaced.Append(query, position, query.Length - position);
                        query = replaced.ToString();
                    }
                    // Named path: a resolved placeholder's text is replaced wherever it appears (ignoring
                    // case unless caseSensitive), not only where the pattern matched it. Queries the pattern pairs oddly rely on
                    // that: a stray "{{" in a literal before a second {{id}} (the pattern pairs them, so
                    // that {{id}} is no match of its own), a pipe operator beside |a| delimiters, or a
                    // pattern that also matches the character before the marker.
                    foreach (var text in resolvedText)
                        query = ReplaceOutsideSlots(query, text.Key, Slot(text.Value), comparison, delimiter);
                }
                else if (slotOfMatch.Any(slot => slot >= 0) || resolvedText.Count > 0 || oneMarkerText.Count > 0)
                {
                    // The pattern's matches are replaced (see PlaceholderSpan for which part of each).
                    var replaced = new System.Text.StringBuilder(query.Length);
                    int position = 0;
                    for (int i = 0; i < matches.Count; i++)
                    {
                        if (slotOfMatch[i] < 0) continue;
                        var (start, end) = PlaceholderSpan(matches[i]);
                        replaced.Append(query, position, start - position);
                        replaced.Append(Slot(slotOfMatch[i]));
                        position = end;
                    }
                    replaced.Append(query, position, query.Length - position);
                    query = replaced.ToString();

                    if (replaceText)
                    {
                        // Named path, a pattern with a one-marker alternative: its two-marker placeholders
                        // are bound by their text as above, and its one-marker ones by guarded text.
                        foreach (var text in resolvedText)
                            query = ReplaceOutsideSlots(query, text.Key, Slot(text.Value), comparison, delimiter);
                        query = ReplaceOneMarkerText(query, oneMarkerText, Slot, comparison, delimiter, oneMarkerPlaceholders);
                    }
                }

                TakeClaims(g, group, source);

                if (unresolvedNames.Count > 0)
                    unresolved.Add((group.Pattern!, source, unresolvedNames, unresolvedTexts,
                        oneMarkerPlaceholders.Distinct(comparer).ToList()));
            }

            // What no set had a value for is NULL. Matching again finds only the placeholders still in
            // the query: one a later set resolved is a slot by now, and gets no NULL parameter. Only the
            // pattern's matches become NULL: text the pattern skips (":token" in 'session:token') stays.
            foreach (var (queryParamsRegex, unresolvedSource, names, texts, oneMarkerPlaceholders) in unresolved)
            {
                var slotOfName = new Dictionary<string, int>(comparer);
                int NullSlot(string name)
                {
                    if (!slotOfName.TryGetValue(name, out var slot))
                    {
                        slot = slots.Count;
                        slots.Add((name, unresolvedSource, null));
                        slotOfName.Add(name, slot);
                    }
                    return slot;
                }

                if (replaceNullText)
                {
                    // Fill: a NULL placeholder's text is replaced as a resolved one's is, wherever it appears
                    // with both markers, and with one wherever it doesn't go on into a longer name. So a stray
                    // "{{" before a second use of that placeholder can't hide it. A text that lies inside a longer
                    // one goes first, so a stray marker's span that holds a placeholder is gone by the time its
                    // own text comes up; the others keep the order the pattern matched them in.
                    var oneMarkerNullText = new Dictionary<string, (int Slot, bool OpenEmpty, bool CloseEmpty)>(comparer);
                    var ordered = new List<(string Text, string Name, bool OpenEmpty, bool CloseEmpty)>();
                    var emitted = new bool[texts.Count];
                    void Emit(int i)
                    {
                        if (emitted[i]) return;
                        emitted[i] = true;
                        for (int j = 0; j < texts.Count; j++)
                            if (texts[j].Text.Length < texts[i].Text.Length && texts[i].Text.IndexOf(texts[j].Text, comparison) >= 0)
                                Emit(j);
                        ordered.Add(texts[i]);
                    }
                    for (int i = 0; i < texts.Count; i++)
                        Emit(i);
                    foreach (var text in ordered)
                    {
                        if (absentTexts.Contains(text.Text)) continue;
                        if (!OccursOutsideSlots(query, text.Text, comparison, delimiter))
                        {
                            absentTexts.Add(text.Text);
                            continue;
                        }
                        if (!text.OpenEmpty && !text.CloseEmpty)
                            query = ReplaceOutsideSlots(query, text.Text, Slot(NullSlot(text.Name)), comparison, delimiter);
                        else
                            oneMarkerNullText[text.Text] = (NullSlot(text.Name), text.OpenEmpty, text.CloseEmpty);
                    }
                    query = ReplaceOneMarkerText(query, oneMarkerNullText, Slot, comparison, delimiter, oneMarkerPlaceholders);
                }

                var replaced = new System.Text.StringBuilder(query.Length);
                int position = 0;
                foreach (var m in MatchesOutsideSlots(query, queryParamsRegex, delimiter, out _))
                {
                    var name = m.Groups["param"].Value;
                    if (!names.Contains(name)) continue;
                    var (start, end) = PlaceholderSpan(m);
                    replaced.Append(query, position, start - position);
                    replaced.Append(Slot(NullSlot(name)));
                    position = end;
                }
                replaced.Append(query, position, query.Length - position);
                query = replaced.ToString();
            }

            // A name a set filled from one source has one slot, though a claim (see Claimants) can leave it two:
            // the claimed placeholder's and the set's own. They hold the same value, so they are made one, for
            // one SQL parameter and one valueConverter call. A slot the query doesn't use (one a claim took all
            // of a text's uses from) is dropped, so it makes no unused parameter.
            var used = new bool[slots.Count];
            FillSlots(query, delimiter, slot => { used[slot] = true; return ""; });
            var slotOfSourceName = new Dictionary<(int Source, string Name), int>(new SourceNameComparer(comparer));
            var newIndex = new int[slots.Count];
            var merged = new List<(string Name, int Source, object? Value)>();
            for (int i = 0; i < slots.Count; i++)
            {
                if (!used[i]) continue;
                if (!slotOfSourceName.TryGetValue((slots[i].Source, slots[i].Name), out var index))
                {
                    index = merged.Count;
                    merged.Add(slots[i]);
                    slotOfSourceName[(slots[i].Source, slots[i].Name)] = index;
                }
                newIndex[i] = index;
            }
            if (merged.Count < slots.Count)
            {
                query = FillSlots(query, delimiter, slot => Slot(newIndex[slot]));
                slots = merged;
            }

            slotText = query;
            slotDelimiter = delimiter;
            return slots;
        }

        private sealed class SourceNameComparer : IEqualityComparer<(int Source, string Name)>
        {
            private readonly StringComparer _names;
            public SourceNameComparer(StringComparer names) => _names = names;
            public bool Equals((int Source, string Name) x, (int Source, string Name) y) => x.Source == y.Source && _names.Equals(x.Name, y.Name);
            public int GetHashCode((int Source, string Name) x) => x.Source * 397 ^ _names.GetHashCode(x.Name);
        }

        /// <summary>The sources with one pattern string, read as one in the place of the last of them.</summary>
        private sealed class SourceGroup
        {
            public SourceGroup(string? pattern, StringComparer comparer)
            {
                Pattern = pattern;
                Model = new Dictionary<string, object?>(comparer);
                SupplierOf = new Dictionary<string, int>(comparer);
            }

            public string? Pattern { get; }
            public List<(int Position, IDictionary<string, object>? Model)> Members { get; } = new();
            /// <summary>Each name's value: that of the latest source that has the name, even a null one.</summary>
            public Dictionary<string, object?> Model { get; }
            /// <summary>The place in the list of the source each name's value comes from.</summary>
            public Dictionary<string, int> SupplierOf { get; }
            /// <summary>The place in the list of the group's last source.</summary>
            public int Last { get; set; }
            /// <summary>The place in the list of the group's last source with a non-null value: no claim comes from further up.</summary>
            public int LatestWithAValue { get; set; }
        }

        /// <summary>A placeholder's slot that a group read later may still take (see ResolveMarkers).</summary>
        private sealed class SlotClaim
        {
            public SlotClaim(int slot, int supplier, List<(int Group, string Name)> claimants)
            {
                Slot = slot;
                Supplier = supplier;
                Claimants = claimants;
            }

            public int Slot { get; }
            /// <summary>The place in the list of the source whose value the slot holds now.</summary>
            public int Supplier { get; set; }
            /// <summary>The groups read later whose pattern matched the placeholder, and the name each read there.</summary>
            public List<(int Group, string Name)> Claimants { get; }
        }

        /// <summary>How a group read later reads the query as it stands when another group reads (see ResolveMarkers).</summary>
        private sealed class LaterSetReading
        {
            public LaterSetReading(StringComparer comparer)
            {
                ByText = new Dictionary<string, (string Name, List<(int, int)> Anchors)>(comparer);
                OneMarkerTextSeen = new HashSet<string>(comparer);
            }

            /// <summary>
            /// The texts with both markers that its pass replaces wherever they appear, with the name it reads and
            /// the places that make it do so (its anchors).
            /// </summary>
            public Dictionary<string, (string Name, List<(int, int)> Anchors)> ByText { get; }
            /// <summary>Its matches outside slots, by place, with the name it reads there and whether the match has both markers.</summary>
            public Dictionary<(int, int), (string Name, bool BothMarkers)> BySpan { get; } = new();
            /// <summary>The texts of its one-marker matches, once each, which its guarded pass replaces (see ReplaceOneMarkerText).</summary>
            public List<(string Text, string Name, bool OpenEmpty, bool CloseEmpty)> OneMarkerTexts { get; } = new();
            public HashSet<string> OneMarkerTextSeen { get; }
            /// <summary>Its one-marker placeholders, once each, which a shorter one's text isn't replaced inside.</summary>
            public List<string> OneMarkerPlaceholders { get; } = new();
            /// <summary>The same by length (see ByLength), built when first needed.</summary>
            public Dictionary<int, HashSet<string>>? OneMarkerPlaceholdersByLength { get; set; }
        }

        /// <summary>Places in a text, each from a start to an end, that say whether any of them overlaps a place.</summary>
        private sealed class PlaceSet
        {
            private readonly int[] _starts;
            // The furthest end among the places up to each one, in order of their starts.
            private readonly int[] _furthestEnds;

            public PlaceSet(List<(int Start, int End)> places)
            {
                places.Sort((a, b) => a.Start.CompareTo(b.Start));
                _starts = new int[places.Count];
                _furthestEnds = new int[places.Count];
                int furthest = int.MinValue;
                for (int i = 0; i < places.Count; i++)
                {
                    _starts[i] = places[i].Start;
                    _furthestEnds[i] = furthest = Math.Max(furthest, places[i].End);
                    if (Merged.Count > 0 && places[i].Start < Merged[Merged.Count - 1].End)
                        Merged[Merged.Count - 1] = (Merged[Merged.Count - 1].Start, Math.Max(Merged[Merged.Count - 1].End, places[i].End));
                    else
                        Merged.Add(places[i]);
                }
            }

            /// <summary>The places, those that overlap made one, in order.</summary>
            public List<(int Start, int End)> Merged { get; } = new();

            public bool Overlaps(int start, int end)
            {
                // The places that start before the end given: one of them overlaps if it ends after the start.
                int low = 0, high = _starts.Length;
                while (low < high)
                {
                    int middle = low + (high - low) / 2;
                    if (_starts[middle] < end) low = middle + 1;
                    else high = middle;
                }
                return low > 0 && _furthestEnds[low - 1] > start;
            }
        }

        /// <summary>The first of the places (in order, none overlapping another) that ends after <paramref name="position"/>.</summary>
        private static int FirstEndingAfter(List<(int Start, int End)> places, int position)
        {
            int low = 0, high = places.Count;
            while (low < high)
            {
                int middle = low + (high - low) / 2;
                if (places[middle].End <= position) low = middle + 1;
                else high = middle;
            }
            return low;
        }

        /// <summary>
        /// Whether the pattern takes <paramref name="text"/>, all of it, for a placeholder of
        /// <paramref name="name"/>: on its own, or between spaces for a pattern that wants a character
        /// before or after its markers.
        /// </summary>
        private static bool IsPlaceholderOf(string text, string? pattern, string name, StringComparer comparer)
        {
            if (pattern is null) return false;
            foreach (var (candidate, offset) in new[] { (text, 0), (" " + text + " ", 1) })
                foreach (Match m in Regex.Matches(candidate, pattern))
                {
                    var (start, end) = PlaceholderSpan(m);
                    if (start == offset && end == offset + text.Length && comparer.Equals(m.Groups["param"].Value, name))
                        return true;
                }
            return false;
        }

        /// <summary>Records that a match at <paramref name="anchor"/> makes a set's pass replace <paramref name="text"/> everywhere.</summary>
        private static void AddAnchor(Dictionary<string, (string Name, List<(int, int)> Anchors)> byText, string text, string name, (int, int) anchor)
        {
            if (byText.TryGetValue(text, out var entry))
                entry.Anchors.Add(anchor);
            else
                byText[text] = (name, new List<(int, int)> { anchor });
        }

        /// <summary>The placeholders with both markers that the pattern matches in the query as written, once each.</summary>
        private static List<(string Text, string Name, bool OpenEmpty, bool CloseEmpty)> OriginalReading(
            string originalQuery, string pattern, StringComparer comparer)
        {
            var result = new List<(string Text, string Name, bool OpenEmpty, bool CloseEmpty)>();
            var seen = new HashSet<string>(comparer);
            foreach (Match m in Regex.Matches(originalQuery, pattern))
            {
                var name = m.Groups["param"].Value;
                if (string.IsNullOrEmpty(name) || m.Groups["open_marker"].Length == 0 || m.Groups["close_marker"].Length == 0)
                    continue;
                var (start, end) = PlaceholderSpan(m);
                var text = originalQuery.Substring(start, end - start);
                if (seen.Add(text))
                    result.Add((text, name, false, false));
            }
            return result;
        }

        /// <summary>
        /// The pattern's matches in <paramref name="text"/>, without those whose placeholder part (see
        /// <see cref="PlaceholderSpan(Match)"/>) touches a slot already written. Such a match is skipped and the
        /// search goes on after it, so the markers pair as they do in the text with its slots. A later set's
        /// pattern then can't read a slot's index as a name (\d+, \w+), and a stray "{{" that pairs with a
        /// marker past a slot doesn't make the text between them, slot included, a placeholder of its own.
        /// </summary>
        private static List<Match> MatchesOutsideSlots(string text, string pattern, char delimiter, out bool skipped)
        {
            var result = new List<Match>();
            skipped = false;
            // The slot delimiters' positions, in order: a slot runs from an even-numbered one to the next.
            var delimiters = new List<int>();
            for (int i = text.IndexOf(delimiter); i >= 0; i = text.IndexOf(delimiter, i + 1))
                delimiters.Add(i);

            int DelimitersBefore(int index)
            {
                var at = delimiters.BinarySearch(index);
                return at >= 0 ? at : ~at;
            }

            foreach (Match m in Regex.Matches(text, pattern))
            {
                if (delimiters.Count > 0)
                {
                    var (start, end) = PlaceholderSpan(m);
                    int before = DelimitersBefore(start);
                    if (before % 2 == 1 || DelimitersBefore(end) != before)
                    {
                        skipped = true;
                        continue;
                    }
                }
                result.Add(m);
            }
            return result;
        }

        /// <summary>Whether the text before <paramref name="index"/> leaves it inside a slot, counted on from <paramref name="scanned"/>.</summary>
        private static bool InsideSlotAt(string query, char delimiter, int index, ref int scanned, ref bool insideSlot)
        {
            for (int d = query.IndexOf(delimiter, scanned, index - scanned); d >= 0; d = query.IndexOf(delimiter, scanned, index - scanned))
            {
                insideSlot = !insideSlot;
                scanned = d + 1;
            }
            scanned = index;
            return insideSlot;
        }

        /// <summary>
        /// Replaces each occurrence of <paramref name="text"/> that is outside the slots already written.
        /// The text holds no delimiter, so an occurrence that starts inside a slot (a marker made of digits,
        /// say) lies wholly inside it, and stays.
        /// </summary>
        private static string ReplaceOutsideSlots(string query, string text, string replacement, StringComparison comparison, char delimiter)
        {
            // A slot holds the delimiter and digits only, so only a text made of digits can lie inside one.
            if (!IsDigits(text)) return query.Replace(text, replacement, comparison);
            int index = query.IndexOf(text, comparison);
            if (index < 0) return query;
            var replaced = new System.Text.StringBuilder(query.Length);
            int position = 0, scanned = 0;
            bool insideSlot = false;
            while (index >= 0)
            {
                replaced.Append(query, position, index - position);
                if (InsideSlotAt(query, delimiter, index, ref scanned, ref insideSlot)) replaced.Append(query, index, text.Length);
                else replaced.Append(replacement);
                position = index + text.Length;
                index = query.IndexOf(text, position, comparison);
            }
            replaced.Append(query, position, query.Length - position);
            return replaced.ToString();
        }

        /// <summary>Whether the text is made of ASCII digits only, as a slot's index is.</summary>
        private static bool IsDigits(string text)
        {
            foreach (var c in text)
                if (c < '0' || c > '9') return false;
            return text.Length > 0;
        }

        /// <summary>The places <paramref name="text"/> occurs outside the slots already written.</summary>
        private static List<(int, int)> OccurrencesOutsideSlots(string query, string text, StringComparison comparison, char delimiter)
        {
            var result = new List<(int, int)>();
            int scanned = 0;
            bool insideSlot = false, digits = IsDigits(text);
            for (int index = query.IndexOf(text, comparison); index >= 0; index = query.IndexOf(text, index + text.Length, comparison))
                if (!(digits && InsideSlotAt(query, delimiter, index, ref scanned, ref insideSlot)))
                    result.Add((index, index + text.Length));
            return result;
        }

        /// <summary>Whether <paramref name="text"/> occurs outside the slots already written.</summary>
        private static bool OccursOutsideSlots(string query, string text, StringComparison comparison, char delimiter)
        {
            if (!IsDigits(text)) return query.IndexOf(text, comparison) >= 0;
            int scanned = 0;
            bool insideSlot = false;
            for (int index = query.IndexOf(text, comparison); index >= 0; index = query.IndexOf(text, index + 1, comparison))
                if (!InsideSlotAt(query, delimiter, index, ref scanned, ref insideSlot))
                    return true;
            return false;
        }

        /// <summary>
        /// Named path, a pattern with one marker (":name"): as with two markers, a placeholder's text is
        /// also replaced where the pattern didn't match it ("1-:a" under a pattern that lists the
        /// characters allowed before the colon), compared as names are everywhere else (ordinal, and
        /// ignoring case unless the comparison says otherwise), but not where it goes on into a longer name:
        /// a name character on the side without a marker (":a" leaves ":ab" alone, and "a$" leaves "ba$"),
        /// or a longer placeholder the pattern matched (":date" leaves ":date-from" alone under a pattern
        /// that allows "-" in names). Nor inside a slot already written.
        /// </summary>
        private static string ReplaceOneMarkerText(
            string query,
            Dictionary<string, (int Slot, bool OpenEmpty, bool CloseEmpty)> oneMarkerText,
            Func<int, string> slot,
            StringComparison comparison,
            char delimiter,
            List<string> oneMarkerPlaceholders)
        {
            var byLength = ByLength(oneMarkerPlaceholders, comparison);
            foreach (var text in oneMarkerText)
            {
                var places = OneMarkerTextPlaces(query, text.Key, text.Value.OpenEmpty, text.Value.CloseEmpty,
                    comparison, delimiter, byLength);
                if (places.Count == 0) continue;
                var replaced = new System.Text.StringBuilder(query.Length);
                int position = 0;
                foreach (var (start, end) in places)
                {
                    replaced.Append(query, position, start - position);
                    replaced.Append(slot(text.Value.Slot));
                    position = end;
                }
                replaced.Append(query, position, query.Length - position);
                query = replaced.ToString();
            }
            return query;
        }

        /// <summary>The one-marker placeholders by length, as <see cref="OneMarkerTextPlaces"/> looks them up.</summary>
        private static Dictionary<int, HashSet<string>> ByLength(IEnumerable<string> oneMarkerPlaceholders, StringComparison comparison)
        {
            var byLength = new Dictionary<int, HashSet<string>>();
            foreach (var x in oneMarkerPlaceholders)
            {
                if (!byLength.TryGetValue(x.Length, out var ofLength))
                    byLength[x.Length] = ofLength = new HashSet<string>(
                        comparison == StringComparison.Ordinal ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase);
                ofLength.Add(x);
            }
            return byLength;
        }

        /// <summary>
        /// The places <see cref="ReplaceOneMarkerText"/> replaces a one-marker placeholder's text, left to right.
        /// <paramref name="oneMarkerPlaceholders"/> holds the pattern's one-marker placeholders by length
        /// (<see cref="ByLength"/>): an occurrence that is the start of a longer one (or, with a close marker
        /// only, the end of one) goes on into it.
        /// </summary>
        private static List<(int Start, int End)> OneMarkerTextPlaces(
            string query,
            string text,
            bool openEmpty,
            bool closeEmpty,
            StringComparison comparison,
            char delimiter,
            Dictionary<int, HashSet<string>> oneMarkerPlaceholders)
        {
            var places = new List<(int Start, int End)>();
            int position = 0, scanned = 0;
            bool insideSlot = false, digits = IsDigits(text);
            int index;
            while ((index = query.IndexOf(text, position, comparison)) >= 0)
            {
                int end = index + text.Length;
                bool continued = (digits && InsideSlotAt(query, delimiter, index, ref scanned, ref insideSlot))
                    || (openEmpty && index > 0 && IsNameCharacter(query[index - 1]))
                    || (closeEmpty && end < query.Length && IsNameCharacter(query[end]))
                    || oneMarkerPlaceholders.Any(x => x.Key > text.Length && (closeEmpty
                        ? index + x.Key <= query.Length && x.Value.Contains(query.Substring(index, x.Key))
                        : end - x.Key >= 0 && x.Value.Contains(query.Substring(end - x.Key, x.Key))));
                if (!continued) places.Add((index, end));
                position = end;
            }
            return places;
        }

        /// <summary>A character a regular expression's \w matches: a letter, a decimal digit, a nonspacing mark or a connector such as _.</summary>
        private static bool IsNameCharacter(char c)
        {
            switch (CharUnicodeInfo.GetUnicodeCategory(c))
            {
                case UnicodeCategory.UppercaseLetter:
                case UnicodeCategory.LowercaseLetter:
                case UnicodeCategory.TitlecaseLetter:
                case UnicodeCategory.ModifierLetter:
                case UnicodeCategory.OtherLetter:
                case UnicodeCategory.NonSpacingMark:
                case UnicodeCategory.DecimalDigitNumber:
                case UnicodeCategory.ConnectorPunctuation:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// The part of a pattern's match that a placeholder's slot replaces: its markers and name when it
        /// has a marker, so text the pattern matched around them stays (the character it requires before
        /// ":name", which keeps "::" casts out); the whole match when it has neither marker, since its
        /// text would be a bare name (a column).
        /// </summary>
        private static (int Start, int End) PlaceholderSpan(Match m) =>
            PlaceholderSpan(m, m.Groups["open_marker"], m.Groups["close_marker"], m.Groups["param"]);

        private static (int Start, int End) PlaceholderSpan(Match m, Group open, Group close, Group param)
        {
            if (open.Length == 0 && close.Length == 0) return (m.Index, m.Index + m.Length);
            return (open.Length > 0 ? open.Index : param.Index,
                    close.Length > 0 ? close.Index + close.Length : param.Index + param.Length);
        }

        /// <summary>
        /// Replaces each slot that <see cref="ResolveMarkers"/> wrote with the text <paramref name="textOfSlot"/>
        /// returns for its index, called once per slot, left to right.
        /// </summary>
        internal static string FillSlots(string slotText, char slotDelimiter, Func<int, string> textOfSlot)
        {
            var result = new System.Text.StringBuilder(slotText.Length);
            int position = 0;
            int open;
            while ((open = slotText.IndexOf(slotDelimiter, position)) >= 0)
            {
                int close = slotText.IndexOf(slotDelimiter, open + 1);
                result.Append(slotText, position, open - position);
                result.Append(textOfSlot(int.Parse(slotText.Substring(open + 1, close - open - 1), CultureInfo.InvariantCulture)));
                position = close + 1;
            }
            result.Append(slotText, position, slotText.Length - position);
            return result.ToString();
        }

        private static string SqlParameterName(string parameterPrefix, int source, string cleanedParamName) =>
            DefaultParameterTemplate.Replace("{{DefaultParameterPrefix}}", parameterPrefix)
                .Replace("{{ParameterCount}}", source.ToString(CultureInfo.InvariantCulture))
                .Replace("{{ParameterName}}", cleanedParamName);

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
        /// <param name="closeConnectionOnExit">If true, disposing the result disposes the connection. SqlClient, Npgsql and MySqlConnector can't open a disposed connection again; ExecuteQuery only closes it.</param>
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
                dbc, query, queryParams as IEnumerable<DbQueryParams>, closeConnectionOnExit, cToken).ConfigureAwait(false);
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
            try
            {
                // The result is made with closeConnectionOnExit false, because disposing it with
                // true DISPOSES the connection, and SqlClient, Npgsql and MySqlConnector can't open
                // a disposed connection again. The flag means "close", as on ExecuteQuery.
                var result = await ExecuteQueryAsync(dbc, query, queryParams, queryParamsRegex, false, cToken).ConfigureAwait(false);
                await using (((IAsyncDisposable)result).ConfigureAwait(false))
                {
                    // AsAsyncEnumerable() disambiguates the ConfigureAwait call — the result implements
                    // both IAsyncEnumerable<T> and IAsyncDisposable, so calling ConfigureAwait directly
                    // on it is ambiguous between the two TaskAsyncEnumerableExtensions overloads.
                    await foreach (var _ in result.AsAsyncEnumerable().ConfigureAwait(false)) ; // Consume the enumerable to execute the command
                }
            }
            finally
            {
                // Closed here, not through the result, so the flag is honoured when the first
                // statement fails too, before any result exists. Before 10.1.0.10 it was ignored.
                if (closeConnectionOnExit && dbc?.Connection is not null)
                    await dbc.Connection.EnsureClosedAsync(CancellationToken.None).ConfigureAwait(false);
            }
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
        /// <param name="closeConnectionOnExit">If true, disposing the result disposes the connection. SqlClient, Npgsql and MySqlConnector can't open a disposed connection again; ExecuteQuery only closes it.</param>
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
            var dynamicResult = await ExecuteQueryAsync(dbc, query, queryParams, queryParamsRegex, closeConnectionOnExit, cToken).ConfigureAwait(false);
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
        /// <param name="closeConnectionOnExit">If true, disposing the result disposes the connection. SqlClient, Npgsql and MySqlConnector can't open a disposed connection again; ExecuteQuery only closes it.</param>
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
            // Do NOT use 'using' here - the command must stay alive until the result is disposed
            // The result will take ownership of the command and dispose it when the result is disposed
            DbCommand dbc = con.CreateCommand();
            try
            {
                if (commandTimeout.HasValue)
                    dbc.CommandTimeout = commandTimeout.Value;
                var result = await ExecuteQueryAsync(dbc, query, queryParams, queryParamsRegex, closeConnectionOnExit, cToken).ConfigureAwait(false);
                // Return a new result that takes ownership of the command
                return new DbAsyncQueryResult<dynamic>(result.AsAsyncEnumerable(), result.Reader, dbc, result.Connection, closeConnectionOnExit);
            }
            catch
            {
                // Only dispose the command if we fail before returning the result
                await dbc.DisposeAsync().ConfigureAwait(false);
                throw;
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

            // Pass through the command ownership to the sync result
            return new DbQueryResult<dynamic>(asyncResult.AsAsyncEnumerable(), asyncResult.Reader, asyncResult.Command, asyncResult.Connection, closeConnectionOnExit);
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
            try
            {
                // Disposing the result disposes the command created for this call. It is made with
                // closeConnectionOnExit false, because disposing it with true DISPOSES the
                // connection, which SqlClient, Npgsql and MySqlConnector can't open again.
                var result = await ExecuteQueryAsync(con, query, queryParams, queryParamsRegex, commandTimeout, false, cToken).ConfigureAwait(false);
                await using (((IAsyncDisposable)result).ConfigureAwait(false))
                {
                    await foreach (var _ in result.AsAsyncEnumerable().ConfigureAwait(false)) ; // Consume the enumerable to execute the command
                }
            }
            finally
            {
                // Closed here, not through the result, so the flag is honoured when the first
                // statement fails too, before any result exists. Before 10.1.0.10 it was ignored.
                if (closeConnectionOnExit)
                    await con.EnsureClosedAsync(CancellationToken.None).ConfigureAwait(false);
            }
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
        /// <param name="closeConnectionOnExit">If true, disposing the result disposes the connection. SqlClient, Npgsql and MySqlConnector can't open a disposed connection again; ExecuteQuery only closes it.</param>
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
            // Do NOT use 'using' here - the command must stay alive until the result is disposed
            // The result will take ownership of the command and dispose it when the result is disposed
            DbCommand dbc = con.CreateCommand();
            try
            {
                if (commandTimeout.HasValue)
                    dbc.CommandTimeout = commandTimeout.Value;
                var result = await ExecuteQueryAsync<T>(dbc, query, queryParams, queryParamsRegex, closeConnectionOnExit, cToken).ConfigureAwait(false);
                // Return a new result that takes ownership of the command
                return new DbAsyncQueryResult<T>(result.AsAsyncEnumerable(), result.Reader, dbc, result.Connection, closeConnectionOnExit);
            }
            catch
            {
                // Only dispose the command if we fail before returning the result
                await dbc.DisposeAsync().ConfigureAwait(false);
                throw;
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

            // Pass through the command ownership to the sync result
            return new DbQueryResult<T>(asyncResult.AsAsyncEnumerable(), asyncResult.Reader, asyncResult.Command, asyncResult.Connection, closeConnectionOnExit);
        }
        #endregion



        #endregion





        /// <summary>
        /// Converts dynamic async enumerable to typed async enumerable
        /// </summary>
        internal static async IAsyncEnumerable<T> ConvertToType<T>(IAsyncEnumerable<dynamic> source)
        {
            await foreach (var item in source.ConfigureAwait(false))
            {
                // A NULL in a single unnamed column arrives as null. It is still a row, so it maps
                // to default(T), as a NULL in a named column does. Passed to the mapper, a dynamic
                // null binds to the overload that returns null, and the row was dropped.
                if ((object?)item is null)
                {
                    yield return default!;
                    continue;
                }

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
