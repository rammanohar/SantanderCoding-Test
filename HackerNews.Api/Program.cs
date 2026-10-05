using HackerNews.Service;
using IHackerNews;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

// Register in-memory caching
builder.Services.AddMemoryCache();

builder.Services.AddHttpClient<INewsService, NewsService>(client =>
{
    var baseUrl = builder.Configuration["HackerNewsApi:BaseUrl"];

    if (string.IsNullOrEmpty(baseUrl))
    {
        throw new InvalidOperationException("Hacker News BaseUrl is missing from configuration.");
    }

    client.BaseAddress = new Uri(baseUrl);
    client.DefaultRequestHeaders.Add("User-Agent", "HN-Best-Stories-API");
});


var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();
