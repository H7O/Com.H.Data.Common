namespace Com.H.Data.Common
{
    /// <summary>
    /// Provides extension methods for various join operations on collections.
    /// </summary>
    internal static class JoinExtensions
    {
        /// <summary>
        /// Performs a left join between two IEnumerable
        /// </summary>
        /// <typeparam name="TOuter"></typeparam>
        /// <typeparam name="TInner"></typeparam>
        /// <typeparam name="TKey"></typeparam>
        /// <typeparam name="TResult"></typeparam>
        /// <param name="outer"></param>
        /// <param name="inner"></param>
        /// <param name="outerKeySelector"></param>
        /// <param name="innerKeySelector"></param>
        /// <param name="resultSelector"></param>
        /// <returns></returns>
        public static IEnumerable<TResult> LeftJoin<TOuter, TInner, TKey, TResult>(
            this IEnumerable<TOuter> outer,
            IEnumerable<TInner> inner,
            Func<TOuter, TKey> outerKeySelector,
            Func<TInner, TKey> innerKeySelector,
            Func<TOuter, TInner?, TResult> resultSelector)
            => outer.GroupJoin(
                    inner,
                    outerKeySelector,
                    innerKeySelector,
                    (o, i) => new { o, i = i.DefaultIfEmpty() }
                    )
                    .SelectMany(m => m.i.Select(inn => resultSelector(m.o, inn)));




        /// <summary>
        /// Performs a right join between two IEnumerable objects
        /// </summary>
        /// <typeparam name="TOuter"></typeparam>
        /// <typeparam name="TInner"></typeparam>
        /// <typeparam name="TKey"></typeparam>
        /// <typeparam name="TResult"></typeparam>
        /// <param name="outer"></param>
        /// <param name="inner"></param>
        /// <param name="outerKeySelector"></param>
        /// <param name="innerKeySelector"></param>
        /// <param name="resultSelector"></param>
        /// <returns></returns>
        public static IEnumerable<TResult> RightJoin<TOuter, TInner, TKey, TResult>(
            this IEnumerable<TOuter> outer,
            IEnumerable<TInner> inner,
            Func<TOuter, TKey> outerKeySelector,
            Func<TInner, TKey> innerKeySelector,
            Func<TOuter?, TInner, TResult> resultSelector)
            => inner.LeftJoin(outer, innerKeySelector, outerKeySelector, (i, o) => resultSelector(o, i));



        /// <summary>
        /// Performs a full outer join between two IEnumerable objects
        /// </summary>
        /// <typeparam name="TOuter">Type of elements in the outer collection</typeparam>
        /// <typeparam name="TInner">Type of elements in the inner collection</typeparam>
        /// <typeparam name="TKey">Type of the join key</typeparam>
        /// <typeparam name="TResult">Type of the result elements</typeparam>
        /// <param name="outer">The outer collection</param>
        /// <param name="inner">The inner collection</param>
        /// <param name="outerKeySelector">Function to extract the join key from outer elements</param>
        /// <param name="innerKeySelector">Function to extract the join key from inner elements</param>
        /// <param name="resultSelector">Function to create result elements from matched outer and inner elements</param>
        /// <returns>Collection of joined elements</returns>
        public static IEnumerable<TResult> FullOuterJoin<TOuter, TInner, TKey, TResult>(
            this IEnumerable<TOuter> outer,
            IEnumerable<TInner> inner,
            Func<TOuter, TKey> outerKeySelector,
            Func<TInner, TKey> innerKeySelector,
            Func<TOuter?, TInner?, TResult> resultSelector)
            => outer.LeftJoin(inner, outerKeySelector, innerKeySelector, resultSelector)
                .Union(
                    outer.RightJoin(inner, outerKeySelector, innerKeySelector, resultSelector)
                    );




        /// <summary>
        /// Merges two dictionaries into a new dictionary. Values from the second dictionary will overwrite values from the first if keys conflict.
        /// </summary>
        /// <typeparam name="TKey">The type of the dictionary keys</typeparam>
        /// <typeparam name="TVal">The type of the dictionary values</typeparam>
        /// <param name="first">The first dictionary</param>
        /// <param name="second">The second dictionary</param>
        /// <returns>A new merged dictionary</returns>
        public static Dictionary<TKey, TVal> Merge<TKey, TVal>(
            this Dictionary<TKey, TVal> first, Dictionary<TKey, TVal> second) where TKey : notnull
        {
            return first.Union(second).ToDictionary(k => k.Key, v => v.Value);
        }

    }
}
