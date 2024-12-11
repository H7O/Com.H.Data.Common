namespace Com.H.Data.Common
{
    internal static class LinqExtensions
    {
        /// <summary>
        /// Filter a dictionary based on the passed keys in orderedFilter argument.
        /// Then return the values those keys represents ordered in the order of the given orderFilter keys
        /// </summary>
        /// <typeparam name="TKey"></typeparam>
        /// <typeparam name="TValue"></typeparam>
        /// <param name="dictionary"></param>
        /// <param name="orderedFilter"></param>
        /// <returns></returns>
        public static IEnumerable<TValue> OrdinalFilter<TKey, TValue>(
            this IDictionary<TKey, TValue> dictionary, IEnumerable<TKey> orderedFilter)
            => orderedFilter.Join(dictionary, o => o, d => d.Key, (o, d) => d.Value);

        /// <summary>
        /// Encloses a signle item into an Enumerable of its type, then returns the resulting Enumerable.
        /// Alternatively, if the object passed is already an Enumerable, it just returns the Enumerable back as is.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="obj"></param>
        /// <returns></returns>
        public static IEnumerable<T> EnsureEnumerable<T>(this object obj)
            =>
                typeof(IEnumerable<T>).IsAssignableFrom(obj.GetType())
                            ? (IEnumerable<T>)obj
                            : Enumerable.Empty<T>().Append((T)obj);

        /// <summary>
        /// Encloses a signle item into an Enumerable of dynamic, then returns the resulting Enumerable.
        /// Also, if the object passed is already an Enumerable, it just returns the Enumerable back as is.
        /// </summary>
        /// <param name="obj"></param>
        /// <returns></returns>
        public static IEnumerable<dynamic> EnsureEnumerable(this object obj)
             => EnsureEnumerable<dynamic>(obj);

        /// <summary>
        /// Finds and returns an item within a hierarchical tree structure by traversing it's child elements using a string
        /// path that denotes its tree elements seperated by a pre-defined string delimiter.
        /// </summary>
        /// <typeparam name="T">Traversable object</typeparam>
        /// <param name="traversableItem">An item that carries children of the same type as itself</param>
        /// <param name="path">A string path representing the decendants tree seperated by a delimiter</param>
        /// <param name="findChild">A delegate that takes a parent element and tries to find a direct decendant wihin its children that corresponds to the child path sub-string</param>
        /// <param name="pathDelimiters">Delimieters to be used to distinguish between decendant elements in the path string</param>
        /// <param name="checkRoot">A delegate that checks the root element for whether or not it correspond to the first path sub-string</param>
        /// <returns>The decendant object</returns>
        public static T? FindDescendant<T>(
            this T traversableItem,
            string path,
            Func<T, string, T?> findChild,
            string[] pathDelimiters,
            Func<T, string, bool>? checkRoot = null
            )
            => string.IsNullOrEmpty(path)
                || EqualityComparer<T>.Default.Equals(traversableItem, default)
                || findChild is null
                || pathDelimiters == null
                || pathDelimiters.Length < 1
                ? default
            : path.Split(pathDelimiters,
                StringSplitOptions.RemoveEmptyEntries)
                .Aggregate((T?)traversableItem, (i, n) =>
                 i is null
                 || EqualityComparer<T>.Default.Equals(i, default)

                  ?
                 default
                 :
                          (
                            // if checkRoot deligate available
                           checkRoot is not null
                            // check if root is matching the first sliced name from path string
                           && checkRoot(i, n)
                            // if checkRoot returns true, this line sets checkRoot to null
                            // so checkRoot won't run in next iteration
                           && (checkRoot = null) == null
                          ) ? traversableItem
                              :
                            // if checkRoot return false, then checkRoot must not be null, hence return default as
                            // this means traversableItem did not satisify checkRoot
                            // otherwise (i.e. checkRoot is null), then start iterating through children
                              (checkRoot is not null ? default : findChild(i, n))
                );


        /// <summary>
        /// Find and return an item within a hierarchical tree structure by traversing it's child elements using a string
        /// path that denotes its tree elements seperated by a pre-defined string delimiter.
        /// </summary>
        /// <typeparam name="T">Traversable object</typeparam>
        /// <param name="traversableItem">An item that carries children of the same type as itself</param>
        /// <param name="path">A string path representing the decendants tree seperated by a delimiter</param>
        /// <param name="findChild">A delegate that takes a parent element and tries to find a direct decendant wihin its children that corresponds to the child path sub-string</param>
        /// <param name="pathDelimiters">Delimieters to be used to distinguish between decendant elements in the path string</param>
        /// <param name="checkRoot">A delegate that checks the root element for whether or not it correspond to the first path sub-string</param>
        /// <returns>The decendant if found, or default T if not</returns>
        public static T? FindDescendant<T>(this T traversableItem,
            string path,
            Func<T, string, T?> findChild,
            char[] pathDelimiters,
            Func<T, string, bool>? checkRoot = null
            )
            => string.IsNullOrEmpty(path)
                || EqualityComparer<T>.Default.Equals(traversableItem, default)
                || findChild is null
                || pathDelimiters == null
                || pathDelimiters.Length < 1
                ? default
            : traversableItem.FindDescendant(path, findChild, pathDelimiters.Select(x=>x.ToString()).ToArray(), checkRoot);



        /// <summary>
        /// Find and return item(s) within a hierarchical tree structure by traversing it's child elements using a string
        /// path that denotes its tree elements seperated by a pre-defined string delimiter.
        /// </summary>
        /// <typeparam name="T">Traversable object</typeparam>
        /// <param name="traversableItem">An item that carries children of the same type as itself</param>
        /// <param name="path">A string path representing the decendants tree seperated by a delimiter</param>
        /// <param name="findChildren">A delegate that takes a parent element and tries to find direct decendants wihin its children that corresponds to the child path sub-string</param>
        /// <param name="pathDelimiters">Delimieters to be used to distinguish between decendant elements in the path string</param>
        /// <param name="checkRoot">A delegate that checks the root element for whether or not it correspond to the first path sub-string</param>
        /// <returns>The decendant(s) if found, or null if not</returns>

        public static IEnumerable<T?>? FindDescendants<T>(
            this T traversableItem,
            string path,
            Func<T, string, IEnumerable<T?>?> findChildren,
            string[] pathDelimiters,
            Func<T, string?, bool>? checkRoot = null
            )
        {
            if (string.IsNullOrEmpty(path)
                  || EqualityComparer<T>.Default.Equals(traversableItem, default)
                  || findChildren is null
                  || pathDelimiters == null
                  || pathDelimiters.Length < 1) return default;

            var nodes = path.Split(pathDelimiters,
                  StringSplitOptions.RemoveEmptyEntries);
            if (checkRoot is not null)
            {
                if (!checkRoot(traversableItem, nodes[0])) return default;
                return FindDescendants(traversableItem, path[nodes[0].Length..], findChildren, pathDelimiters);
            }
            // ignore null reference warnings as the code analyzer unable to detect the references are
            // already pre-checked for nulls
#pragma warning disable CS8604 // Possible null reference argument.
            var result = nodes.Aggregate(Enumerable.Empty<T?>().Append(traversableItem), (i, n) =>
                                  
#pragma warning disable CS8603 // Possible null reference return.
                                  i is null
                                  ?
                                  null
                                  :
                                   i?.Where(x => x is not null && !EqualityComparer<T>.Default.Equals(x, default))
                                       .Select(x => findChildren(x, n))
                                       .Where(x => x is not null)
                                       .SelectMany(x => x)
#pragma warning restore CS8603 // Possible null reference return.
                              ).Where(x => x != null && !EqualityComparer<T>.Default.Equals(x, default));
#pragma warning restore CS8604 // Possible null reference argument.
            return !(result?.Any() == true) ? null : result;

        }


        /// <summary>
        /// Find and return item(s) within a hierarchical tree structure by traversing it's child elements using a string
        /// path that denotes its tree elements seperated by a pre-defined string delimiter.
        /// </summary>
        /// <typeparam name="T">Traversable object</typeparam>
        /// <param name="traversableItem">An item that carries children of the same type as itself</param>
        /// <param name="path">A string path representing the decendants tree seperated by a delimiter</param>
        /// <param name="findChildren">A delegate that takes a parent element and tries to find direct decendants wihin its children that corresponds to the child path sub-string</param>
        /// <param name="pathDelimiters">Delimieters to be used to distinguish between decendant elements in the path string</param>
        /// <param name="checkRoot">A delegate that checks the root element for whether or not it correspond to the first path sub-string</param>
        /// <returns>The decendant(s) if found, or null if not</returns>

        public static IEnumerable<T?>? FindDescendants<T>(
            this T traversableItem,
            string path,
            Func<T, string, IEnumerable<T?>?> findChildren,
            char[] pathDelimiters,
            Func<T, string?, bool>? checkRoot = null
            ) =>
                string.IsNullOrEmpty(path)
                      || EqualityComparer<T>.Default.Equals(traversableItem, default)
                      || findChildren is null
                      || pathDelimiters == null
                      || pathDelimiters.Length < 1 ? default 
                      : FindDescendants(traversableItem, 
                      path, 
                      findChildren, 
                      pathDelimiters.Select(x=>x.ToString()).ToArray(),
                      checkRoot);


        


        /// <summary>
        /// Support until logic for Aggregate
        /// </summary>
        /// <typeparam name="TSource"></typeparam>
        /// <typeparam name="TAccumulate"></typeparam>
        /// <param name="source"></param>
        /// <param name="seed"></param>
        /// <param name="func"></param>
        /// <param name="untilCheck"></param>
        /// <returns></returns>
        /// <exception cref="ArgumentNullException"></exception>
        public static TAccumulate? AggregateUntil<TSource, TAccumulate>(
            this IEnumerable<TSource> source,
            TAccumulate? seed,
            Func<TAccumulate?, TSource?, TAccumulate?> func,
            Func<TAccumulate?, TSource?, bool> untilCheck)
        {
            ArgumentNullException.ThrowIfNull(source);

            ArgumentNullException.ThrowIfNull(func);

            ArgumentNullException.ThrowIfNull(untilCheck);

            _ = source.Any(x => untilCheck(seed = func(seed, x), x));

            return seed;
        }

        public static TAccumulate? AggregateWhile<TSource, TAccumulate>(
            this IEnumerable<TSource> source,
            TAccumulate? seed,
            Func<TAccumulate?, TSource?, TAccumulate?> func,
            Func<TAccumulate?, TSource?, bool> whileCheck)
        {
            ArgumentNullException.ThrowIfNull(source);

            ArgumentNullException.ThrowIfNull(func);

            ArgumentNullException.ThrowIfNull(whileCheck);
            foreach (var item in source)
            {
                if (!whileCheck(seed, item)) return seed;
                seed = func(seed, item);
            }
            return seed;
        }


    }
}
