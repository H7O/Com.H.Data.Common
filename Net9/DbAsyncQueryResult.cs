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
/// When disposed, it automatically closes and disposes the reader and connection (if closeConnectionOnDispose was true).
/// User maintains full control over when to dispose resources via the using statement or explicit disposal.
/// </remarks>
/// <example>
/// <code>
/// await using var result = await connection.ExecuteQueryAsync("SELECT * FROM Users");
/// await foreach (var row in result)
/// {
///     Console.WriteLine(row.Name);
/// }
/// // Reader and connection (if specified) are automatically disposed here
/// </code>
/// </example>
public class DbAsyncQueryResult<T> : IAsyncEnumerable<T>, IAsyncDisposable, IDisposable
{
    private readonly IAsyncEnumerable<T> _asyncEnumerable;
    private readonly DbDataReader? _reader;
    private readonly DbConnection? _connection;
    private readonly bool _closeConnectionOnDispose;
    private bool _disposed = false;

    internal DbAsyncQueryResult(IAsyncEnumerable<T> asyncEnumerable, DbDataReader? reader, DbConnection? connection, bool closeConnectionOnDispose)
    {
        _asyncEnumerable = asyncEnumerable;
        _reader = reader;
        _connection = connection;
        _closeConnectionOnDispose = closeConnectionOnDispose;
    }

    // Internal properties for accessing the reader and connection
    internal DbDataReader? Reader => _reader;
    internal DbConnection? Connection => _connection;

    /// <summary>
    /// Returns the underlying async enumerable for iteration.
    /// Resources (reader and connection) remain open until this DbAsyncQueryResult is disposed.
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
    /// Resources (reader and connection) remain open until this DbAsyncQueryResult is disposed.
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
    /// Closes the underlying DbDataReader asynchronously without disposing the entire result.
    /// Use this when you want to close the reader early (e.g., after reading first few results)
    /// to allow executing another query on the same connection.
    /// </summary>
    /// <returns>A task representing the asynchronous close operation</returns>
    /// <example>
    /// <code>
    /// await using var result = await connection.ExecuteQueryAsync("SELECT * FROM Users");
    /// var firstUser = await result.FirstOrDefaultAsync();
    /// await result.CloseReaderAsync(); // Close reader to execute another query on same connection
    /// </code>
    /// </example>
    public async ValueTask CloseReaderAsync()
    {
        if (_reader != null && !_reader.IsClosed)
        {
            await _reader.CloseAsync();
        }
    }

    /// <summary>
    /// Closes the underlying DbDataReader synchronously without disposing the entire result.
    /// Use this when you want to close the reader early (e.g., after reading first few results)
    /// to allow executing another query on the same connection.
    /// </summary>
    /// <example>
    /// <code>
    /// using var result = connection.ExecuteQuery("SELECT * FROM Users");
    /// var firstUser = result.FirstOrDefault();
    /// result.CloseReader(); // Close reader to execute another query on same connection
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
    /// Asynchronously disposes the DbAsyncQueryResult, closing and disposing the underlying DbDataReader.
    /// If closeConnectionOnDispose was set to true during creation, also disposes the DbConnection.
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
                await _reader.DisposeAsync();
            }

            // Dispose connection if we own it
            if (_closeConnectionOnDispose && _connection != null)
            {
                await _connection.DisposeAsync();
            }

            _disposed = true;
        }
    }

    /// <summary>
    /// Synchronously disposes the DbAsyncQueryResult, closing and disposing the underlying DbDataReader.
    /// If closeConnectionOnDispose was set to true during creation, also disposes the DbConnection.
    /// This method is called automatically when using 'using' statement.
    /// Prefer using 'await using' and DisposeAsync() for async disposal to avoid potential blocking.
    /// </summary>
    /// <example>
    /// <code>
    /// using var result = connection.ExecuteQuery("SELECT * FROM Users");
    /// // Dispose is called automatically at the end of the using block
    /// </code>
    /// </example>
    public void Dispose()
    {
        if (!_disposed)
        {
            // Use synchronous disposal to avoid GetAwaiter().GetResult() deadlock issues
            _reader?.Dispose();

            if (_closeConnectionOnDispose && _connection != null)
            {
                _connection.Dispose();
            }

            _disposed = true;
        }
    }
}