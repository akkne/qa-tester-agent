using System.Net;
using Microsoft.EntityFrameworkCore;
using QaAgent.Host.Api;
using QaAgent.Host.Composition;
using QaAgent.Infrastructure.Persistence;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
builder.Services.AddQaAgentCore();
bool pipelineConfigured = !string.IsNullOrWhiteSpace(builder.Configuration.GetConnectionString("Postgres"));
if (pipelineConfigured)
{
    foreach (string name in new[] { "AgentApi:Key", "GitHub:Repository", "GitHub:Branch" })
    {
        if (string.IsNullOrWhiteSpace(builder.Configuration[name]))
        {
            throw new InvalidOperationException($"{name} is required for the M1 pipeline.");
        }
    }

    if (builder.Configuration.GetValue<int>("Agent:WorkerCount") != 1 ||
        builder.Configuration.GetValue<int>("Agent:StageTimeoutMinutes") <= 0)
    {
        throw new InvalidOperationException("M1 requires one worker and a positive stage timeout.");
    }

    string urls = builder.Configuration["urls"] ?? "http://localhost:5000";
    foreach (string address in urls.Split(';', StringSplitOptions.RemoveEmptyEntries))
    {
        if (!Uri.TryCreate(address, UriKind.Absolute, out Uri? uri) ||
            !(uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
              (IPAddress.TryParse(uri.Host, out IPAddress? ip) && IPAddress.IsLoopback(ip))))
        {
            throw new InvalidOperationException("The agent API must listen on a loopback address.");
        }
    }

    builder.Services.AddQaAgentPipeline(builder.Configuration);
}

WebApplication app = builder.Build();
if (pipelineConfigured)
{
    using IServiceScope scope = app.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<RunDbContext>().Database.MigrateAsync();
    app.MapRunEndpoints();
    app.MapGet("/healthz", async (RunDbContext db, CancellationToken cancellationToken) =>
        await db.Database.CanConnectAsync(cancellationToken)
            ? Results.Ok(new { status = "ready", phase = "M4" })
            : Results.StatusCode(StatusCodes.Status503ServiceUnavailable));
}
else
{
    app.MapGet("/healthz", () => Results.Problem("PostgreSQL is not configured.", statusCode: 503));
}
app.Run();

public partial class Program;
