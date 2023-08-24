# Com.H.Data.Common
Adds ExecuteQuery and ExecuteQueryAsync extension methods to DbConntion and DbCommand that return dynamic data result `IEnumerable<dynamic>` or `IAsyncEnumerable<dynamic>`.
For source code and documentation, kindly visit the project's github page [https://github.com/H7O/Com.H.Data.Common](https://github.com/H7O/Com.H.Data.Common)


## Sample 1
This sample demonstrates how to execute a simple query without parameters on a SQL Server Database.

To run this sample, you need to:
1) Create a new console application
2) Add NuGet package [Com.H.Data.Common](https://www.nuget.org/packages/Com.H.Data.Common)  
3) Add NuGet package [Microsoft.Data.SqlClient](https://www.nuget.org/packages/Microsoft.Data.SqlClient)
4) Copy and paste the following code into your Program.cs file:

```csharp
using Com.H.Data.Common;
using System.Data.Common;
using Microsoft.Data.SqlClient;

string conStr = @"connection string goes here";
DbConnection dc = new SqlConnection(conStr);
// ^ note the use of DbConnection instead of SqlConnection. The extension methods 
// are defined on DbConnection

var result = dc.ExecuteQuery("select 'John' as name, '123' as phone");
// ^ returns IEnumerable<dynamic>, you can also return IEnumerable<T> where T is your data model class
// by using the ExecuteQuery<T> method which returns IEnumerable<T>
// example: var result = dc.ExecuteQuery<YourDataModelClass>("select 'John' as name, '123' as phone");
// Also, returns IAsyncEnumerable when called asynchronously via dc.ExecuteQueryAsync() 
// or dc.ExecuteQueryAsync<T>()

foreach (var item in result)
{
    System.Console.WriteLine($"name = {item.name}, phone = {item.phone}");
}
```

## Sample 2
This sample demonstrates how to pass parameters to your SQL query

```csharp
using Com.H.Data.Common;
using System.Data.Common;
using Microsoft.Data.SqlClient;

string conStr = @"your connection string goes here";
DbConnection dc = new SqlConnection(conStr);
// ^ note the use of DbConnection instead of SqlConnection. The extension methods 
// are defined on DbConnection

var queryParams = new { name = "Jane" };
// ^ queryParams could be an anonymous object (similar to the example above)
// IDictionary<string, object>
// JSON string
// System.Text.Json.JsonElement (useful when building Web APIs, allows passing 
// JsonElement input directly from a web client)
// Example 1: var queryParams = new Dictionary<string, object> { { "name", "Jane" } }
// Example 2: var queryParams = new MyCustomParamClass { name = "John" }
// Example 3: var queryParams = "{\"name\":\"Jane\"}"
// Example 4: var queryParams = System.Text.Json.JsonDocument.Parse("{\"name\":\"John\"}").RootElement


var result = dc.ExecuteQuery(@"
	select * from (values 
		('John', '55555'), 
		('Jane', '44444')) as t (name, phone)
	where name = {{name}}", queryParams
);
// ^ note the use of curly braces around the parameter name in the query. 
// This is a special syntax that allows you to pass parameters to your query.
// The parameter name must match the property name in the queryParams object.
// It also protects you from SQL injection attacks and is configurable to use other 
// delimiters by passing a regular expression 

 
// Example 1: using `[[` and `]]` instead of `{{` and `}}` dc.ExecuteQuery(@"
//	select * from (values ('John', '55555'), ('Jane', '44444')) as t (name, phone)
//	where name = [[name]]", 
//  queryParams, @"(?<open_marker>\[\[)(?<param>.*?)?(?<close_marker>\]\])" );

// Example 2: using `|` instead of `{{` and `}}` dc.ExecuteQuery(@"
//	select * from (values ('John', '55555'), ('Jane', '44444')) as t (name, phone)
//	where name = |name|", 
//  queryParams, @"(?<open_marker>\|)(?<param>.*?)?(?<close_marker>\|)" );



foreach (var item in result)
{
    System.Console.WriteLine($"name = {item.name}, phone = {item.phone}");
}
```

## What other databases this library supports?
Any ADO.NET provider that implements DbConnection and DbCommand classes should work with this library.

## What other features this library has?
This small library has several other options that allow for more advanced features that might not be of much use to most, hence samples for those features have been left out in this quick `how to` documentation.