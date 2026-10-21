WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
WebApplication app = builder.Build();

app.MapGet("/healthz", () => Results.Ok(new { status = "ready", phase = "M0" }));
app.Run();

public partial class Program;
