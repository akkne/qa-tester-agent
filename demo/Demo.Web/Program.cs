WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
WebApplication app = builder.Build();

app.MapGet("/", () => Results.Content("""
    <!doctype html>
    <html lang="ru">
    <head><meta charset="utf-8"><title>Демо регистрации</title></head>
    <body>
      <main>
        <h1 data-testid="page-title">Демо регистрации</h1>
        <p>Регистрация и личный кабинет будут добавлены отдельным изменением по ТЗ.</p>
      </main>
    </body>
    </html>
    """, "text/html; charset=utf-8"));
app.MapGet("/healthz", () => Results.Ok(new { status = "ready", phase = "M1" }));
app.Run();

public partial class Program;
