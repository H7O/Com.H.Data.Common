using System;
using System.Collections.Generic;
using System.Dynamic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace Com.H.Data.Common
{
    internal static class ReflectionExtensions
    {
        private static readonly DataMapper _mapper = new DataMapper();

        public static (string Name, PropertyInfo Info)[] GetCachedProperties(this Type type)
            => _mapper.GetCachedProperties(type);

        public static (string Name, PropertyInfo Info)[] GetCachedProperties(this object obj)
            => _mapper.GetCachedProperties(obj);

        public static T Map<T>(this object source)
            => _mapper.Map<T>(source);

        public static object Map(this object source, Type dstType)
            => _mapper.Map(source, dstType);

        public static T Clone<T>(this T source)
            => _mapper.Clone<T>(source);

        public static IEnumerable<T> Map<T>(this IEnumerable<object> source)
            => source == null ? null : _mapper.Map<T>(source);

        public static void FillWith(
            this object destination,
            object source,
            bool skipNull = false
            )
            => _mapper.FillWith(destination, source, skipNull);

        /// <summary>
        /// Rrturns values of IDictionary after filtering them based on an IEnumerable of keys.
        /// The filter keys don't have to be of the same type as the IDictionary keys.
        /// They only need to be mappable to IDictionary keys type (i.e. can be conerted to IDicionary keys type)
        /// </summary>
        /// <typeparam name="TKey"></typeparam>
        /// <typeparam name="TValue"></typeparam>
        /// <typeparam name="TOKey"></typeparam>
        /// <param name="dictionary"></param>
        /// <param name="oFilter"></param>
        /// <returns></returns>
        public static IEnumerable<TValue> OrdinallyMappedFilteredValues<TKey, TValue, TOKey>(
            IDictionary<TKey, TValue> dictionary, IEnumerable<TOKey> oFilter)
        =>
            oFilter is null ? dictionary.Values.AsEnumerable()
            : oFilter.Where(x => x != null).Join(dictionary, o => o.Map<TKey>(), d => d.Key, (o, d) => d.Value);



        public static IEnumerable<(string Name, PropertyInfo Info)> GetProperties(this ExpandoObject expando)
        {
            if (expando == null) throw new ArgumentNullException(nameof(expando));
            foreach (var p in expando)
            {
                yield return (p.Key, new DynamicPropertyInfo(p.Key, p.Value?.GetType() ?? typeof(string)));
            }
        }


        public static object GetDefault(this Type type)
            => ((Func<object>)GetDefault<object>)?.Method?.GetGenericMethodDefinition()?
            .MakeGenericMethod(type)?.Invoke(null, null);

        private static T GetDefault<T>()
            => default;


        public static object ConvertTo(this object obj, Type type)
        {
            Type dstType = Nullable.GetUnderlyingType(type) ?? type;
            return (obj == null || DBNull.Value.Equals(obj)) ?
               type.GetDefault() : Convert.ChangeType(obj, dstType);
        }

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
                throw new ArgumentNullException($"{nameof(assemblyNameOrDllPath)} should not be null or white space");
            // return the assembly if it is already loaded
            Assembly assembly = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == assemblyNameOrDllPath);
            if (assembly != null) return assembly;

            if (!File.Exists(assemblyNameOrDllPath))
            {
                var assemblyPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, assemblyNameOrDllPath);
                if (File.Exists(assemblyPath))
                    assemblyNameOrDllPath = assemblyPath;
                else
                {
                    var executableAssemblyPath = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                    if (executableAssemblyPath != null)
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