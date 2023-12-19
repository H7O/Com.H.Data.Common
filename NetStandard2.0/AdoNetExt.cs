using System.Collections.Generic;
using System;
using System.Data;
using System.Data.Common;
using System.Dynamic;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Threading;

namespace Com.H.Data.Common
{
    public static class AdoNetExt
    {
        private readonly static string _cleanVariableNamesRegex = @"[-\s\.\(\)\[\]\{\}\:\;\,\?\!\#\$\%\^\&\*\+\=\|\\\/\~\`\´\'\""\<\>\=\?\ ]";
        private static readonly Regex _cleanVariableNamesRegexCompiled = new Regex(_cleanVariableNamesRegex, RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline);

        private readonly static DataMapper _mapper = new DataMapper();

        //public static DbConnection CreateDbConnection(this string connStr, string providerName = "Microsoft.Data.SqlClient")
        //{
        //    var csb = new DbConnectionStringBuilder { ConnectionString = connStr };

        //    if (string.IsNullOrEmpty(providerName)
        //        &&
        //        csb.ContainsKey("provider")
        //        )
        //    {
        //        providerName = csb["provider"].ToString() ?? "";
        //    }

        //    if (string.IsNullOrWhiteSpace(providerName))
        //    {
        //        throw new ArgumentNullException(nameof(providerName),
        //            "Provider name cannot be null or empty."
        //            + Environment.NewLine
        //            + " Make sure to define the correct provider in your connection string."
        //            + Environment.NewLine
        //            + @" E.g. ""Provider=Microsoft.Data.SqlClient; Data Source=MySqlServer\MSSQL1;User ID=Admin;Password=;`"""
        //            );
        //    }
        //    try
        //    {
        //        var factory = DbProviderFactories.GetFactory(providerName);
        //        var connection = factory.CreateConnection() ?? throw new Exception($"Failed to create a using '{providerName}' provider."
        //                + " Make sure to define the correct provider in your connection string."
        //                + @" E.g. ""Provider=Microsoft.Data.SqlClient; Data Source=MySqlServer\MSSQL1;User ID=Admin;Password=;`""");
        //        if (csb.ContainsKey("provider")) csb.Remove("provider");
        //        connection.ConnectionString = csb.ConnectionString;
        //        return connection;
        //    }
        //    catch (Exception ex)
        //    {
        //        var errorMsg = $"Perhaps you're missing registering your provider at the start of the application."
        //            + Environment.NewLine
        //            + @" E.g., DbProviderFactories.RegisterFactory(""Microsoft.Data.SqlClient"", Microsoft.Data.SqlClient.SqlClientFactory.Instance);"
        //            + Environment.NewLine
        //            + @" Note: The above example should be done only once during the lifetime of the application."
        //            + Environment.NewLine + Environment.NewLine
        //            + $@" Inner exception: {ex.Message}";
        //        ;
        //        throw new Exception(errorMsg, ex);
        //    }
        //}
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
        public static async Task EnsureClosed(this DbConnection conn, CancellationToken cToken = default)
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

                conn.Close();
            }
        }

        private static void EnsureClosed(this DbDataReader reader)
        {
            if (reader == null) return;
            if (reader.IsClosed) return;
            reader.Close();
        }



        public static IDictionary<string, object> ToDictionary(this DbParameterCollection dbParameterCollection)
        {
            if (dbParameterCollection == null) return null;
            var result = new Dictionary<string, object>();
            foreach (DbParameter item in dbParameterCollection)
            {
                result.Add(item.ParameterName, item.Value);
            }
            return result;
        }


        #region async

        #region main implementation
        private static IEnumerable<dynamic> ExecuteQueryMain(
        this DbCommand dbc,
        string query,
        IEnumerable<QueryParams> queryParamList = null,
        CancellationToken cToken = default,
        bool closeConnectionOnExit = false
        )
        {
            if (dbc == null) throw new ArgumentNullException(nameof(dbc));
            if (string.IsNullOrEmpty(query)) throw new ArgumentNullException(nameof(query));
            var conn = dbc.Connection ?? throw new Exception("DbCommand.Connection is null");
            bool cont = true;


            DbDataReader reader = null;
            DbCommand command = null;
            try
            {
                conn.EnsureOpenAsync(cToken).GetAwaiter().GetResult();
                cToken.ThrowIfCancellationRequested();
                command = conn.CreateCommand();
                command.CommandType = CommandType.Text;

                // Extract placeholder parameter names (without the markers) from the SQL query.
                // Each QueryParams object in queryParamList has it's own set of markers
                // and also has it's own set of parameters that are passed in the object's DataModel property.
                // Finally, each object in queryParamList also has it's own regular expression pattern
                // in its RegexPattern property telling this method how to extract the parameter names from the SQL query.

                if (queryParamList != null)
                {
                    int count = 0;
                    // note: reverse the order of the queryParamList
                    // so that the last object in the list has the highest priority (i.e., Last In First Out)
                    foreach (var queryParam in queryParamList.ReduceToUnique(true))
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
                            object matchingDataModelPropertyValue = null;
                            dataModelProperties?.TryGetValue(matchingQueryVar.Name, out matchingDataModelPropertyValue);


                            // if the query parameter name matches a data model field name
                            // then add the query parameter name to the list of placeholders
                            // and also add the value of the data model field to the list of placeholders
                            // so that the query parameter name is replaced with a prepared statement variable
                            // and the value of the data model field is passed as a parameter to the prepared statement variable

                            // Note: the prepared statement variable name is prefixed with @vxv_<count>_
                            // where <count> is the number of the QueryParams object in the queryParamList
                            // e.g. if the queryParamList has 3 objects, then the prepared statement variable name
                            // for the first object in the queryParamList will be @vxv_1_<query parameter name>
                            // and the prepared statement variable name for the second object in the queryParamList
                            // will be @vxv_2_<query parameter name>, and so on.
                            // This is done to ensure that the prepared statement variable names are unique
                            // across all the objects in the queryParamList.

                            // special characters in the query parameter name are replaced with underscores
                            // e.g. if the query parameter name is "my param", then the prepared statement variable name

                            // todo: replace the following with a regex
                            // regex version
                            var sqlParamName = $"@vxv_{count}_" +
                                _cleanVariableNamesRegexCompiled.Replace(matchingQueryVar.Name, "_");

                            query = query.Replace(
                                        matchingQueryVar.OpenMarker
                                        + matchingQueryVar.Name
                                        + matchingQueryVar.CloseMarker
                                        , sqlParamName
                                        // ,
                                        // StringComparison.OrdinalIgnoreCase
                                        );
                            var p = command.CreateParameter();
                            p.ParameterName = sqlParamName;
                            p.Value = matchingDataModelPropertyValue ?? DBNull.Value;
                            command.Parameters.Add(p);
                        }

                    }
                }

                command.CommandText = query;

                reader = command.ExecuteReaderAsync(cToken).GetAwaiter().GetResult();
                cToken.ThrowIfCancellationRequested();

            }
            catch (Exception ex)
            {
                if (reader != null)
                    reader.EnsureClosed();
                if (closeConnectionOnExit)
                    conn.EnsureClosed(cToken).GetAwaiter().GetResult();

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
                        cont = reader.ReadAsync(cToken).GetAwaiter().GetResult();
                        cToken.ThrowIfCancellationRequested();
                        if (!cont) break;
                    }
                    catch
                    {
                        reader.EnsureClosed();
                        if (closeConnectionOnExit) conn.EnsureClosed(cToken).GetAwaiter().GetResult();
                        throw;
                    }

                    ExpandoObject result = new ExpandoObject();

                    foreach (var item in Enumerable.Range(0, reader.FieldCount)
                            .Select(x => new { Name = reader.GetName(x), Value = reader.GetValue(x) }))
                    {
                        // ensure null of the correct type is returned
                        if (item.Value is DBNull)
                        {
                            if (!((IDictionary<string, object>) result)
                                .ContainsKey(item.Name))
                                ((IDictionary<string, object>)result).Add(item.Name, null);
                        }
                        else
                        {
                            if (!((IDictionary<string, object>)result)
                                .ContainsKey(item.Name))
                                ((IDictionary<string, object>)result).Add(item.Name, item.Value);

                        }
                    }

                    yield return result;
                }
            }
            cToken.ThrowIfCancellationRequested();
            if (reader != null)
                reader.EnsureClosed();
            if (closeConnectionOnExit) conn.EnsureClosed(cToken).GetAwaiter().GetResult();
            yield break;
        }

        #endregion

        #region DbCommand

        public static IEnumerable<dynamic> ExecuteQuery(
            this DbCommand dbc,
            string query,
            object queryParams = null,
            string queryParamsRegex = @"(?<open_marker>\{\{)(?<param>.*?)?(?<close_marker>\}\})",
            CancellationToken cToken = default,
            bool closeConnectionOnExit = false
            )
        {
            if (queryParams != null)
            {
                if ((queryParams as IEnumerable<QueryParams>)==null)
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

            foreach (var item in
                ExecuteQueryMain(dbc, query, (IEnumerable<QueryParams>)queryParams, cToken, closeConnectionOnExit))
                yield return item;
            yield break;
        }

        public static IEnumerable<T> ExecuteQuery<T>(
            this DbCommand dbc,
            string query,
            object queryParams = null,
            string queryParamsRegex = @"(?<open_marker>\{\{)(?<param>.*?)?(?<close_marker>\}\})",
            CancellationToken cToken = default,
            bool closeConnectionOnExit = false
            )
        {
            foreach (var item in ExecuteQuery(
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
        public static IEnumerable<dynamic> ExecuteQuery(
            this DbConnection con,
            string query,
            object queryParams = null,
            string queryParamsRegex = @"(?<open_marker>\{\{)(?<param>.*?)?(?<close_marker>\}\})",
            CancellationToken cToken = default,
            bool closeConnectionOnExit = false
            )
        {
            using (DbCommand dbc = con.CreateCommand())
            {
                foreach (var item in ExecuteQuery(
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



        public static IEnumerable<T> ExecuteQuery<T>(
            this DbConnection con,
            string query,
            object queryParams = null,
            string queryParamsRegex = @"(?<open_marker>\{\{)(?<param>.*?)?(?<close_marker>\}\})",
            CancellationToken cToken = default,
            bool closeConnectionOnExit = false
            )
        {
            foreach (var item in ExecuteQuery(
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


        //public static IEnumerable<dynamic> ExecuteQuery(
        //    this string connectionString,
        //    string query,
        //    object queryParams = null,
        //    string queryParamsRegex = @"(?<open_marker>\{\{)(?<param>.*?)?(?<close_marker>\}\})",
        //    CancellationToken cToken = default,
        //    bool closeConnectionOnExit = false
        //    )
        //{
        //    using (DbConnection con = CreateDbConnection(connectionString))
        //    {
        //        foreach (var item in ExecuteQuery(
        //            con,
        //            query,
        //            queryParams,
        //            queryParamsRegex,
        //            cToken,
        //            closeConnectionOnExit))
        //            yield return item;
        //    }
        //    yield break;
        //}


        //public static IEnumerable<T> ExecuteQuery<T>(
        //    this string connectionString,
        //    string query,
        //    object queryParams = null,
        //    string queryParamsRegex = @"(?<open_marker>\{\{)(?<param>.*?)?(?<close_marker>\}\})",
        //    CancellationToken cToken = default,
        //    bool closeConnectionOnExit = false
        //    )
        //{
        //    using (DbConnection con = CreateDbConnection(connectionString))
        //    {

        //        foreach (var item in ExecuteQuery(
        //        con,
        //        query,
        //        queryParams,
        //        queryParamsRegex,
        //        cToken,
        //        closeConnectionOnExit))
        //        {
        //            var converted = _mapper.Map<T>(item);
        //            if (converted is null) continue;
        //            yield return converted;
        //        }
        //    }
        //    yield break;
        //}


        #endregion

        #endregion


        #region sync

        //#region DbCommand
        //public static IEnumerable<dynamic> ExecuteQuery(
        //    this DbCommand dbc,
        //    string query,
        //    object queryParams = null,
        //    string queryParamsRegex = @"(?<open_marker>\{\{)(?<param>.*?)?(?<close_marker>\}\})",
        //    bool closeConnectionOnExit = false)
        //{
        //    return dbc.ExecuteQuery(
        //        query,
        //        queryParams,
        //        queryParamsRegex,
        //        CancellationToken.None,
        //        closeConnectionOnExit);
        //}

        //public static IEnumerable<T> ExecuteQuery<T>(
        //    this DbCommand dbc,
        //    string query,
        //    object queryParams = null,
        //    string queryParamsRegex = @"(?<open_marker>\{\{)(?<param>.*?)?(?<close_marker>\}\})",
        //    bool closeConnectionOnExit = false)
        //{
        //    return dbc.ExecuteQuery<T>(
        //        query,
        //        queryParams,
        //        queryParamsRegex,
        //        CancellationToken.None,
        //        closeConnectionOnExit);
        //}

        //#endregion

        //#region DbConnection
        //public static IEnumerable<dynamic> ExecuteQuery(
        //    this DbConnection con,
        //    string query,
        //    object queryParams = null,
        //    string queryParamsRegex = @"(?<open_marker>\{\{)(?<param>.*?)?(?<close_marker>\}\})",
        //    bool closeConnectionOnExit = false)
        //{
        //    return con.ExecuteQuery(
        //        query,
        //        queryParams,
        //        queryParamsRegex,
        //        CancellationToken.None,
        //        closeConnectionOnExit);
        //}

        //public static IEnumerable<T> ExecuteQuery<T>(
        //    this DbConnection con,
        //    string query,
        //    object queryParams = null,
        //    string queryParamsRegex = @"(?<open_marker>\{\{)(?<param>.*?)?(?<close_marker>\}\})",
        //    bool closeConnectionOnExit = false)
        //{
        //    return con.ExecuteQuery<T>(
        //        query,
        //        queryParams,
        //        queryParamsRegex,
        //        CancellationToken.None,
        //        closeConnectionOnExit);
        //}
        //#endregion

        //#region connection string
        //public static IEnumerable<dynamic> ExecuteQuery(
        //    this string connectionString,
        //    string query,
        //    object queryParams = null,
        //    string queryParamsRegex = @"(?<open_marker>\{\{)(?<param>.*?)?(?<close_marker>\}\})",
        //    bool closeConnectionOnExit = false)
        //{
        //    return connectionString.ExecuteQuery(
        //        query,
        //        queryParams,
        //        queryParamsRegex,
        //        CancellationToken.None,
        //        closeConnectionOnExit);
        //}

        //public static IEnumerable<T> ExecuteQuery<T>(
        //    this string connectionString,
        //    string query,
        //    object queryParams = null,
        //    string queryParamsRegex = @"(?<open_marker>\{\{)(?<param>.*?)?(?<close_marker>\}\})",
        //    bool closeConnectionOnExit = false)
        //{
        //    return connectionString.ExecuteQuery<T>(
        //        query,
        //        queryParams,
        //        queryParamsRegex,
        //        CancellationToken.None,
        //        closeConnectionOnExit);
        //}

        //#endregion

        #endregion

        #region embedded extensions imported from Com.H.x packages



        #region exception messaging

        private static string GenerateError(
            this Exception ex,
            DbCommand command,
            string query,
            IDictionary<string, object> queryParams
            )
        {
            string errMsg = "Error executing query:"
                + Environment.NewLine
                + $"Error msg:{Environment.NewLine}"
                + Environment.NewLine + ex.Message
                + $"{Environment.NewLine}-----------{Environment.NewLine}"
                ;

            if (command != null)
                errMsg +=
                $"{Environment.NewLine}-----------{Environment.NewLine}"
                + $"Parameters:{Environment.NewLine}"
                + string.Join(Environment.NewLine,
                command.Parameters.Cast<DbParameter>().Select(x => $"{x.ParameterName} = {x.Value}"));
            else if (queryParams != null)
                errMsg +=
                $"{Environment.NewLine}-----------{Environment.NewLine}"
                + $"Parameters:{Environment.NewLine}"
                + string.Join(Environment.NewLine,
                queryParams.Select(x => $"{x.Key} = {x.Value}"));

            if (command != null)
                errMsg += $"{Environment.NewLine}-----{Environment.NewLine}Query{Environment.NewLine}"
                + command.CommandText + $"{Environment.NewLine}-------{Environment.NewLine}";
            else if (query != null)
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