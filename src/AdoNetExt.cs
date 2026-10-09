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
                    var slots = ResolveMarkers(query, queryParamList, replaceText: true, out var slotText, out var slotDelimiter);
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
        /// sets that has a non-null one. Sets with the same pattern string are merged into one first, in
        /// the place of the last of them, the later set's value winning for a name both have, even when
        /// it is null. Only the patterns' matches are replaced, though: the named path also replaces the
        /// text of a placeholder that has a value where its pattern doesn't match it (a use after a stray
        /// "{{" that the pattern pairs with it, or one a pattern skips), so such a use can differ between
        /// the two. A NULL placeholder is replaced only where its pattern matches it, on both paths.
        /// </summary>
        /// <param name="query">The SQL query with placeholders.</param>
        /// <param name="queryParamList">The parameter sets with regex patterns and data models.</param>
        /// <returns>A tuple of (transformedQuery, orderedParameters) where orderedParameters contains
        /// (paramName, value) pairs in query-occurrence order.</returns>
        public static (string TransformedQuery, List<(string Name, object? Value)> OrderedParameters)
            BuildPositionalParameters(string query, IEnumerable<DbQueryParams> queryParamList)
        {
            var slots = ResolveMarkers(query, queryParamList, replaceText: false, out var slotText, out var slotDelimiter);
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
        /// Sets with the same pattern string are first merged into one by ReduceToUnique, in the place of
        /// the last of them, the later set's value winning for a name both have, even when it is null.
        /// The resulting parameter sets are
        /// read last to first. A placeholder takes the value of the latest set whose
        /// pattern matches it and that has a non-null value under its name (compared ignoring case). A set
        /// without one leaves the placeholder for the set before it: several sets' patterns can match the
        /// same placeholder ({{name}} for both a JSON body and a query string, say), and each placeholder
        /// is replaced only once. A placeholder that no set has a value for becomes NULL, under the latest
        /// set whose pattern matched it.
        /// </para>
        /// <para>
        /// The placeholders of one set whose names differ only in case share one slot. In the returned
        /// text each slot is written as its index between two <paramref name="slotDelimiter"/> characters:
        /// a private-use character the query doesn't contain, so text that looks like a slot can't come
        /// from the query, and the patterns of the sets read after it don't see a parameter name.
        /// </para>
        /// <para>
        /// With <paramref name="replaceText"/> (the named path), for a pattern with an open and a close
        /// marker, a resolved placeholder's text (open marker, name, close marker) becomes its slot wherever
        /// it appears, ignoring case. Otherwise, and for NULL placeholders, the part of each of the
        /// pattern's matches that <see cref="PlaceholderSpan"/> gives does; on the named path, the text of
        /// a resolved placeholder with one marker then also does wherever no name character continues it
        /// (<see cref="ReplaceOneMarkerText"/>). A NULL placeholder's text where the pattern skips it stays.
        /// </para>
        /// </remarks>
        private static List<(string Name, int Source, object? Value)> ResolveMarkers(
            string query,
            IEnumerable<DbQueryParams> queryParamList,
            bool replaceText,
            out string slotText,
            out char slotDelimiter)
        {
            var delimiter = '\uE000';
            while (query.IndexOf(delimiter) >= 0)
            {
                if (delimiter == '\uF8FF')
                    throw new ArgumentException("The query contains every Unicode private-use character.", nameof(query));
                delimiter++;
            }
            string Slot(int index) => delimiter + index.ToString(CultureInfo.InvariantCulture) + delimiter;

            var slots = new List<(string Name, int Source, object? Value)>();
            // Per set, the placeholder names it had no value for.
            var unresolved = new List<(string QueryParamsRegex, int Source, HashSet<string> Names)>();
            int source = 0;
            foreach (var queryParam in queryParamList.ReduceToUnique(true))
            {
                source++;
                var dataModelProperties = queryParam.DataModel?.GetDataModelParameters(true);
                // The slot of each placeholder name this set has resolved, or -1 for one it left for the next set.
                var slotOfName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                var unresolvedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                // The text of each placeholder this set resolved, as written (open marker, name, close marker).
                var resolvedText = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                bool everyResolvedMatchHasBothMarkers = true;
                // The same for placeholders with one marker, and which side has none.
                var oneMarkerText = new Dictionary<string, (int Slot, bool OpenEmpty, bool CloseEmpty)>(StringComparer.OrdinalIgnoreCase);
                var matches = Regex.Matches(query, queryParam.QueryParamsRegex);
                foreach (Match m in matches)
                {
                    var name = m.Groups["param"].Value;
                    if (string.IsNullOrEmpty(name)) continue;
                    if (!slotOfName.TryGetValue(name, out var slot))
                    {
                        object? value = null;
                        dataModelProperties?.TryGetValue(name, out value);
                        if (value is null)
                        {
                            slot = -1;
                            unresolvedNames.Add(name);
                        }
                        else
                        {
                            slot = slots.Count;
                            slots.Add((name, source, value));
                        }
                        slotOfName.Add(name, slot);
                    }
                    if (slot < 0) continue;
                    var openMarker = m.Groups["open_marker"].Value;
                    var closeMarker = m.Groups["close_marker"].Value;
                    if (openMarker.Length > 0 && closeMarker.Length > 0)
                        resolvedText[openMarker + name + closeMarker] = slot;
                    else
                    {
                        everyResolvedMatchHasBothMarkers = false;
                        if (openMarker.Length > 0 || closeMarker.Length > 0)
                            oneMarkerText[openMarker + name + closeMarker] = (slot, openMarker.Length == 0, closeMarker.Length == 0);
                    }
                }

                if (replaceText && everyResolvedMatchHasBothMarkers)
                {
                    // Named path: a resolved placeholder's text is replaced wherever it appears, ignoring
                    // case, not only where the pattern matched it. Queries the pattern pairs oddly rely on
                    // that: a stray "{{" in a literal before a second {{id}} (the pattern pairs them, so
                    // that {{id}} is no match of its own), a pipe operator beside |a| delimiters, or a
                    // pattern that also matches the character before the marker.
                    foreach (var text in resolvedText)
                        query = query.Replace(text.Key, Slot(text.Value), StringComparison.OrdinalIgnoreCase);
                }
                else if (slotOfName.Values.Any(slot => slot >= 0))
                {
                    // The pattern's matches are replaced (see PlaceholderSpan for which part of each).
                    var replaced = new System.Text.StringBuilder(query.Length);
                    int position = 0;
                    foreach (Match m in matches)
                    {
                        var name = m.Groups["param"].Value;
                        if (string.IsNullOrEmpty(name) || slotOfName[name] < 0) continue;
                        var (start, end) = PlaceholderSpan(m);
                        replaced.Append(query, position, start - position);
                        replaced.Append(Slot(slotOfName[name]));
                        position = end;
                    }
                    replaced.Append(query, position, query.Length - position);
                    query = replaced.ToString();

                    if (replaceText)
                    {
                        // Named path, a pattern with a one-marker alternative: its two-marker placeholders
                        // are bound by their text as above, and its one-marker ones by guarded text.
                        foreach (var text in resolvedText)
                            query = query.Replace(text.Key, Slot(text.Value), StringComparison.OrdinalIgnoreCase);
                        query = ReplaceOneMarkerText(query, oneMarkerText, Slot);
                    }
                }
                if (unresolvedNames.Count > 0)
                    unresolved.Add((queryParam.QueryParamsRegex, source, unresolvedNames));
            }

            // What no set had a value for is NULL. Matching again finds only the placeholders still in
            // the query: one a later set resolved is a slot by now, and gets no NULL parameter. Only the
            // pattern's matches become NULL: text the pattern skips (":token" in 'session:token') stays.
            foreach (var (queryParamsRegex, unresolvedSource, names) in unresolved)
            {
                var slotOfName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                query = Regex.Replace(query, queryParamsRegex, m =>
                {
                    var name = m.Groups["param"].Value;
                    if (!names.Contains(name)) return m.Value;
                    if (!slotOfName.TryGetValue(name, out var slot))
                    {
                        slot = slots.Count;
                        slots.Add((name, unresolvedSource, null));
                        slotOfName.Add(name, slot);
                    }
                    var (start, end) = PlaceholderSpan(m);
                    return m.Value.Substring(0, start - m.Index) + Slot(slot) + m.Value.Substring(end - m.Index);
                });
            }

            slotText = query;
            slotDelimiter = delimiter;
            return slots;
        }

        /// <summary>
        /// Named path, a pattern with one marker (":name"): as with two markers, a placeholder's text is
        /// also replaced where the pattern didn't match it ("1-:a" under a pattern that lists the
        /// characters allowed before the colon), ignoring case as names are compared everywhere else
        /// (ordinal), but only where no name character continues it on the side without a marker, so
        /// ":a" leaves ":ab" alone and "a$" leaves "ba$" alone.
        /// </summary>
        private static string ReplaceOneMarkerText(
            string query,
            Dictionary<string, (int Slot, bool OpenEmpty, bool CloseEmpty)> oneMarkerText,
            Func<int, string> slot)
        {
            foreach (var text in oneMarkerText)
            {
                var replaced = new System.Text.StringBuilder(query.Length);
                int position = 0;
                int index;
                while ((index = query.IndexOf(text.Key, position, StringComparison.OrdinalIgnoreCase)) >= 0)
                {
                    int end = index + text.Key.Length;
                    bool continued = (text.Value.OpenEmpty && index > 0 && IsNameCharacter(query[index - 1]))
                        || (text.Value.CloseEmpty && end < query.Length && IsNameCharacter(query[end]));
                    replaced.Append(query, position, index - position);
                    replaced.Append(continued ? query.Substring(index, text.Key.Length) : slot(text.Value.Slot));
                    position = end;
                }
                replaced.Append(query, position, query.Length - position);
                query = replaced.ToString();
            }
            return query;
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
        private static (int Start, int End) PlaceholderSpan(Match m)
        {
            var open = m.Groups["open_marker"];
            var close = m.Groups["close_marker"];
            if (open.Length == 0 && close.Length == 0) return (m.Index, m.Index + m.Length);
            var param = m.Groups["param"];
            return (open.Length > 0 ? open.Index : param.Index,
                    close.Length > 0 ? close.Index + close.Length : param.Index + param.Length);
        }

        /// <summary>
        /// Replaces each slot that <see cref="ResolveMarkers"/> wrote with the text <paramref name="textOfSlot"/>
        /// returns for its index, called once per slot, left to right.
        /// </summary>
        private static string FillSlots(string slotText, char slotDelimiter, Func<int, string> textOfSlot)
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
