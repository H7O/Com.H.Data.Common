using System.Data.Common;
using System.Runtime.CompilerServices;

namespace Com.H.Data.Common;

/// <summary>
/// Disposable wrapper for synchronous query results that implements IEnumerable&lt;T&gt; and ensures proper cleanup of database resources.
/// Provides automatic management of DbDataReader and optionally DbConnection lifecycle.
/// Can be used directly where IEnumerable&lt;T&gt; is expected.
/// Must be disposed using 'using' statement or explicit Dispose() call to release database resources.
/// </summary>
/// <typeparam name="T">The type of objects in the query result. Use 'dynamic' for flexible schema or a specific type for strongly-typed results.</typeparam>
/// <remarks>
/// This class wraps an IAsyncEnumerable and provides synchronous enumeration via IEnumerable&lt;T&gt;.
/// It manages the lifecycle of the underlying DbDataReader and optionally the DbConnection.
/// When disposed, it automatically closes and disposes the reader and connection (if closeConnectionOnDispose was true).
/// User maintains full control over when to dispose resources via the using statement or explicit disposal.
/// </remarks>
/// <example>
/// <code>
/// using var result = connection.ExecuteQuery("SELECT * FROM Users");
/// foreach (var row in result)
/// {
///     Console.WriteLine(row.Name);
/// }
/// // Reader and connection (if specified) are automatically disposed here
/// </code>
/// </example>
public class DbQueryResult<T> : IEnumerable<T>, IAsyncDisposable, IDisposable
{
	private readonly IAsyncEnumerable<T> _asyncEnumerable;
	private readonly DbDataReader? _reader;
	private readonly DbConnection? _connection;
	private readonly bool _closeConnectionOnDispose;
	private bool _disposed = false;

	internal DbQueryResult(IAsyncEnumerable<T> asyncEnumerable, DbDataReader? reader, DbConnection? connection, bool closeConnectionOnDispose)
	{
		_asyncEnumerable = asyncEnumerable;
		_reader = reader;
		_connection = connection;
		_closeConnectionOnDispose = closeConnectionOnDispose;
	}

	// Internal properties for accessing the reader and connection
	public DbDataReader? Reader => _reader;
	internal DbConnection? Connection => _connection;

	/// <summary>
	/// Returns the underlying async enumerable for asynchronous iteration.
	/// Resources (reader and connection) remain open until this DbQueryResult is disposed.
	/// Use this when you need to pass the enumerable to methods that expect IAsyncEnumerable&lt;T&gt;.
	/// </summary>
	/// <returns>The underlying IAsyncEnumerable&lt;T&gt; that yields query results</returns>
	/// <example>
	/// <code>
	/// using var result = connection.ExecuteQuery("SELECT * FROM Users");
	/// var asyncEnumerable = result.AsAsyncEnumerable();
	/// await foreach (var user in asyncEnumerable)
	/// {
	///     Console.WriteLine(user.Name);
	/// }
	/// </code>
	/// </example>
	public IAsyncEnumerable<T> AsAsyncEnumerable() => _asyncEnumerable;

	/// <summary>
	/// Returns a blocking (synchronous) enumerable from the async enumerable.
	/// Resources (reader and connection) remain open until this DbQueryResult is disposed.
	/// This is the default enumerable used when iterating with foreach.
	/// </summary>
	/// <returns>A blocking IEnumerable&lt;T&gt; that yields query results synchronously</returns>
	/// <example>
	/// <code>
	/// using var result = connection.ExecuteQuery("SELECT * FROM Users");
	/// foreach (var user in result.AsEnumerable())
	/// {
	///     Console.WriteLine(user.Name);
	/// }
	/// </code>
	/// </example>
	public IEnumerable<T> AsEnumerable() => _asyncEnumerable.ToBlockingEnumerable();

	/// <summary>
	/// Gets the enumerator for iterating over query results synchronously.
	/// This method is called implicitly when using 'foreach' syntax.
	/// </summary>
	/// <returns>An enumerator for the query results</returns>
	public IEnumerator<T> GetEnumerator() => _asyncEnumerable.ToBlockingEnumerable().GetEnumerator();
	
	/// <summary>
	/// Gets the non-generic enumerator for iterating over query results.
	/// This is the explicit implementation of IEnumerable.GetEnumerator().
	/// </summary>
	/// <returns>A non-generic enumerator for the query results</returns>
	System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();

	/// <summary>
	/// Closes the underlying DbDataReader asynchronously without disposing the entire result.
	/// Use this when you want to close the reader early (e.g., after reading first few results)
	/// to allow executing another query on the same connection.
	/// </summary>
	/// <returns>A task representing the asynchronous close operation</returns>
	/// <example>
	/// <code>
	/// using var result = connection.ExecuteQuery("SELECT * FROM Users");
	/// var firstUser = result.FirstOrDefault();
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
		CloseReaderAsync().AsTask().GetAwaiter().GetResult();
	}

	/// <summary>
	/// Asynchronously disposes the DbQueryResult, closing and disposing the underlying DbDataReader.
	/// If closeConnectionOnDispose was set to true during creation, also ensures the DbConnection is closed.
	/// This method is called automatically when using 'await using' statement.
	/// </summary>
	/// <returns>A task representing the asynchronous dispose operation</returns>
	/// <example>
	/// <code>
	/// await using var result = connection.ExecuteQuery("SELECT * FROM Users");
	/// // DisposeAsync is called automatically at the end of the await using block
	/// </code>
	/// </example>
	public async ValueTask DisposeAsync()
	{
		if (!_disposed)
		{
			if (_reader != null && !_reader.IsClosed)
			{
				await _reader.CloseAsync();
			}

			if (_closeConnectionOnDispose && _connection != null)
			{
				await _connection.EnsureClosedAsync();
			}

			_disposed = true;
		}
	}

	/// <summary>
	/// Synchronously disposes the DbQueryResult, closing and disposing the underlying DbDataReader.
	/// If closeConnectionOnDispose was set to true during creation, also ensures the DbConnection is closed.
	/// This method is called automatically when using 'using' statement.
	/// Internally calls DisposeAsync() synchronously.
	/// </summary>
	/// <example>
	/// <code>
	/// using var result = connection.ExecuteQuery("SELECT * FROM Users");
	/// // Dispose is called automatically at the end of the using block
	/// </code>
	/// </example>
	public void Dispose()
	{
		DisposeAsync().AsTask().GetAwaiter().GetResult();
	}
}


