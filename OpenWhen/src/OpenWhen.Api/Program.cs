var builder = WebApplication.CreateBuilder(args);

var port = Environment.GetEnvironmentVariable("PORT");
if (!string.IsNullOrEmpty(port))
{
    builder.WebHost.UseUrls($"http://0.0.0.0:{port}");
}

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/health", () => Results.Ok());
app.MapGet("/api/hello", () => new HelloResponse("Hello from OpenWhen"));
app.MapFallbackToFile("index.html");

app.Run();

public record HelloResponse(string Message);

public partial class Program;
