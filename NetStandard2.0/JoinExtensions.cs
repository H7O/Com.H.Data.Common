using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Com.H.Data.Common
{
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
            Func<TOuter, TInner, TResult> resultSelector)
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
            Func<TOuter, TInner, TResult> resultSelector)
            => inner.LeftJoin(outer, innerKeySelector, outerKeySelector, (i, o) => resultSelector(o, i));



        /// <summary>
        /// Performs a full outter join between two IEnumerable objects
        /// </summary>
        /// <typeparam name="TLeft"></typeparam>
        /// <typeparam name="TRight"></typeparam>
        /// <typeparam name="TKey"></typeparam>
        /// <typeparam name="TResult"></typeparam>
        /// <param name="left"></param>
        /// <param name="right"></param>
        /// <param name="leftKeySelector"></param>
        /// <param name="rightKeySelector"></param>
        /// <param name="resultSelector"></param>
        /// <returns></returns>
        public static IEnumerable<TResult> FullOuterJoin<TOuter, TInner, TKey, TResult>(
            this IEnumerable<TOuter> outer,
            IEnumerable<TInner> inner,
            Func<TOuter, TKey> outerKeySelector,
            Func<TInner, TKey> innerKeySelector,
            Func<TOuter, TInner, TResult> resultSelector)
            => outer.LeftJoin(inner, outerKeySelector, innerKeySelector, resultSelector)
                .Union(
                    outer.RightJoin(inner, outerKeySelector, innerKeySelector, resultSelector)
                    );




        public static Dictionary<TKey, TVal> Merge<TKey, TVal>(
            this Dictionary<TKey, TVal> first, Dictionary<TKey, TVal> second)
        {
            return first.Union(second).ToDictionary(k => k.Key, v => v.Value);
        }

    }
}
