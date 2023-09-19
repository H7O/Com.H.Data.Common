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

        private readonly static DataMapper _mapper = new();

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
            if (conn is null) throw new ArgumentNullException(nameof(conn));
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
            if (conn is null) throw new ArgumentNullException(nameof(conn));
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


        #region removed
        //        private static async IAsyncEnumerable<T?> ExecuteQueryIDictionaryAsync<T>(
        //            this DbCommand dbc,
        //            string query,
        //            IDictionary<string, object>? queryParams = null,
        //            [EnumeratorCancellation] CancellationToken cToken = default,
        //            string openMarker = "{{",
        //            string closeMarker = "}}",
        //            bool closeConnectionOnExit = false,
        //            string queryParamsRegex = "(?<param>.*?)?"
        //            )
        //        {

        //            if (dbc == null) throw new ArgumentNullException(nameof(dbc));
        //            if (string.IsNullOrEmpty(query)) throw new ArgumentNullException(nameof(query));
        //            var conn = dbc.Connection;
        //            if (conn == null) throw new ArgumentNullException(nameof(conn));
        //            bool cont = true;
        //            DbDataReader? reader = null;
        //            DbCommand? command = null;
        //            try
        //            {
        //                await conn.EnsureOpenAsync(cToken);
        //                cToken.ThrowIfCancellationRequested();
        //                // extract placeholder parameter names (without the markers) from the SQL query
        //                var queryPlaceholders = Regex.Matches(query, openMarker + queryParamsRegex + closeMarker)
        //                    .Cast<Match>()
        //                    .Select(x => x.Groups["param"].Value)
        //                    .Where(x => !string.IsNullOrEmpty(x))
        //                    .Select(x => x).Distinct().ToList();

        //                command = conn.CreateCommand();
        //                command.CommandType = CommandType.Text;


        //                if (queryPlaceholders.Count > 0)
        //                {
        //                    // Join the placeholders that are found in the SQL Query with their corresponding
        //                    // values that are passed in queryParams
        //                    // by doing a left join on SQL Query placeholders = keys of queryParams
        //                    var joined = queryPlaceholders
        //                        .LeftJoin(queryParams ?? new Dictionary<string, object>(),
        //                        pl => pl.ToUpper(CultureInfo.InvariantCulture),
        //                        p => p.Key.ToUpper(CultureInfo.InvariantCulture),
        //                        // returns placeholders and their values, and if a placeholder doesn't have
        //                        // a corresponding value, return DbNull for that placeholder
        //                        (pl, p) => new { k = pl, v = p.Value??DBNull.Value }).ToList();

        //                    foreach (var item in joined)
        //                    {
        //                        // Replace the placeholders in the SQL Query with
        //                        // prepared declared SQL variables using the format
        //                        // @vxv_<variable name>.
        //                        // e.g. if the placeholder in the SQL Query was `{{date}}`
        //                        // the replacement prepared statement declared variable would be
        //                        // @vxv_date
        //                        query = query
        //                        .Replace(openMarker + item.k + closeMarker,
        //                            "@vxv_" + item.k, true,
        //                            CultureInfo.InvariantCulture);
        //                        var p = command.CreateParameter();
        //                        p.ParameterName = "@vxv_" + item.k;
        //                        p.Value = item.v;
        //                        command.Parameters.Add(p);
        //                    }

        //                }

        //                command.CommandText = query;

        //                reader = await command.ExecuteReaderAsync(cToken);
        //                cToken.ThrowIfCancellationRequested();

        //            }
        //            catch (Exception ex)
        //            {
        //                if (reader is not null)
        //                    await reader.EnsureClosedAsync();
        //                if (closeConnectionOnExit)
        //                {
        //                    await conn.EnsureClosedAsync(cToken);
        //                    cToken.ThrowIfCancellationRequested();
        //                }
        //                throw new Exception(ex.GenerateError(command, query, queryParams));
        //            }

        //            if (reader.HasRows)
        //            {
        //                while (cont)
        //                {
        //                    try
        //                    {
        //                        cont = await reader.ReadAsync(cToken);
        //                        cToken.ThrowIfCancellationRequested();
        //                        if (!cont) break;
        //                    }
        //                    catch
        //                    {
        //                        await reader.EnsureClosedAsync();
        //                        if (closeConnectionOnExit) await conn.EnsureClosedAsync(cToken);
        //                        throw;
        //                    }
        //                    T? result = (typeof(T) == typeof(string)) ? (T?)(object?)(string?)null : Activator.CreateInstance<T>();
        //                    var joined =
        //                    typeof(T).GetCachedProperties()
        //                        .LeftJoin(
        //                            Enumerable.Range(0, reader.FieldCount)
        //                            .Select(x => new { Name = reader.GetName(x), Value = reader.GetValue(x) }),
        //                            dst => dst.Name.ToUpper(CultureInfo.InvariantCulture),
        //                            // see if schema name was applied
        //                            src => src.Name.ToUpper(CultureInfo.InvariantCulture),
        //                            (dst, src) => new { dst, src })
        //                            ;

        //                    foreach (var item in joined.Where(x => x?.src?.Value is not null))
        //                    {
        //                        try
        //                        {
        //                            // item.src.Value cannot be null here
        //#pragma warning disable CS8602 // Dereference of a possibly null reference.
        //                            item.dst.Info.SetValue(result,
        //                                Convert.ChangeType(item.src.Value,
        //                                item.dst.Info.PropertyType, CultureInfo.InvariantCulture));
        //#pragma warning restore CS8602 // Dereference of a possibly null reference.
        //                        }
        //                        catch { }
        //                    }

        //                    yield return result;
        //                }
        //            }
        //            await reader.EnsureClosedAsync();

        //            if (closeConnectionOnExit) await conn.EnsureClosedAsync(cToken);
        //            yield break;
        //        }
        #endregion


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


        #region async

        #region main implementation
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design",
            "CA1068:CancellationToken parameters must come last",
            Justification = "'closeConnectionOnExit' should be the last parameter as it's the most rarely used parameter")]
        private static async IAsyncEnumerable<dynamic> ExecuteQueryAsyncMain(
        this DbCommand dbc,
        string query,
        IEnumerable<QueryParams>? queryParamList = null,
        [EnumeratorCancellation] CancellationToken cToken = default,
        bool closeConnectionOnExit = false
        )
        {
            if (dbc == null) throw new ArgumentNullException(nameof(dbc));
            if (string.IsNullOrEmpty(query)) throw new ArgumentNullException(nameof(query));
            var conn = dbc.Connection ?? throw new Exception("DbCommand.Connection is null");
            bool cont = true;


            DbDataReader? reader = null;
            DbCommand? command = null;
            try
            {
                await conn.EnsureOpenAsync(cToken);
                cToken.ThrowIfCancellationRequested();
                command = conn.CreateCommand();
                command.CommandType = CommandType.Text;

                // Extract placeholder parameter names (without the markers) from the SQL query.
                // Each QueryParams object in queryParamList has it's own set of markers
                // and also has it's own set of parameters that are passed in the object's DataModel property.
                // Finally, each object in queryParamList also has it's own regular expression pattern
                // in its RegexPattern property telling this method how to extract the parameter names from the SQL query.

                if (queryParamList is not null)
                {
                    int count = 0;
                    // note: reverse the order of the queryParamList
                    // so that the last object in the list has the highest priority (i.e., Last In First Out)
                    foreach (var queryParam in queryParamList.Reverse())
                    {
                        count++;
                        // Note: pass true to GetDataModelParameters to get the parameters in
                        // the reverse order of parameters declared in the DataModel that would go well with the
                        // Last In First Out (LIFO) order of the queryParamList.
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
                            // see if the query parameter name matches a data model field name
                            // if so, then add the query parameter name to the list of placeholders

                            // method 1: using LINQ
                            //var matchingDataModelField = dataModelFields?
                            //    .FirstOrDefault(x => x.Key.Equals(queryVarNameWithoutMarkers, StringComparison.InvariantCultureIgnoreCase));

                            // method 2: using TryGetValue
                            object? matchingDataModelPropertyValue = null;
                            dataModelProperties?.TryGetValue(matchingQueryVar.Name, out matchingDataModelPropertyValue);


                            // if the query parameter name matches a data model field name
                            // then add the query parameter name to the list of placeholders
                            // and also add the value of the data model field to the list of placeholders
                            var sqlParamName = $"@vxv_{count}_" + matchingQueryVar.Name;
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

            }
            catch (Exception ex)
            {
                if (reader is not null)
                    await reader.EnsureClosedAsync();
                if (closeConnectionOnExit)
                    await conn.EnsureClosedAsync(cToken);

                throw new Exception(
                    ex.GenerateError(
                        command,
                        query,
                        command?.Parameters?.ToDictionary()), ex);
            }

            if (reader?.HasRows == true)
            {
                while (cont)
                {
                    try
                    {
                        cont = await reader.ReadAsync(cToken);
                        cToken.ThrowIfCancellationRequested();
                        if (!cont) break;
                    }
                    catch
                    {
                        await reader.EnsureClosedAsync();
                        if (closeConnectionOnExit) await conn.EnsureClosedAsync(cToken);
                        throw;
                    }

                    ExpandoObject result = new();

                    foreach (var item in Enumerable.Range(0, reader.FieldCount)
                            .Select(x => new { Name = reader.GetName(x), Value = reader.GetValue(x) }))
                    {
                        result.TryAdd(item.Name, item.Value);
                    }

                    yield return result;
                }
            }
            cToken.ThrowIfCancellationRequested();
            if (reader is not null)
                await reader.EnsureClosedAsync();
            if (closeConnectionOnExit) await conn.EnsureClosedAsync(cToken);
            yield break;
        }

        #endregion

        #region DbCommand
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design",
            "CA1068:CancellationToken parameters must come last",
            Justification = "'closeConnectionOnExit' should be the last parameter as it's the most rarely used parameter")]

        public static async IAsyncEnumerable<dynamic> ExecuteQueryAsync(
            this DbCommand dbc,
            string query,
            object? queryParams = null,
            string queryParamsRegex = @"(?<open_marker>\{\{)(?<param>.*?)?(?<close_marker>\}\})",
            [EnumeratorCancellation] CancellationToken cToken = default,
            bool closeConnectionOnExit = false
            )
        {
            if (queryParams is not null)
            {
                if (queryParams is not IEnumerable<QueryParams>)
                {
                    queryParams = new List<QueryParams>()
                    {
                        new QueryParams()
                        {
                            DataModel = queryParams,
                            QueryParamsRegex = queryParamsRegex
                        }
                    };
                }
            }

            await foreach (var item in
                ExecuteQueryAsyncMain(dbc, query, (IEnumerable<QueryParams>?)queryParams, cToken, closeConnectionOnExit))
                yield return item;
            yield break;
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design",
    "CA1068:CancellationToken parameters must come last",
        Justification = "'closeConnectionOnExit' should be the last parameter as it's the most rarely used parameter")]
        public static async IAsyncEnumerable<T> ExecuteQueryAsync<T>(
            this DbCommand dbc,
            string query,
            object? queryParams = null,
            string queryParamsRegex = @"(?<open_marker>\{\{)(?<param>.*?)?(?<close_marker>\}\})",
            [EnumeratorCancellation] CancellationToken cToken = default,
            bool closeConnectionOnExit = false
            )
        {
            await foreach (var item in ExecuteQueryAsync(
                dbc,
                query,
                queryParams,
                queryParamsRegex,
                cToken,
                closeConnectionOnExit))
            {
                var converted = _mapper.Map<T>(item);
                if (converted is null) continue;
                yield return converted;
            }
            yield break;
        }
        #endregion

        #region DbConnection
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design",
            "CA1068:CancellationToken parameters must come last",
            Justification = "'closeConnectionOnExit' should be the last parameter as it's the most rarely used parameter")]
        public static async IAsyncEnumerable<dynamic> ExecuteQueryAsync(
            this DbConnection con,
            string query,
            object? queryParams = null,
            string queryParamsRegex = @"(?<open_marker>\{\{)(?<param>.*?)?(?<close_marker>\}\})",
            [EnumeratorCancellation] CancellationToken cToken = default,
            bool closeConnectionOnExit = false
            )
        {
            using (DbCommand dbc = con.CreateCommand())
            {
                await foreach (var item in ExecuteQueryAsync(
                    dbc,
                    query,
                    queryParams,
                    queryParamsRegex,
                    cToken,
                    closeConnectionOnExit))
                    yield return item;
            }
            yield break;
        }


        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design",
    "CA1068:CancellationToken parameters must come last",
        Justification = "'closeConnectionOnExit' should be the last parameter as it's the most rarely used parameter")]

        public static async IAsyncEnumerable<T> ExecuteQueryAsync<T>(
            this DbConnection con,
            string query,
            object? queryParams = null,
            string queryParamsRegex = @"(?<open_marker>\{\{)(?<param>.*?)?(?<close_marker>\}\})",
            [EnumeratorCancellation] CancellationToken cToken = default,
            bool closeConnectionOnExit = false
            )
        {
            await foreach (var item in ExecuteQueryAsync(
                con,
                query,
                queryParams,
                queryParamsRegex,
                cToken,
                closeConnectionOnExit))
            {
                var converted = _mapper.Map<T>(item);
                if (converted is null) continue;
                yield return converted;
            }
            yield break;
        }

        #endregion

        #region connection string


        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design",
        "CA1068:CancellationToken parameters must come last",
        Justification = "'closeConnectionOnExit' should be the last parameter as it's the most rarely used parameter")]
        public static async IAsyncEnumerable<dynamic> ExecuteQueryAsync(
            this string connectionString,
            string query,
            object? queryParams = null,
            string queryParamsRegex = @"(?<open_marker>\{\{)(?<param>.*?)?(?<close_marker>\}\})",
            [EnumeratorCancellation] CancellationToken cToken = default,
            bool closeConnectionOnExit = false
            )
        {
            using (DbConnection con = CreateDbConnection(connectionString))
            {
                await foreach (var item in ExecuteQueryAsync(
                    con,
                    query,
                    queryParams,
                    queryParamsRegex,
                    cToken,
                    closeConnectionOnExit))
                    yield return item;
            }
            yield break;
        }


        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design",
        "CA1068:CancellationToken parameters must come last",
        Justification = "'closeConnectionOnExit' should be the last parameter as it's the most rarely used parameter")]
        public static async IAsyncEnumerable<T> ExecuteQueryAsync<T>(
            this string connectionString,
            string query,
            object? queryParams = null,
            string queryParamsRegex = @"(?<open_marker>\{\{)(?<param>.*?)?(?<close_marker>\}\})",
            [EnumeratorCancellation] CancellationToken cToken = default,
            bool closeConnectionOnExit = false
            )
        {
            using (DbConnection con = CreateDbConnection(connectionString))
            {

                await foreach (var item in ExecuteQueryAsync(
                con,
                query,
                queryParams,
                queryParamsRegex,
                cToken,
                closeConnectionOnExit))
                {
                    var converted = _mapper.Map<T>(item);
                    if (converted is null) continue;
                    yield return converted;
                }
            }
            yield break;
        }


        #endregion

        #endregion


        #region sync

        #region DbCommand
        public static IEnumerable<dynamic> ExecuteQuery(
            this DbCommand dbc,
            string query,
            object? queryParams = null,
            string queryParamsRegex = @"(?<open_marker>\{\{)(?<param>.*?)?(?<close_marker>\}\})",
            bool closeConnectionOnExit = false)
        {
            return dbc.ExecuteQueryAsync(
                query,
                queryParams,
                queryParamsRegex,
                CancellationToken.None,
                closeConnectionOnExit)
                .ToBlockingEnumerable();
        }

        public static IEnumerable<T> ExecuteQuery<T>(
            this DbCommand dbc,
            string query,
            object? queryParams = null,
            string queryParamsRegex = @"(?<open_marker>\{\{)(?<param>.*?)?(?<close_marker>\}\})",
            bool closeConnectionOnExit = false)
        {
            return dbc.ExecuteQueryAsync<T>(
                query,
                queryParams,
                queryParamsRegex,
                CancellationToken.None,
                closeConnectionOnExit)
                .ToBlockingEnumerable();
        }

        #endregion

        #region DbConnection
        public static IEnumerable<dynamic> ExecuteQuery(
            this DbConnection con,
            string query,
            object? queryParams = null,
            string queryParamsRegex = @"(?<open_marker>\{\{)(?<param>.*?)?(?<close_marker>\}\})",
            bool closeConnectionOnExit = false)
        {
            return con.ExecuteQueryAsync(
                query,
                queryParams,
                queryParamsRegex,
                CancellationToken.None,
                closeConnectionOnExit)
                .ToBlockingEnumerable();
        }

        public static IEnumerable<T> ExecuteQuery<T>(
            this DbConnection con,
            string query,
            object? queryParams = null,
            string queryParamsRegex = @"(?<open_marker>\{\{)(?<param>.*?)?(?<close_marker>\}\})",
            bool closeConnectionOnExit = false)
        {
            return con.ExecuteQueryAsync<T>(
                query,
                queryParams,
                queryParamsRegex,
                CancellationToken.None,
                closeConnectionOnExit)
                .ToBlockingEnumerable();
        }
        #endregion

        #region connection string
        public static IEnumerable<dynamic> ExecuteQuery(
            this string connectionString,
            string query,
            object? queryParams = null,
            string queryParamsRegex = @"(?<open_marker>\{\{)(?<param>.*?)?(?<close_marker>\}\})",
            bool closeConnectionOnExit = false)
        {
            return connectionString.ExecuteQueryAsync(
                query,
                queryParams,
                queryParamsRegex,
                CancellationToken.None,
                closeConnectionOnExit)
                .ToBlockingEnumerable();
        }

        public static IEnumerable<T> ExecuteQuery<T>(
            this string connectionString,
            string query,
            object? queryParams = null,
            string queryParamsRegex = @"(?<open_marker>\{\{)(?<param>.*?)?(?<close_marker>\}\})",
            bool closeConnectionOnExit = false)
        {
            return connectionString.ExecuteQueryAsync<T>(
                query,
                queryParams,
                queryParamsRegex,
                CancellationToken.None,
                closeConnectionOnExit)
                .ToBlockingEnumerable();
        }

        #endregion

        #endregion

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
