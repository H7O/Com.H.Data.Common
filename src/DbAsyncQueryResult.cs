using System.Data.Common;
namespace Com.H.Data.Common;

/// <summary>
/// Disposable wrapper for asynchronous query results that implements IAsyncEnumerable&lt;T&gt; and ensures proper cleanup of database resources.
/// Provides automatic management of DbDataReader and optionally DbConnection lifecycle.
/// Can be used directly where IAsyncEnumerable&lt;T&gt; is expected.
/// Must be disposed using 'await using' statement or explicit DisposeAsync() call to release database resources.
/// </summary>
/// <typeparam name="T">The type of objects in the query result. Use 'dynamic' for flexible schema or a specific type for strongly-typed results.</typeparam>
/// <remarks>
/// This class wraps an IAsyncEnumerable and manages the lifecycle of the underlying DbDataReader and optionally the DbConnection.
/// The reader closes by itself once its rows are read to the end, when an enumeration stops early
/// (FirstAsync, FirstOrDefaultAsync, Take, an await foreach left with break) and when reading throws, so another
/// query can then run on the same connection. It stays open while you hold an enumerator part-way through the rows,
/// and for a result you haven't enumerated: CloseReaderAsync closes it then.
/// Disposing the result disposes the reader and the command the library created for the query, and disposes the
/// connection if closeConnectionOnExit was true. SqlClient, Npgsql and MySqlConnector can't open a disposed
/// connection again.
/// </remarks>
/// <example>
/// <code>
/// await using var result = await connection.ExecuteQueryAsync("SELECT * FROM Users");
/// await foreach (var row in result)
/// {
///     Console.WriteLine(row.Name);
/// }
/// // The reader closed when the await foreach finished; the result is disposed here
/// </code>
/// </example>
public class DbAsyncQueryResult<T> : IAsyncEnumerable<T>, IAsyncDisposable, IDisposable
{
    private readonly IAsyncEnumerable<T> _asyncEnumerable;
    private readonly DbDataReader? _reader;
    private readonly DbCommand? _command;
    private readonly DbConnection? _connection;
    private readonly bool _closeConnectionOnDispose;
    private bool _disposed = false;

    internal DbAsyncQueryResult(IAsyncEnumerable<T> asyncEnumerable, DbDataReader? reader, DbConnection? connection, bool closeConnectionOnDispose)
        : this(asyncEnumerable, reader, null, connection, closeConnectionOnDispose)
    {
    }

    internal DbAsyncQueryResult(IAsyncEnumerable<T> asyncEnumerable, DbDataReader? reader, DbCommand? command, DbConnection? connection, bool closeConnectionOnDispose)
    {
        _asyncEnumerable = asyncEnumerable;
        _reader = reader;
        _command = command;
        _connection = connection;
        _closeConnectionOnDispose = closeConnectionOnDispose;
    }

    // Internal properties for accessing the reader, command and connection
    internal DbDataReader? Reader => _reader;
    internal DbCommand? Command => _command;
    internal DbConnection? Connection => _connection;

    /// <summary>
    /// Returns the underlying async enumerable for iteration.
    /// The reader closes once its rows are read to the end or an enumeration stops early (see the class remarks).
    /// Use this when you need to pass the enumerable to methods that expect IAsyncEnumerable&lt;T&gt;.
    /// </summary>
    /// <returns>The underlying IAsyncEnumerable&lt;T&gt; that yields query results</returns>
    /// <example>
    /// <code>
    /// await using var result = await connection.ExecuteQueryAsync("SELECT * FROM Users");
    /// var enumerable = result.AsAsyncEnumerable();
    /// var filteredUsers = enumerable.Where(u => u.Age &gt; 18);
    /// </code>
    /// </example>
    public IAsyncEnumerable<T> AsAsyncEnumerable() => _asyncEnumerable;

    /// <summary>
    /// Returns a blocking (synchronous) enumerable from the async enumerable.
    /// The reader closes once its rows are read to the end or an enumeration stops early (see the class remarks).
    /// Use this when you need to use synchronous iteration (foreach) instead of async iteration (await foreach).
    /// </summary>
    /// <returns>A blocking IEnumerable&lt;T&gt; that yields query results synchronously</returns>
    /// <example>
    /// <code>
    /// using var result = await connection.ExecuteQueryAsync("SELECT * FROM Users");
    /// foreach (var user in result.AsEnumerable())
    /// {
    ///     Console.WriteLine(user.Name);
    /// }
    /// </code>
    /// </example>
    public IEnumerable<T> AsEnumerable() => _asyncEnumerable.ToBlockingEnumerable();

    /// <summary>
    /// Gets the async enumerator for iterating over query results.
    /// This method is called implicitly when using 'await foreach' syntax.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for the async iteration</param>
    /// <returns>An async enumerator for the query results</returns>
    public IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default)
        => _asyncEnumerable.GetAsyncEnumerator(cancellationToken);

    /// <summary>
    /// Closes the underlying DbDataReader asynchronously without disposing the entire result,
    /// so another query can run on the same connection.
    /// The reader already closes by itself once its rows are read to the end or an enumeration stops early
    /// (FirstAsync, FirstOrDefaultAsync, Take, an await foreach left with break), so this is needed only for a
    /// result you haven't enumerated, or while you hold an enumerator part-way through the rows.
    /// It does nothing when the reader is already closed.
    /// </summary>
    /// <returns>A task representing the asynchronous close operation</returns>
    /// <example>
    /// <code>
    /// await using var result = await connection.ExecuteQueryAsync("SELECT * FROM Users");
    /// await using var users = result.GetAsyncEnumerator();
    /// await users.MoveNextAsync(); // the reader stays open while this enumerator is held
    /// await result.CloseReaderAsync(); // another query can now run on this connection
    /// </code>
    /// </example>
    public async ValueTask CloseReaderAsync()
    {
        if (_reader != null && !_reader.IsClosed)
        {
            await _reader.CloseAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Closes the underlying DbDataReader synchronously without disposing the entire result,
    /// so another query can run on the same connection.
    /// The reader already closes by itself once its rows are read to the end or an enumeration stops early
    /// (FirstAsync, FirstOrDefaultAsync, Take, an await foreach left with break), so this is needed only for a
    /// result you haven't enumerated, or while you hold an enumerator part-way through the rows.
    /// It does nothing when the reader is already closed.
    /// </summary>
    /// <example>
    /// <code>
    /// await using var result = await connection.ExecuteQueryAsync("SELECT * FROM Users");
    /// await using var users = result.GetAsyncEnumerator();
    /// await users.MoveNextAsync(); // the reader stays open while this enumerator is held
    /// result.CloseReader(); // another query can now run on this connection
    /// </code>
    /// </example>
    public void CloseReader()
    {
        if (_reader != null && !_reader.IsClosed)
        {
            _reader.Close();
        }
    }

    /// <summary>
    /// Asynchronously disposes the DbAsyncQueryResult: disposes the reader and the command the library created
    /// for the query. If closeConnectionOnExit was true, also disposes the DbConnection, which SqlClient, Npgsql
    /// and MySqlConnector can't open again.
    /// This method is called automatically when using 'await using' statement.
    /// </summary>
    /// <returns>A task representing the asynchronous dispose operation</returns>
    /// <example>
    /// <code>
    /// await using var result = await connection.ExecuteQueryAsync("SELECT * FROM Users");
    /// // DisposeAsync is called automatically at the end of the using block
    /// </code>
    /// </example>
    public async ValueTask DisposeAsync()
    {
        if (!_disposed)
        {
            // Dispose reader (which also closes it and releases all resources)
            if (_reader != null)
            {
                await _reader.DisposeAsync().ConfigureAwait(false);
            }

            // Dispose command if we own it (must be disposed after reader)
            if (_command != null)
            {
                await _command.DisposeAsync().ConfigureAwait(false);
            }

            // Dispose connection if we own it
            if (_closeConnectionOnDispose && _connection != null)
            {
                await _connection.DisposeAsync().ConfigureAwait(false);
            }

            _disposed = true;
        }
    }

    /// <summary>
    /// Synchronously disposes the DbAsyncQueryResult: disposes the reader and the command the library created
    /// for the query. If closeConnectionOnExit was true, also disposes the DbConnection, which SqlClient, Npgsql
    /// and MySqlConnector can't open again.
    /// This method is called automatically when using 'using' statement.
    /// Prefer using 'await using' and DisposeAsync() for async disposal to avoid potential blocking.
    /// </summary>
    /// <example>
    /// <code>
    /// using var result = await connection.ExecuteQueryAsync("SELECT * FROM Users");
    /// // Dispose is called automatically at the end of the using block
    /// </code>
    /// </example>
    public void Dispose()
    {
        if (!_disposed)
        {
            // Use synchronous disposal to avoid GetAwaiter().GetResult() deadlock issues
            _reader?.Dispose();

            // Dispose command if we own it (must be disposed after reader)
            _command?.Dispose();

            if (_closeConnectionOnDispose && _connection != null)
            {
                _connection.Dispose();
            }

            _disposed = true;
        }
    }
}