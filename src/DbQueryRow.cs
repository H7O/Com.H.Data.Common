using System.Collections;
using System.ComponentModel;
using System.Dynamic;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Com.H.Data.Common
{
    /// <summary>
    /// A row a query returned: its columns by name, in the order the query returned them, each
    /// value as the driver read it, and <c>null</c> for a database <c>NULL</c>. Read it as
    /// <c>dynamic</c> (<c>row.name</c>) or as an <see cref="IDictionary{TKey, TValue}"/>. Names are
    /// compared as written (case-sensitive), as an <see cref="ExpandoObject"/> compares them.
    /// </summary>
    /// <remarks>
    /// The rows of one result share their column names, so a row holds only its values. Building
    /// one takes two allocations, where an ExpandoObject grows one column at a time. Adding or
    /// removing a column gives the row names of its own, and leaves the other rows as they are.
    /// System.Text.Json writes a row as an object of its columns, as it writes an ExpandoObject,
    /// and reads one with each value as a <see cref="JsonElement"/>. A row is not safe to change
    /// from several threads at once.
    /// </remarks>
    [JsonConverter(typeof(DbQueryRowJsonConverter))]
    public sealed class DbQueryRow : DynamicObject, IDictionary<string, object?>, INotifyPropertyChanged
    {
        private Columns _columns;
        private object?[] _values;

        /// <summary>A row without columns, for columns added later.</summary>
        public DbQueryRow() : this(Columns.Empty, []) { }

        internal DbQueryRow(Columns columns, object?[] values)
        {
            _columns = columns;
            _values = values;
        }

        /// <summary>Raised when a column is set, added or removed, with the column's name.</summary>
        public event PropertyChangedEventHandler? PropertyChanged;

        internal Columns ColumnNames => _columns;

        internal object?[] ColumnValues => _values;

        /// <inheritdoc/>
        public int Count => _columns.Names.Length;

        /// <inheritdoc/>
        public object? this[string key]
        {
            get => TryGetValue(key, out var value)
                ? value
                : throw new KeyNotFoundException($"The given key '{key}' was not present in the dictionary.");
            set => Set(key, value);
        }

        /// <summary>The column names, in order: a copy.</summary>
        public ICollection<string> Keys => (string[])_columns.Names.Clone();

        /// <summary>The values, in the columns' order: a copy.</summary>
        public ICollection<object?> Values => (object?[])_values.Clone();

        bool ICollection<KeyValuePair<string, object?>>.IsReadOnly => false;

        /// <inheritdoc/>
        public bool ContainsKey(string key) => _columns.IndexOf(key) >= 0;

        /// <inheritdoc/>
        public bool TryGetValue(string key, out object? value)
        {
            var index = _columns.IndexOf(key);
            value = index >= 0 ? _values[index] : null;
            return index >= 0;
        }

        /// <inheritdoc/>
        public void Add(string key, object? value)
        {
            if (ContainsKey(key))
                throw new ArgumentException($"An item with the same key has already been added. Key: {key}", nameof(key));
            Append(key, value);
        }

        /// <inheritdoc/>
        public bool Remove(string key)
        {
            var index = _columns.IndexOf(key);
            if (index < 0)
                return false;
            var values = new object?[_values.Length - 1];
            Array.Copy(_values, 0, values, 0, index);
            Array.Copy(_values, index + 1, values, index, values.Length - index);
            _columns = _columns.Without(index);
            _values = values;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(key));
            return true;
        }

        /// <inheritdoc/>
        public void Clear()
        {
            var names = _columns.Names;
            _columns = Columns.Empty;
            _values = [];
            foreach (var name in names)
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        void ICollection<KeyValuePair<string, object?>>.Add(KeyValuePair<string, object?> item) => Add(item.Key, item.Value);

        bool ICollection<KeyValuePair<string, object?>>.Contains(KeyValuePair<string, object?> item)
            => TryGetValue(item.Key, out var value) && Equals(value, item.Value);

        bool ICollection<KeyValuePair<string, object?>>.Remove(KeyValuePair<string, object?> item)
            => ((ICollection<KeyValuePair<string, object?>>)this).Contains(item) && Remove(item.Key);

        void ICollection<KeyValuePair<string, object?>>.CopyTo(KeyValuePair<string, object?>[] array, int arrayIndex)
        {
            if (array == null) throw new ArgumentNullException(nameof(array));
            if (arrayIndex < 0 || array.Length - arrayIndex < Count) throw new ArgumentOutOfRangeException(nameof(arrayIndex));
            for (int i = 0; i < Count; i++)
                array[arrayIndex + i] = new KeyValuePair<string, object?>(_columns.Names[i], _values[i]);
        }

        /// <summary>The columns in order. A change to the row while it is enumerated is not seen.</summary>
        public IEnumerator<KeyValuePair<string, object?>> GetEnumerator()
        {
            var names = _columns.Names;
            var values = _values;
            for (int i = 0; i < names.Length; i++)
                yield return new KeyValuePair<string, object?>(names[i], values[i]);
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        /// <inheritdoc/>
        public override bool TryGetMember(GetMemberBinder binder, out object? result)
        {
            var index = MemberIndex(binder.Name, binder.IgnoreCase);
            result = index >= 0 ? _values[index] : null;
            return index >= 0;
        }

        /// <inheritdoc/>
        public override bool TrySetMember(SetMemberBinder binder, object? value)
        {
            var index = MemberIndex(binder.Name, binder.IgnoreCase);
            if (index >= 0)
                SetAt(index, value);
            else
                Append(binder.Name, value);
            return true;
        }

        /// <inheritdoc/>
        public override bool TryDeleteMember(DeleteMemberBinder binder)
        {
            var index = MemberIndex(binder.Name, binder.IgnoreCase);
            return index >= 0 && Remove(_columns.Names[index]);
        }

        /// <inheritdoc/>
        public override IEnumerable<string> GetDynamicMemberNames() => _columns.Names;

        // A language that ignores case (Visual Basic) finds a column written in any case.
        private int MemberIndex(string name, bool ignoreCase)
        {
            var index = _columns.IndexOf(name);
            return index >= 0 || !ignoreCase
                ? index
                : Array.FindIndex(_columns.Names, n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase));
        }

        private void Set(string key, object? value)
        {
            var index = _columns.IndexOf(key);
            if (index >= 0)
                SetAt(index, value);
            else
                Append(key, value);
        }

        private void SetAt(int index, object? value)
        {
            _values[index] = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(_columns.Names[index]));
        }

        private void Append(string key, object? value)
        {
            if (key == null) throw new ArgumentNullException(nameof(key));
            var values = new object?[_values.Length + 1];
            Array.Copy(_values, values, _values.Length);
            values[values.Length - 1] = value;
            _columns = _columns.With(key);
            _values = values;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(key));
        }

        /// <summary>
        /// The column names of a result's rows, shared by them, with each name's position and its
        /// JSON encoding.
        /// </summary>
        internal sealed class Columns
        {
            internal static readonly Columns Empty = new([]);

            private readonly Dictionary<string, int> _index;
            private Encoded? _encoded;

            internal Columns(string[] names)
            {
                Names = names;
                _index = new Dictionary<string, int>(names.Length, StringComparer.Ordinal);
                for (int i = 0; i < names.Length; i++)
                    _index[names[i]] = i;
            }

            internal string[] Names { get; }

            internal int IndexOf(string name) => _index.TryGetValue(name, out var index) ? index : -1;

            internal Columns With(string name)
            {
                var names = new string[Names.Length + 1];
                Array.Copy(Names, names, Names.Length);
                names[names.Length - 1] = name;
                return new Columns(names);
            }

            internal Columns Without(int index)
                => new(Names.Where((_, i) => i != index).ToArray());

            /// <summary>
            /// The names as the writer's encoder escapes them, worked out once for the rows of a
            /// result: a response is written with one encoder.
            /// </summary>
            internal JsonEncodedText[] EncodedNames(JavaScriptEncoder? encoder)
            {
                var encoded = _encoded;
                if (encoded != null && ReferenceEquals(encoded.Encoder, encoder))
                    return encoded.Names;
                var names = new JsonEncodedText[Names.Length];
                for (int i = 0; i < names.Length; i++)
                    names[i] = JsonEncodedText.Encode(Names[i], encoder);
                _encoded = new Encoded(encoder, names);
                return names;
            }

            private sealed class Encoded(JavaScriptEncoder? encoder, JsonEncodedText[] names)
            {
                public JavaScriptEncoder? Encoder { get; } = encoder;
                public JsonEncodedText[] Names { get; } = names;
            }
        }
    }

    /// <summary>
    /// Writes a <see cref="DbQueryRow"/> as System.Text.Json writes an ExpandoObject with the same
    /// columns, byte for byte, and reads one as it reads an ExpandoObject. The values databases
    /// return most are written with the writer methods the serializer's own converters call, and
    /// every other value is handed to the serializer.
    /// </summary>
    internal sealed class DbQueryRowJsonConverter : JsonConverter<DbQueryRow>
    {
        public override DbQueryRow? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.StartObject)
                throw new JsonException($"A {nameof(DbQueryRow)} is read from a JSON object, not a {reader.TokenType}.");
            var row = new DbQueryRow();
            while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
            {
                var name = reader.GetString()!;
                reader.Read();
                row[name] = JsonSerializer.Deserialize<JsonElement>(ref reader, options);
            }
            return row;
        }

        public override void Write(Utf8JsonWriter writer, DbQueryRow row, JsonSerializerOptions options)
        {
            // The writer escapes with its own encoder, which the serializer sets from the options.
            var names = row.ColumnNames.EncodedNames(writer.Options.Encoder);
            var values = row.ColumnValues;
            writer.WriteStartObject();
            for (int i = 0; i < names.Length; i++)
            {
                writer.WritePropertyName(names[i]);
                switch (values[i])
                {
                    case null: writer.WriteNullValue(); break;
                    case string text: writer.WriteStringValue(text); break;
                    case int number: writer.WriteNumberValue(number); break;
                    case long number: writer.WriteNumberValue(number); break;
                    case decimal number: writer.WriteNumberValue(number); break;
                    // NaN and the infinities go to the serializer, which refuses them unless the
                    // options allow them.
                    case double number when !double.IsNaN(number) && !double.IsInfinity(number): writer.WriteNumberValue(number); break;
                    case bool flag: writer.WriteBooleanValue(flag); break;
                    case DateTime time: writer.WriteStringValue(time); break;
                    case Guid id: writer.WriteStringValue(id); break;
                    case var value: JsonSerializer.Serialize(writer, value, value.GetType(), options); break;
                }
            }
            writer.WriteEndObject();
        }
    }
}
