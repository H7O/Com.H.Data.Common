using System.Data.Common;
using System.Runtime.CompilerServices;

namespace Com.H.Data.Common;

/// <summary>
/// Disposable wrapper for synchronous query results that ensures proper cleanup of database resources.
/// User maintains full control over when to dispose resources.
/// Can be used directly where IEnumerable&lt;T&gt; is expected.
/// </summary>
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
	internal DbDataReader? Reader => _reader;
	internal DbConnection? Connection => _connection;

	/// <summary>
	/// Returns the async enumerable. Resources remain open until this DbQueryResult is disposed.
	/// </summary>
	public IAsyncEnumerable<T> AsAsyncEnumerable() => _asyncEnumerable;

	/// <summary>
	/// Returns a blocking enumerable. Resources remain open until this DbQueryResult is disposed.
	/// </summary>
	public IEnumerable<T> AsEnumerable() => _asyncEnumerable.ToBlockingEnumerable();

	// IEnumerable<T> implementation - allows direct use where IEnumerable<T> is expected
	public IEnumerator<T> GetEnumerator() => _asyncEnumerable.ToBlockingEnumerable().GetEnumerator();
	System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();

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

	public void Dispose()
	{
		DisposeAsync().AsTask().GetAwaiter().GetResult();
	}
}


