using Com.H.Data.Common;
using Microsoft.CSharp.RuntimeBinder;
using Microsoft.Data.Sqlite;
using System.ComponentModel;
using System.Data.Common;
using System.Dynamic;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Tests;

/// <summary>
/// A query's rows are DbQueryRows. Each one holds what the ExpandoObject the library returned
/// before held, behaves as one when read as dynamic or as a dictionary, and is written to JSON
/// byte for byte as one, whatever the options.
/// </summary>
public class DbQueryRowTests : IDisposable
{
    private readonly DbConnection _connection;

    // Every type SQLite returns, a name used twice, and names a JSON writer escapes.
    private const string Query = """
        SELECT 1 AS id, 'Ann' AS name, 2.5 AS amount, NULL AS missing, x'0102FF' AS bytes,
               9007199254740993 AS big, 'second' AS name, 'é' AS "é", 3 AS "a""b", 4 AS "<tag>", 5 AS "Name"
        UNION ALL
        SELECT 2, 'Bob', -0.5, NULL, x'', -1, 'x', 'ü', 0, 0, 6
        """;

    public DbQueryRowTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
    }

    public void Dispose() => _connection.Dispose();

    private List<object> Rows(string query = Query) => ((IEnumerable<object>)_connection.ExecuteQuery(query)).ToList();

    // The row the library built before: an ExpandoObject filled with TryAdd, a column at a time.
    private List<ExpandoObject> ExpandoRows(string query = Query)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = query;
        using var reader = command.ExecuteReader();
        var rows = new List<ExpandoObject>();
        while (reader.Read())
        {
            var row = (IDictionary<string, object?>)new ExpandoObject();
            for (int i = 0; i < reader.FieldCount; i++)
            {
                var value = reader.GetValue(i);
                if (!row.ContainsKey(reader.GetName(i)))
                    row.Add(reader.GetName(i), value is DBNull ? null : value);
            }
            rows.Add((ExpandoObject)row);
        }
        return rows;
    }

    [Fact]
    public void EachRow_HoldsWhatItsExpandoObjectHeld()
    {
        var rows = Rows();
        var expected = ExpandoRows();

        Assert.Equal(expected.Count, rows.Count);
        for (int i = 0; i < rows.Count; i++)
        {
            var row = Assert.IsType<DbQueryRow>(rows[i]);
            Assert.Equal(((IDictionary<string, object?>)expected[i]).ToList(), row.ToList());
        }
        // The first column of a name used twice
        Assert.Equal("Ann", ((DbQueryRow)rows[0])["name"]);
    }

    [Fact]
    public void ARow_ReadAsDynamic()
    {
        dynamic row = Rows()[0];

        Assert.Equal(1L, (long)row.id);
        Assert.Equal("Ann", (string)row.name);
        Assert.Null(row.missing);
        Assert.Equal(5L, (long)row.Name);   // names are case-sensitive, as an ExpandoObject's are
        Assert.Throws<RuntimeBinderException>(() => row.nothing);

        row.name = "Anna";
        row.added = 7;
        Assert.Equal("Anna", (string)row.name);
        Assert.Equal(7, (int)row.added);
        Assert.Contains("added", ((DbQueryRow)row).GetDynamicMemberNames());
    }

    [Fact]
    public void ARow_ReadAsADictionary()
    {
        var row = (IDictionary<string, object?>)Rows()[0];

        Assert.Equal(new[] { "id", "name", "amount", "missing", "bytes", "big", "é", "a\"b", "<tag>", "Name" }, row.Keys);
        Assert.Equal(10, row.Count);
        Assert.True(row.ContainsKey("name"));
        Assert.False(row.ContainsKey("NAME"));
        Assert.True(row.TryGetValue("missing", out var missing));
        Assert.Null(missing);
        Assert.False(row.TryGetValue("nothing", out _));
        Assert.Throws<KeyNotFoundException>(() => row["nothing"]);
        Assert.Throws<ArgumentException>(() => row.Add("id", 2));
        Assert.Contains(new KeyValuePair<string, object?>("name", "Ann"), row);

        var copy = new KeyValuePair<string, object?>[11];
        row.CopyTo(copy, 1);
        Assert.Equal(row.ToList(), copy.Skip(1));

        row["id"] = 10;
        row["added"] = "new";
        Assert.True(row.Remove("amount"));
        Assert.False(row.Remove("amount"));
        Assert.Equal(new[] { "id", "name", "missing", "bytes", "big", "é", "a\"b", "<tag>", "Name", "added" }, row.Keys);
        Assert.Equal(10, row["id"]);
        Assert.Equal("new", row["added"]);

        row.Clear();
        Assert.Empty(row);
    }

    [Fact]
    public void ChangingARowsColumns_LeavesTheOtherRowsAsTheyAre()
    {
        var rows = Rows();
        var first = (IDictionary<string, object?>)rows[0];
        var second = (IDictionary<string, object?>)rows[1];

        first.Add("added", 1);
        first.Remove("id");

        Assert.Equal(ExpandoRows()[1].Select(c => c.Key), second.Keys);
        Assert.Equal(2L, second["id"]);
    }

    [Fact]
    public void AChange_IsNotified()
    {
        var row = (DbQueryRow)Rows()[0];
        var changed = new List<string?>();
        ((INotifyPropertyChanged)row).PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        row["name"] = "Anna";
        row.Add("added", 1);
        row.Remove("id");

        Assert.Equal(new[] { "name", "added", "id" }, changed);
    }

    public static TheoryData<string> OptionSets => new() { "default", "web", "relaxed" };

    private static JsonSerializerOptions Options(string set) => set switch
    {
        "default" => new JsonSerializerOptions(),
        "web" => new JsonSerializerOptions(JsonSerializerDefaults.Web),
        _ => new JsonSerializerOptions(JsonSerializerDefaults.Web) { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping },
    };

    // Values of every type a driver returns, in a row and in the ExpandoObject with the same columns.
    private static (DbQueryRow Row, ExpandoObject Expando) EveryType()
    {
        var columns = new List<(string, object?)>
        {
            ("null", null),
            ("text", "é \"quoted\" <b> & ' \\ / 😀 " + (char)0x2028 + " \t " + (char)1),
            ("int", int.MinValue),
            ("long", long.MaxValue),
            ("short", (short)-7),
            ("byte", (byte)255),
            ("decimal", 1.50m),
            ("small decimal", -0.0001m),
            ("largest decimal", decimal.MaxValue),
            ("double", 2.5),
            ("fraction", 0.1),
            ("negative zero", -0.0),
            ("large double", 1e300),
            ("smallest double", double.Epsilon),
            ("float", 0.1f),
            ("true", true),
            ("false", false),
            ("DateTime", new DateTime(2026, 1, 15, 10, 20, 30, 123)),
            ("DateTime UTC", new DateTime(2026, 1, 15, 10, 20, 30, DateTimeKind.Utc)),
            ("DateTime ticks", new DateTime(2026, 1, 15, 10, 20, 30).AddTicks(1234567)),
            ("midnight", new DateTime(2026, 1, 15)),
            ("DateTimeOffset", new DateTimeOffset(2026, 1, 15, 10, 20, 30, TimeSpan.FromHours(4))),
            ("TimeSpan", new TimeSpan(12, 30, 5)),
            ("Guid", Guid.Parse("6F9619FF-8B86-D011-B42D-00C04FC964FF")),
            ("bytes", new byte[] { 1, 2, 255 }),
            ("no bytes", new byte[0]),
            ("char", 'x'),
            ("nested", (object)ToExpando([("a", 1.50m), ("é", "<c>")])),
            ("list", new List<object?> { 1m, "a", null, true }),
            ("JsonElement", JsonDocument.Parse("{\"x\":[1,2.50,\"y\"]}").RootElement),
            ("é", 1),
            ("a\"b", 2),
            ("<tag>", 3),
            ("😀", 4),
            ("", 5),
        };
        var row = new DbQueryRow();
        foreach (var (name, value) in columns)
            row.Add(name, value);
        return (row, ToExpando(columns));
    }

    private static ExpandoObject ToExpando(IEnumerable<(string Name, object? Value)> columns)
    {
        var expando = new ExpandoObject();
        foreach (var (name, value) in columns)
            ((IDictionary<string, object?>)expando).Add(name, value);
        return expando;
    }

    [Theory]
    [MemberData(nameof(OptionSets))]
    public void ARow_IsWrittenAsItsExpandoObjectWas(string set)
    {
        var options = Options(set);
        var (row, expando) = EveryType();

        Assert.Equal(JsonSerializer.Serialize(expando, options), JsonSerializer.Serialize(row, options));
        Assert.Equal(JsonSerializer.Serialize(new object[] { expando, expando }, options), JsonSerializer.Serialize(new object[] { row, row }, options));

        // A query's rows, read from the database
        Assert.Equal(JsonSerializer.Serialize(ExpandoRows(), options), JsonSerializer.Serialize(Rows(), options));
    }

    [Fact]
    public void ARow_IsEscapedByItsWritersEncoder()
    {
        var (row, expando) = EveryType();
        var options = Options("default");

        // The same row through writers of two encoders, in turn: each writes it as it writes the ExpandoObject.
        foreach (var encoder in new[] { JavaScriptEncoder.UnsafeRelaxedJsonEscaping, JavaScriptEncoder.Default, JavaScriptEncoder.UnsafeRelaxedJsonEscaping })
            Assert.Equal(Write(expando, encoder, options), Write(row, encoder, options));

        static string Write(object value, JavaScriptEncoder encoder, JsonSerializerOptions options)
        {
            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Encoder = encoder }))
                JsonSerializer.Serialize(writer, value, options);
            return System.Text.Encoding.UTF8.GetString(stream.ToArray());
        }
    }

    [Fact]
    public void NaN_IsRefusedAsForAnExpandoObject()
    {
        var row = new DbQueryRow { ["x"] = double.NaN };
        var expando = ToExpando([("x", double.NaN)]);

        var refused = Record.Exception(() => JsonSerializer.Serialize(expando));
        Assert.NotNull(refused);
        Assert.IsType(refused.GetType(), Record.Exception(() => JsonSerializer.Serialize(row)));

        var allowing = new JsonSerializerOptions { NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals };
        Assert.Equal(JsonSerializer.Serialize(expando, allowing), JsonSerializer.Serialize(row, allowing));
    }

    [Fact]
    public void ARow_IsReadFromJsonAsAnExpandoObjectIs()
    {
        const string json = "{\"a\":1,\"b\":[true,null],\"c\":{\"d\":\"e\"},\"f\":null}";

        var row = JsonSerializer.Deserialize<DbQueryRow>(json)!;
        var expando = JsonSerializer.Deserialize<ExpandoObject>(json)!;

        Assert.Equal(((IDictionary<string, object?>)expando).Keys, row.Keys);
        Assert.All(row, column => Assert.IsType<JsonElement>(column.Value));
        Assert.Equal(JsonSerializer.Serialize(expando), JsonSerializer.Serialize(row));
    }
}
