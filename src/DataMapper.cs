using System.Collections.Concurrent;
using System.Dynamic;
using System.Globalization;
using System.Reflection;


namespace Com.H.Data.Common
{
    /// <summary>
    /// Provides object-to-object mapping functionality with property matching and type conversion.
    /// </summary>
    internal class DataMapper
    {
        private readonly ConcurrentDictionary<Type, (string Name, PropertyInfo Info)[]> _typesProperties =
            new();


        /// <summary>
        /// Gets cached property information for the specified type, including custom column names from DataMember or JsonPropertyName attributes.
        /// </summary>
        /// <param name="type">The type to get properties for</param>
        /// <returns>Array of tuples containing property names and PropertyInfo objects</returns>
        /// <exception cref="ArgumentNullException">Thrown when type is null</exception>
        public (string Name, PropertyInfo Info)[] GetCachedProperties(Type type)
        {
            if (type == null) throw new ArgumentNullException(nameof(type));
            return _typesProperties.ContainsKey(type) ?
                                _typesProperties[type]
                                : (_typesProperties[type] = GetPropertiesWithColumnName(type).ToArray());
        }
        /// <summary>
        /// Gets cached property information for the specified object, supporting ExpandoObject and regular objects.
        /// </summary>
        /// <param name="obj">The object to get properties for</param>
        /// <returns>Array of tuples containing property names and PropertyInfo objects</returns>
        /// <exception cref="ArgumentNullException">Thrown when obj is null</exception>
        public (string Name, PropertyInfo Info)[] GetCachedProperties(object obj)
        {
            if (obj == null) throw new ArgumentNullException(nameof(obj));
            if (obj.GetType() == typeof(ExpandoObject))
                return ((ExpandoObject)obj).GetProperties().ToArray();
            return GetCachedProperties(obj.GetType());
        }



        private static IEnumerable<(string Name, PropertyInfo Info)> GetPropertiesWithColumnName(Type type)
        {
            foreach (var p in type.GetProperties())
            {
                //ColumnAttribute? c_name = (ColumnAttribute?)p
                //    .GetCustomAttributes(typeof(ColumnAttribute), false).FirstOrDefault();

                var c_attr = p.GetCustomAttributes(false)
                    .FirstOrDefault(x => x.GetType().Name.Equals("DataMemberAttribute")) ??
                    p.GetCustomAttributes(false)
                    .FirstOrDefault(x => x.GetType().Name.Equals("JsonPropertyNameAttribute"));

                // yield return (c_name?.Name??p.Name, p);
                yield return (c_attr?.GetType().GetProperty("Name")?.GetValue(c_attr)?.ToString() ?? p.Name, p);
            }

        }
        /// <summary>
        /// Maps a collection of source objects to a collection of type T.
        /// </summary>
        /// <typeparam name="T">The destination type</typeparam>
        /// <param name="source">The source collection to map from</param>
        /// <returns>A collection of mapped objects</returns>
        public IEnumerable<T?>? Map<T>(IEnumerable<object>? source)
        {
            if (source == null) return null;
            return source.Select(x => this.Map<T>(x));
        }
        /// <summary>
        /// Maps a source object to the specified destination type using reflection.
        /// </summary>
        /// <param name="source">The source object to map from</param>
        /// <param name="type">The destination type</param>
        /// <returns>The mapped object</returns>
        public object? Map(object source, Type type)
            => type.GetMethod("Map")?.MakeGenericMethod(type)?
            .Invoke(this, new object[] { source });

        /// <summary>
        /// Maps a source object to a new instance of type T, supporting dictionaries, ExpandoObjects, and regular objects.
        /// Performs property name matching (case-insensitive) and automatic type conversion.
        /// </summary>
        /// <typeparam name="T">The destination type</typeparam>
        /// <param name="source">The source object to map from</param>
        /// <returns>A new instance of type T with mapped properties</returns>
        public T? Map<T>(object source)
        {
            if (source == null) return default;

            if (!typeof(IDictionary<string, object>).IsAssignableFrom(source.GetType()))
                return this.MapNormal<T>(source);
            //var srcProperties = source as IDictionary<string, object?>;
            var dstProperties = this.GetCachedProperties(typeof(T));
            //if (srcProperties is null) return default;

            if (source is not IDictionary<string, object?> srcProperties) return default;


            var joined = dstProperties.LeftJoin(
                srcProperties,
                dst => dst.Name.ToUpper(CultureInfo.InvariantCulture),
                src => src.Key.ToUpper(CultureInfo.InvariantCulture),
                (dst, src) => new { dst, src }
            );

            T destination = Activator.CreateInstance<T>();

            foreach (var item in joined.Where(x => x.src.Key != null))
            {
                try
                {
                    if (item.src.Value == null)
                    {
                        item.dst.Info.SetValue(destination, null);
                    }
                    else
                    {
                        Type targetType = Nullable.GetUnderlyingType(item.dst.Info.PropertyType) ?? item.dst.Info.PropertyType;
                        item.dst.Info.SetValue(destination,
                            Convert.ChangeType(item.src.Value, targetType, CultureInfo.InvariantCulture)
                        );
                    }
                }
                catch { }
            }
            return destination;
        }

        private static bool IsSimpleType(Type type)
        {
            if (type == null) return false;
            return type.IsPrimitive ||
                   type == typeof(string) ||
                   type == typeof(decimal) ||
                   type == typeof(DateTime) ||
                   type == typeof(Guid) ||
                   type == typeof(DateTimeOffset) ||
                   type == typeof(TimeSpan) ||
                   (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Nullable<>) &&
                    IsSimpleType(Nullable.GetUnderlyingType(type)!));
        }

        private T? MapNormal<T>(object source)
        {
            if (source == null) return default;

            if (source.GetType() == typeof(T))
                return (T)source;


            // check if it's a primitive type or string, decimal, DateTime, Guid, etc..
            if (IsSimpleType(source.GetType()))
            {
                Type targetType = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);
                return (T)Convert.ChangeType(source, targetType, CultureInfo.InvariantCulture);
            }


            var srcProperties = this.GetCachedProperties(source.GetType());
            var dstProperties = this.GetCachedProperties(typeof(T));


            var joined = dstProperties.LeftJoin(
                srcProperties,
                dst => dst.Name.ToUpper(CultureInfo.InvariantCulture),
                src => src.Name.ToUpper(CultureInfo.InvariantCulture),
                (dst, src) => new { dst, src }
            ).Where(x => x.src.Info != null);

            // Console.WriteLine(joined.Count);

            T destination = Activator.CreateInstance<T>();

            foreach (var item in joined)
            {
                try
                {
                    // Console.WriteLine($"src: {item.src.Name} = {item.src.Info?.GetValue(source)}");
                    var val = item.src.Info.GetValue(source);

                    if (val is null) continue;
                    if (item.src.Info.PropertyType == item.dst.Info.PropertyType)
                        item.dst.Info.SetValue(destination, val);
                    else
                    {
                        Type targetType = Nullable.GetUnderlyingType(item.dst.Info.PropertyType) 
                            ?? item.dst.Info.PropertyType;
                        item.dst.Info.SetValue(destination,
                            Convert.ChangeType(val,
                            targetType, CultureInfo.InvariantCulture)
                        );
                    }
                }
                catch {}
            }
            return destination;
        }


        /// <summary>
        /// Creates a deep clone of the source object by mapping it to a new instance of the same type.
        /// </summary>
        /// <typeparam name="T">The type of the object to clone</typeparam>
        /// <param name="source">The source object to clone</param>
        /// <returns>A deep clone of the source object</returns>
        public T? Clone<T>(T source)
            => source is null ? default : this.Map<T>(source);


        /// <summary>
        /// Fills the destination object with property values from the source object.
        /// Performs property name matching (case-insensitive) and automatic type conversion.
        /// </summary>
        /// <param name="destination">The destination object to fill</param>
        /// <param name="source">The source object to get property values from</param>
        /// <param name="skipNull">If true, properties with null values in the source will not overwrite destination properties</param>
        public void FillWith(
            object destination,
            object source,
            bool skipNull = false
            )
        {
            if (source == null || destination == null) return;

            var srcProperties = _typesProperties.ContainsKey(source.GetType()) ?
                                _typesProperties[source.GetType()]
                                : (_typesProperties[source.GetType()] =
                                    GetPropertiesWithColumnName(source.GetType()).ToArray());

            var dstProperties = _typesProperties.ContainsKey(destination.GetType()) ?
                                _typesProperties[destination.GetType()]
                                : (_typesProperties[destination.GetType()] =
                                GetPropertiesWithColumnName(destination.GetType()).ToArray());


            var joined = dstProperties.LeftJoin(
                srcProperties,
                dst => dst.Name.ToUpper(CultureInfo.InvariantCulture),
                src => src.Name.ToUpper(CultureInfo.InvariantCulture),
                (dst, src) => new { dst, src }
            );

            foreach (var item in joined.Where(x => (!skipNull || x.src.Info != null)))
            {
                try
                {
                    if (item.src.Info == null)
                    {
                        item.dst.Info.SetValue(destination, null);
                    }
                    else
                    {
                        var sourceValue = item.src.Info.GetValue(source);
                        if (sourceValue == null)
                        {
                            item.dst.Info.SetValue(destination, null);
                        }
                        else
                        {
                            Type targetType = Nullable.GetUnderlyingType(item.dst.Info.PropertyType) ?? item.dst.Info.PropertyType;
                            item.dst.Info.SetValue(destination,
                                Convert.ChangeType(sourceValue, targetType, CultureInfo.InvariantCulture)
                            );
                        }
                    }
                }
                catch { }
            }
        }
    }
}