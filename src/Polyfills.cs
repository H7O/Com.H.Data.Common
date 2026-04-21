#if NETSTANDARD2_0
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Com.H.Data.Common
{
    /// <summary>
    /// Polyfills for APIs missing on .NET Standard 2.0 that the rest of the codebase
    /// relies on (and which are present natively on .NET 8+/.NET 9+/.NET 10+).
    /// Compiled only when targeting netstandard2.0 so modern TFMs use the BCL versions.
    /// </summary>
    internal static class StringPolyfills
    {
        public static bool Contains(this string source, string value, StringComparison comparison)
            => source.IndexOf(value, comparison) >= 0;

        public static string Replace(this string source, string oldValue, string? newValue, StringComparison comparison)
        {
            if (string.IsNullOrEmpty(oldValue)) return source;
            newValue ??= string.Empty;
            var sb = new StringBuilder();
            int prev = 0;
            int idx;
            while ((idx = source.IndexOf(oldValue, prev, comparison)) >= 0)
            {
                sb.Append(source, prev, idx - prev);
                sb.Append(newValue);
                prev = idx + oldValue.Length;
            }
            sb.Append(source, prev, source.Length - prev);
            return sb.ToString();
        }
    }

    internal static class DictionaryPolyfills
    {
        /// <summary>
        /// Emulates CollectionExtensions.TryAdd (available on IDictionary&lt;K,V&gt; in .NET 5+)
        /// and Dictionary&lt;K,V&gt;.TryAdd (available in .NET Core 2.0+/netstandard 2.1+).
        /// </summary>
        public static bool TryAdd<TKey, TValue>(this IDictionary<TKey, TValue> dict, TKey key, TValue value)
        {
            if (dict.ContainsKey(key)) return false;
            dict.Add(key, value);
            return true;
        }
    }

    internal static class EnumerablePolyfills
    {
        public static IEnumerable<TSource> DistinctBy<TSource, TKey>(
            this IEnumerable<TSource> source, Func<TSource, TKey> keySelector)
        {
            var seen = new HashSet<TKey>();
            foreach (var item in source)
                if (seen.Add(keySelector(item))) yield return item;
        }
    }

    internal static class AsyncEnumerablePolyfills
    {
        /// <summary>
        /// Approximates <c>IAsyncEnumerable&lt;T&gt;.ToBlockingEnumerable()</c> (available in .NET 7+).
        /// Blocks the calling thread on each MoveNextAsync — sufficient for the synchronous
        /// IEnumerable bridges exposed by DbQueryResult / DbAsyncQueryResult.
        /// </summary>
        public static IEnumerable<T> ToBlockingEnumerable<T>(
            this IAsyncEnumerable<T> source, CancellationToken cancellationToken = default)
        {
            var enumerator = source.GetAsyncEnumerator(cancellationToken);
            try
            {
                while (true)
                {
                    var moveNextTask = enumerator.MoveNextAsync();
                    if (!moveNextTask.AsTask().GetAwaiter().GetResult()) break;
                    yield return enumerator.Current;
                }
            }
            finally
            {
                enumerator.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
        }
    }

    /// <summary>
    /// Async extension shims for ADO.NET types that only expose synchronous Close/Dispose
    /// on .NET Standard 2.0. On newer TFMs these methods exist natively, so the polyfills
    /// are compiled out and the BCL implementations are used.
    /// </summary>
    internal static class DbAsyncPolyfills
    {
        public static Task CloseAsync(this DbConnection connection)
        {
            connection.Close();
            return Task.CompletedTask;
        }

        public static Task CloseAsync(this DbDataReader reader)
        {
            reader.Close();
            return Task.CompletedTask;
        }

        public static ValueTask DisposeAsync(this DbDataReader reader)
        {
            reader.Dispose();
            return default;
        }

        public static ValueTask DisposeAsync(this DbCommand command)
        {
            command.Dispose();
            return default;
        }

        public static ValueTask DisposeAsync(this DbConnection connection)
        {
            connection.Dispose();
            return default;
        }
    }
}
#endif
