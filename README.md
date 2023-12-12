# DotNetCore.GaussDB - the .NET data provider for PostgreSQL

## Quickstart

Here's a basic code snippet to get you started:

```csharp
using GaussDB;

var connString = "host={host};port={port};username={username};Password={password};database={database}";

var dataSourceBuilder = new GaussDBDataSourceBuilder(connString);
var dataSource = dataSourceBuilder.Build();

var conn = await dataSource.OpenConnectionAsync();

// Insert some data
await using (var cmd = new GaussDBCommand("INSERT INTO data (some_field) VALUES (@p)", conn))
{
    cmd.Parameters.AddWithValue("p", "Hello world");
    await cmd.ExecuteNonQueryAsync();
}

// Retrieve all rows
await using (var cmd = new GaussDBCommand("SELECT some_field FROM data", conn))
await using (var reader = await cmd.ExecuteReaderAsync())
{
    while (await reader.ReadAsync())
        Console.WriteLine(reader.GetString(0));
}
```