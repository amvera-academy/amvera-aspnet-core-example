using System.Text.Json;

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory,
    WebRootPath = Path.Combine(AppContext.BaseDirectory, "wwwroot")
});
var app = builder.Build();
var dataDir = Environment.GetEnvironmentVariable("DATA_DIR") ?? (Environment.GetEnvironmentVariable("AMVERA") is not null ? "/data" : Path.Combine(app.Environment.ContentRootPath, "data"));
var dataFile = Path.Combine(dataDir, "items.json");
var gate = new SemaphoreSlim(1, 1);

async Task<List<Item>> ReadItems()
{
    Directory.CreateDirectory(dataDir);
    if (!File.Exists(dataFile)) await File.WriteAllTextAsync(dataFile, "[]");
    return JsonSerializer.Deserialize<List<Item>>(await File.ReadAllTextAsync(dataFile)) ?? [];
}

async Task WriteItems(List<Item> items)
{
    await File.WriteAllTextAsync(dataFile, JsonSerializer.Serialize(items, new JsonSerializerOptions { WriteIndented = true }));
}

app.UseDefaultFiles();
app.UseStaticFiles();
app.MapGet("/api/health", () => Results.Ok(new { ok = true, framework = "ASP.NET Core", storage = dataFile }));
app.MapGet("/api/items", async () =>
{
    var items = await ReadItems();
    items.Reverse();
    return Results.Ok(new { items, count = items.Count });
});
app.MapPost("/api/items", async (CreateItem data) =>
{
    var name = data.Name?.Trim() ?? "";
    if (name.Length is < 1 or > 120) return Results.BadRequest(new { error = "Name must contain from 1 to 120 characters" });
    await gate.WaitAsync();
    try
    {
        var items = await ReadItems();
        var item = new Item(items.Count == 0 ? 1 : items.Max(value => value.Id) + 1, name);
        items.Add(item);
        await WriteItems(items);
        return Results.Created($"/api/items/{item.Id}", new { item });
    }
    finally { gate.Release(); }
});
app.MapDelete("/api/items/{id:int}", async (int id) =>
{
    await gate.WaitAsync();
    try
    {
        var items = await ReadItems();
        if (items.RemoveAll(item => item.Id == id) == 0) return Results.NotFound(new { error = "Item not found" });
        await WriteItems(items);
        return Results.Ok(new { deleted = true, id });
    }
    finally { gate.Release(); }
});
app.Run();

record Item(int Id, string Name);
record CreateItem(string? Name);
