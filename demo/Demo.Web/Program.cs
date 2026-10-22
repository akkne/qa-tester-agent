using System.Security.Claims;
using Demo.Web;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
builder.Services.AddDbContext<DemoDbContext>(options => options.UseNpgsql(
    builder.Configuration.GetConnectionString("Demo")
    ?? "Host=db;Database=demo;Username=demo;Password=demo-local-only"));
builder.Services.AddScoped<IAccountStore, PostgresAccountStore>();
builder.Services.AddScoped<AccountService>();
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "demo.session";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Strict;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.LoginPath = "/login";
        options.Events.OnRedirectToLogin = context =>
        {
            if (context.Request.Path.StartsWithSegments("/api"))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return Task.CompletedTask;
            }

            context.Response.Redirect(context.RedirectUri);
            return Task.CompletedTask;
        };
        options.Events.OnRedirectToAccessDenied = context =>
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        };
    });
builder.Services.AddAuthorization();
builder.Services.AddAntiforgery();

WebApplication app = builder.Build();
if (!app.Environment.IsEnvironment("Testing"))
{
    await using AsyncServiceScope scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<DemoDbContext>().Database.EnsureCreatedAsync();
}

app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/", () => Results.Content(DemoPages.Home(), "text/html; charset=utf-8"));
app.MapGet("/healthz", () => Results.Ok(new
{
    status = "ready",
    sourceSha = Environment.GetEnvironmentVariable("DEMO_SOURCE_SHA") ?? "unverified"
}));

app.MapGet("/register", (HttpContext context, IAntiforgery antiforgery) =>
    Results.Content(DemoPages.Register(antiforgery.GetAndStoreTokens(context).RequestToken!), "text/html; charset=utf-8"));
app.MapPost("/register", async (HttpContext context, IAntiforgery antiforgery, AccountService accounts) =>
{
    if (!await antiforgery.IsRequestValidAsync(context)) return Results.BadRequest();
    IFormCollection form = await context.Request.ReadFormAsync();
    RegistrationResult result = await accounts.RegisterAsync(form["email"].ToString(), form["password"].ToString());
    return result.Error is null
        ? Results.Redirect("/login?registered=1")
        : Results.Content(DemoPages.Register(antiforgery.GetAndStoreTokens(context).RequestToken!, result.Error), "text/html; charset=utf-8", statusCode: result.IsDuplicate ? 409 : 400);
});

app.MapGet("/login", (HttpContext context, IAntiforgery antiforgery) =>
    Results.Content(DemoPages.Login(antiforgery.GetAndStoreTokens(context).RequestToken!, context.Request.Query.ContainsKey("registered")), "text/html; charset=utf-8"));
app.MapPost("/login", async (HttpContext context, IAntiforgery antiforgery, AccountService accounts) =>
{
    if (!await antiforgery.IsRequestValidAsync(context)) return Results.BadRequest();
    IFormCollection form = await context.Request.ReadFormAsync();
    Account? account = await accounts.AuthenticateAsync(form["email"].ToString(), form["password"].ToString());
    if (account is null)
    {
        return Results.Content(DemoPages.Login(antiforgery.GetAndStoreTokens(context).RequestToken!, false, "Неверный email или пароль."), "text/html; charset=utf-8", statusCode: 401);
    }

    await SignInAsync(context, account);
    return Results.Redirect("/cabinet");
});
app.MapPost("/logout", async (HttpContext context, IAntiforgery antiforgery) =>
{
    if (!await antiforgery.IsRequestValidAsync(context)) return Results.BadRequest();
    await context.SignOutAsync();
    return Results.Redirect("/");
}).RequireAuthorization();

app.MapGet("/cabinet", async (HttpContext context, IAccountStore store, IAntiforgery antiforgery) =>
{
    Guid id = Guid.Parse(context.User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    Account? account = await store.FindByIdAsync(id);
    return account is null ? Results.NotFound() : Results.Content(DemoPages.Cabinet(account, antiforgery.GetAndStoreTokens(context).RequestToken!), "text/html; charset=utf-8");
}).RequireAuthorization();
app.MapGet("/cabinet/{id:guid}", async (Guid id, HttpContext context, IAccountStore store, IAntiforgery antiforgery) =>
{
    if (context.User.FindFirstValue(ClaimTypes.NameIdentifier) != id.ToString()) return Results.Forbid();
    Account? account = await store.FindByIdAsync(id);
    return account is null ? Results.NotFound() : Results.Content(DemoPages.Cabinet(account, antiforgery.GetAndStoreTokens(context).RequestToken!), "text/html; charset=utf-8");
}).RequireAuthorization();

app.MapPost("/api/register", async (RegistrationInput input, AccountService accounts) =>
{
    RegistrationResult result = await accounts.RegisterAsync(input.Email, input.Password);
    return result.Error is null
        ? Results.Created($"/api/accounts/{result.Account!.Id}", new AccountView(result.Account.Id, result.Account.Email))
        : result.IsDuplicate ? Results.Conflict(new ErrorView(result.Error)) : Results.BadRequest(new ErrorView(result.Error));
});
app.MapPost("/api/login", async (LoginInput input, HttpContext context, AccountService accounts) =>
{
    Account? account = await accounts.AuthenticateAsync(input.Email, input.Password);
    if (account is null) return Results.Json(new ErrorView("Неверный email или пароль."), statusCode: 401);
    await SignInAsync(context, account);
    return Results.Ok(new AccountView(account.Id, account.Email));
});
app.MapGet("/api/me", async (HttpContext context, IAccountStore store) =>
{
    Guid id = Guid.Parse(context.User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    Account? account = await store.FindByIdAsync(id);
    return account is null ? Results.NotFound() : Results.Ok(new AccountView(account.Id, account.Email));
}).RequireAuthorization();
app.MapGet("/api/accounts/{id:guid}", async (Guid id, HttpContext context, IAccountStore store) =>
{
    if (context.User.FindFirstValue(ClaimTypes.NameIdentifier) != id.ToString()) return Results.Forbid();
    Account? account = await store.FindByIdAsync(id);
    return account is null ? Results.NotFound() : Results.Ok(new AccountView(account.Id, account.Email));
}).RequireAuthorization();

app.Run();

static Task SignInAsync(HttpContext context, Account account) =>
    context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme,
        new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, account.Id.ToString()), new Claim(ClaimTypes.Email, account.Email)],
            CookieAuthenticationDefaults.AuthenticationScheme)));

public partial class Program;
