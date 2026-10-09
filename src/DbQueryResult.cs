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
/// The reader closes by itself once its rows are read to the end, when an enumeration stops early
/// (First, FirstOrDefault, Take, a foreach left with break) and when reading throws, so another query can then
/// run on the same connection. It stays open while you hold an enumerator part-way through the rows, and for a
/// result you haven't enumerated: CloseReader closes it then.
/// Disposing the result closes the reader if it is still open, disposes the command the library created for the
/// query, and closes the connection if closeConnectionOnExit was true.
/// </remarks>
/// <example>
/// <code>
/// using var result = connection.ExecuteQuery("SELECT * FROM Users");
/// foreach (var row in result)
/// {
///     Console.WriteLine(row.Name);
/// }
/// // The reader closed when the foreach finished; the result is disposed here
/// </code>
/// </example>
public class DbQueryResult<T> : IEnumerable<T>, IAsyncDisposable, IDisposable
{
	private readonly IAsyncEnumerable<T> _asyncEnumerable;
	private readonly DbDataReader? _reader;
	private readonly DbCommand? _command;
	private readonly DbConnection? _connection;
	private readonly bool _closeConnectionOnDispose;
	private bool _disposed = false;

	internal DbQueryResult(IAsyncEnumerable<T> asyncEnumerable, DbDataReader? reader, DbConnection? connection, bool closeConnectionOnDispose)
		: this(asyncEnumerable, reader, null, connection, closeConnectionOnDispose)
	{
	}

	internal DbQueryResult(IAsyncEnumerable<T> asyncEnumerable, DbDataReader? reader, DbCommand? command, DbConnection? connection, bool closeConnectionOnDispose)
	{
		_asyncEnumerable = asyncEnumerable;
		_reader = reader;
		_command = command;
		_connection = connection;
		_closeConnectionOnDispose = closeConnectionOnDispose;
	}

	// Internal properties for accessing the reader, command and connection
	/// <summary>
	/// The DbDataReader the rows are read from. It closes by itself once its rows are read to the end or an
	/// enumeration stops early (see the class remarks), so check IsClosed before using it.
	/// </summary>
	public DbDataReader? Reader => _reader;
	internal DbCommand? Command => _command;
	internal DbConnection? Connection => _connection;

	/// <summary>
	/// Returns the underlying async enumerable for asynchronous iteration.
	/// The reader closes once its rows are read to the end or an enumeration stops early (see the class remarks).
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
	/// The reader closes once its rows are read to the end or an enumeration stops early (see the class remarks).
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
	/// Closes the underlying DbDataReader asynchronously without disposing the entire result,
	/// so another query can run on the same connection.
	/// The reader already closes by itself once its rows are read to the end or an enumeration stops early
	/// (First, FirstOrDefault, Take, a foreach left with break), so this is needed only for a result you
	/// haven't enumerated, or while you hold an enumerator part-way through the rows.
	/// It does nothing when the reader is already closed.
	/// </summary>
	/// <returns>A task representing the asynchronous close operation</returns>
	/// <example>
	/// <code>
	/// using var result = connection.ExecuteQuery("SELECT * FROM Users");
	/// using var users = result.GetEnumerator();
	/// users.MoveNext(); // the reader stays open while this enumerator is held
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
	/// (First, FirstOrDefault, Take, a foreach left with break), so this is needed only for a result you
	/// haven't enumerated, or while you hold an enumerator part-way through the rows.
	/// It does nothing when the reader is already closed.
	/// </summary>
	/// <example>
	/// <code>
	/// using var result = connection.ExecuteQuery("SELECT * FROM Users");
	/// using var users = result.GetEnumerator();
	/// users.MoveNext(); // the reader stays open while this enumerator is held
	/// result.CloseReader(); // another query can now run on this connection
	/// </code>
	/// </example>
	public void CloseReader()
	{
		CloseReaderAsync().AsTask().GetAwaiter().GetResult();
	}

	/// <summary>
	/// Asynchronously disposes the DbQueryResult: closes the reader if it is still open and disposes the command
	/// the library created for the query. If closeConnectionOnExit was true, also closes the DbConnection.
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
				await _reader.CloseAsync().ConfigureAwait(false);
			}

			// Dispose command if we own it (must be disposed after reader)
			if (_command != null)
			{
				await _command.DisposeAsync().ConfigureAwait(false);
			}

			if (_closeConnectionOnDispose && _connection != null)
			{
				await _connection.EnsureClosedAsync().ConfigureAwait(false);
			}

			_disposed = true;
		}
	}

	/// <summary>
	/// Synchronously disposes the DbQueryResult: closes the reader if it is still open and disposes the command
	/// the library created for the query. If closeConnectionOnExit was true, also closes the DbConnection.
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


