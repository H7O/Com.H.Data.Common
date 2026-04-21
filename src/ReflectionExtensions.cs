using System.Dynamic;
using System.Reflection;

namespace Com.H.Data.Common
{
    internal static class ReflectionExtensions
    {
        private static readonly DataMapper _mapper = new();

        /// <summary>
        /// Gets cached property information for the specified type.
        /// </summary>
        /// <param name="type">The type to get properties for</param>
        /// <returns>Array of tuples containing property names and PropertyInfo objects</returns>
        public static (string Name, PropertyInfo Info)[] GetCachedProperties(this Type type)
            => _mapper.GetCachedProperties(type);

        /// <summary>
        /// Gets cached property information for the specified object.
        /// </summary>
        /// <param name="obj">The object to get properties for</param>
        /// <returns>Array of tuples containing property names and PropertyInfo objects</returns>
        public static (string Name, PropertyInfo Info)[] GetCachedProperties(this object obj)
            => _mapper.GetCachedProperties(obj);

        /// <summary>
        /// Maps properties from a source object to a new instance of type T.
        /// </summary>
        /// <typeparam name="T">The destination type</typeparam>
        /// <param name="source">The source object to map from</param>
        /// <returns>A new instance of type T with mapped properties</returns>
        public static T? Map<T>(this object source)
            => _mapper.Map<T>(source);

        /// <summary>
        /// Maps properties from a source object to a new instance of the specified type.
        /// </summary>
        /// <param name="source">The source object to map from</param>
        /// <param name="dstType">The destination type</param>
        /// <returns>A new instance of the destination type with mapped properties</returns>
        public static object? Map(this object source, Type dstType)
            => _mapper.Map(source, dstType);

        /// <summary>
        /// Creates a deep clone of the source object.
        /// </summary>
        /// <typeparam name="T">The type of the object to clone</typeparam>
        /// <param name="source">The source object to clone</param>
        /// <returns>A deep clone of the source object</returns>
        public static T? Clone<T>(this T source)
            => _mapper.Clone<T>(source);

        /// <summary>
        /// Maps a collection of objects to a collection of type T.
        /// </summary>
        /// <typeparam name="T">The destination type</typeparam>
        /// <param name="source">The source collection to map from</param>
        /// <returns>A collection of mapped objects</returns>
        public static IEnumerable<T?>? Map<T>(this IEnumerable<object> source)
            => source==null?null:_mapper.Map<T>(source);

        /// <summary>
        /// Fills the destination object with properties from the source object.
        /// </summary>
        /// <param name="destination">The destination object to fill</param>
        /// <param name="source">The source object to get property values from</param>
        /// <param name="skipNull">If true, null values from the source will not overwrite destination properties</param>
        public static void FillWith(
            this object destination,
            object source,
            bool skipNull = false
            )
            => _mapper.FillWith(destination, source, skipNull);

        /// <summary>
        /// Returns values of IDictionary after filtering them based on an IEnumerable of keys.
        /// The filter keys don't have to be of the same type as the IDictionary keys.
        /// They only need to be mappable to IDictionary keys type (i.e. can be converted to IDictionary keys type)
        /// </summary>
        /// <typeparam name="TKey">The type of the dictionary keys</typeparam>
        /// <typeparam name="TValue">The type of the dictionary values</typeparam>
        /// <typeparam name="TOKey">The type of the filter keys</typeparam>
        /// <param name="dictionary">The dictionary to filter</param>
        /// <param name="oFilter">The keys to filter by</param>
        /// <returns>Filtered values from the dictionary</returns>
        public static IEnumerable<TValue> OrdinallyMappedFilteredValues<TKey, TValue, TOKey>(
            IDictionary<TKey, TValue> dictionary, IEnumerable<TOKey> oFilter) where TOKey: notnull
        =>
            oFilter is null?dictionary.Values.AsEnumerable()
            :oFilter.Where(x=>x is not null).Join(dictionary, o => o.Map<TKey>(), d => d.Key, (o, d) => d.Value);



        /// <summary>
        /// Gets property information from an ExpandoObject.
        /// </summary>
        /// <param name="expando">The ExpandoObject to get properties from</param>
        /// <returns>Enumerable of property name and PropertyInfo tuples</returns>
        /// <exception cref="ArgumentNullException">Thrown when expando is null</exception>
        public static IEnumerable<(string Name, PropertyInfo Info)> GetProperties(this ExpandoObject expando)
        {
            if (expando == null) throw new ArgumentNullException(nameof(expando));
            foreach (var p in expando)
            {
                yield return (p.Key, new DynamicPropertyInfo(p.Key, p.Value?.GetType() ?? typeof(string)));
            }
        }


        /// <summary>
        /// Gets the default value for a specified type.
        /// </summary>
        /// <param name="type">The type to get the default value for</param>
        /// <returns>The default value for the type (null for reference types, 0 or equivalent for value types)</returns>
        public static object? GetDefault(this Type type)
            => ((Func<object?>)GetDefault<object>)?.Method?.GetGenericMethodDefinition()?
            .MakeGenericMethod(type)?.Invoke(null, null);

        private static T? GetDefault<T>()
            => default;


        /// <summary>
        /// Converts an object to the specified type, handling null and DBNull values.
        /// </summary>
        /// <param name="obj">The object to convert</param>
        /// <param name="type">The target type</param>
        /// <returns>The converted object, or the default value for the type if obj is null or DBNull</returns>
        public static object? ConvertTo(this object obj, Type type)
        {
            Type dstType = Nullable.GetUnderlyingType(type) ?? type;
            return (obj == null || DBNull.Value.Equals(obj)) ?
               type.GetDefault() : Convert.ChangeType(obj, dstType);
        }

        /// <summary>
        /// Determines whether a value is the default value for its type.
        /// </summary>
        /// <typeparam name="T">The value type</typeparam>
        /// <param name="value">The value to check</param>
        /// <returns>True if the value equals the default for its type, false otherwise</returns>
        public static bool IsDefault<T>(this T value) where T : struct
            => value.Equals(default(T));


        /// <summary>
        /// Attempts to load assembly by either assembly name or dll path
        /// </summary>
        /// <param name="assemblyNameOrDllPath"></param>
        /// <returns></returns>
        /// <exception cref="ArgumentNullException"></exception>
        /// <exception cref="Exception"></exception>
        public static Assembly LoadAssembly(this string assemblyNameOrDllPath)
        {
            if (string.IsNullOrWhiteSpace(assemblyNameOrDllPath))
                throw new($"{nameof(assemblyNameOrDllPath)} should not be null or white space");
            // return the assembly if it is already loaded
            Assembly? assembly = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == assemblyNameOrDllPath);
            if (assembly is not null) return assembly;

            if (!File.Exists(assemblyNameOrDllPath))
            {
                var assemblyPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, assemblyNameOrDllPath);
                if (File.Exists(assemblyPath))
                    assemblyNameOrDllPath = assemblyPath;
                else
                {
                    var executableAssemblyPath = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                    if (executableAssemblyPath is not null)
                    {
                        assemblyPath = Path.Combine(executableAssemblyPath, assemblyNameOrDllPath);
                        if (File.Exists(assemblyPath))
                            assemblyNameOrDllPath = assemblyPath;
                    }
                }
            }


            if (File.Exists(assemblyNameOrDllPath))
            {
                try
                {
                    assembly = Assembly.LoadFrom(assemblyNameOrDllPath);
                }
                catch (Exception ex)
                {
                    throw new Exception($"Failed to load assembly from {assemblyNameOrDllPath} with error {ex.Message}");
                }
            }
            else
            {
                try
                {
                    assembly = Assembly.Load(assemblyNameOrDllPath);
                }
                catch (Exception ex)
                {
                    throw new Exception($"Failed to load assembly {assemblyNameOrDllPath} with error {ex.Message}");
                }
            }
            return assembly;

        }
    }
}