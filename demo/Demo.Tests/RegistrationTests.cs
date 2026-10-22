using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Demo.Web;

namespace Demo.Tests;

public sealed class RegistrationTests
{
    private const string Password = "secure-password-123";

    [Theory]
    [InlineData("", Password, "email")]
    [InlineData("invalid", Password, "email")]
    [InlineData("valid@example.com", "short", "12")]
    public async Task Registration_rejects_invalid_input(string email, string password, string expectedError)
    {
        using DemoWebFactory factory = new();
        using HttpClient client = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        HttpResponseMessage response = await client.PostAsJsonAsync("/api/register", new RegistrationInput(email, password));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(expectedError, await response.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, factory.Accounts.Count);
    }

    [Fact]
    public async Task Registration_normalizes_email_and_rejects_duplicate_case_insensitively()
    {
        using DemoWebFactory factory = new();
        using HttpClient client = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        HttpResponseMessage first = await client.PostAsJsonAsync("/api/register", new RegistrationInput("  Alice@Example.COM  ", Password));
        HttpResponseMessage duplicate = await client.PostAsJsonAsync("/api/register", new RegistrationInput("alice@example.com", Password));

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Contains("уже зарегистрирован", await duplicate.Content.ReadAsStringAsync());
        Assert.Equal(1, factory.Accounts.Count);
        Assert.Equal("alice@example.com", factory.Accounts.Get("alice@example.com")?.Email);
    }

    [Fact]
    public async Task Password_is_hashed_and_never_returned_in_api_or_page()
    {
        using DemoWebFactory factory = new();
        using HttpClient client = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        string registration = await (await client.PostAsJsonAsync("/api/register", new RegistrationInput("alice@example.com", Password))).Content.ReadAsStringAsync();
        Account account = factory.Accounts.Get("alice@example.com")!;
        Assert.NotEqual(Password, account.PasswordHash);
        Assert.DoesNotContain(Password, account.PasswordHash);
        Assert.StartsWith("AQAAAA", account.PasswordHash);

        string login = await (await client.PostAsJsonAsync("/api/login", new LoginInput("alice@example.com", Password))).Content.ReadAsStringAsync();
        string cabinet = await client.GetStringAsync("/cabinet");
        foreach (string response in new[] { registration, login, cabinet })
        {
            Assert.DoesNotContain(Password, response);
            Assert.DoesNotContain(account.PasswordHash, response);
        }
    }

    [Fact]
    public async Task Login_and_cabinet_require_valid_credentials_and_ownership()
    {
        using DemoWebFactory factory = new();
        using HttpClient anonymous = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await anonymous.GetAsync("/cabinet")).StatusCode);

        using HttpClient alice = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        using HttpClient bob = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        Guid aliceId = await RegisterAsync(alice, "alice@example.com");
        Guid bobId = await RegisterAsync(bob, "bob@example.com");

        Assert.Equal(HttpStatusCode.Unauthorized, (await alice.PostAsJsonAsync("/api/login", new LoginInput("alice@example.com", "wrong-password"))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await alice.GetAsync("/api/me")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await alice.PostAsJsonAsync("/api/login", new LoginInput(" ALICE@EXAMPLE.COM ", Password))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await bob.PostAsJsonAsync("/api/login", new LoginInput("bob@example.com", Password))).StatusCode);

        Assert.Contains("alice@example.com", await alice.GetStringAsync("/cabinet"));
        Assert.Equal(HttpStatusCode.OK, (await alice.GetAsync($"/cabinet/{aliceId}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await alice.GetAsync($"/cabinet/{bobId}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await alice.GetAsync($"/api/accounts/{bobId}")).StatusCode);
        Assert.Contains("bob@example.com", await bob.GetStringAsync("/cabinet"));
        Assert.DoesNotContain("bob@example.com", await alice.GetStringAsync("/cabinet"));
    }

    [Fact]
    public async Task Forms_have_labels_test_ids_clear_errors_and_work_with_antiforgery()
    {
        using DemoWebFactory factory = new();
        using HttpClient client = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        string registerPage = await client.GetStringAsync("/register");
        Assert.Contains("<label for=\"email\">", registerPage);
        Assert.Contains("<label for=\"password\">", registerPage);
        Assert.Contains("data-testid=\"register-form\"", registerPage);
        Assert.Contains("data-testid=\"register-submit\"", registerPage);
        Assert.Contains("name=\"viewport\"", registerPage);

        string registerToken = Token(registerPage);
        HttpResponseMessage invalid = await client.PostAsync("/register", Form(registerToken, "bad", Password));
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Contains("data-testid=\"form-error\"", await invalid.Content.ReadAsStringAsync());

        HttpResponseMessage registered = await client.PostAsync("/register", Form(registerToken, "a@example.com", Password));
        Assert.Equal(HttpStatusCode.Redirect, registered.StatusCode);
        Assert.Equal("/login?registered=1", registered.Headers.Location?.ToString());

        string loginPage = await client.GetStringAsync("/login");
        Assert.Contains("data-testid=\"login-form\"", loginPage);
        Assert.Contains("data-testid=\"login-submit\"", loginPage);
        string loginToken = Token(loginPage);

        HttpResponseMessage denied = await client.PostAsync("/login", Form(loginToken, "a@example.com", "wrong"));
        Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
        Assert.Contains("Неверный email или пароль", await denied.Content.ReadAsStringAsync());

        HttpResponseMessage loggedIn = await client.PostAsync("/login", Form(loginToken, "a@example.com", Password));
        Assert.Equal(HttpStatusCode.Redirect, loggedIn.StatusCode);
        string cabinet = await client.GetStringAsync("/cabinet");
        Assert.Contains("data-testid=\"account-email\"", cabinet);
        Assert.Contains("a@example.com", cabinet);

        HttpResponseMessage loggedOut = await client.PostAsync("/logout",
            new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = Token(cabinet) }));
        Assert.Equal(HttpStatusCode.Redirect, loggedOut.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/me")).StatusCode);
    }

    private static async Task<Guid> RegisterAsync(HttpClient client, string email)
    {
        HttpResponseMessage response = await client.PostAsJsonAsync("/api/register", new RegistrationInput(email, Password));
        response.EnsureSuccessStatusCode();
        using JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.GetProperty("id").GetGuid();
    }

    private static FormUrlEncodedContent Form(string token, string email, string password) =>
        new(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["email"] = email,
            ["password"] = password
        });

    private static string Token(string html)
    {
        Match match = Regex.Match(html, "name=\"__RequestVerificationToken\" value=\"([^\"]+)\"");
        Assert.True(match.Success);
        return WebUtility.HtmlDecode(match.Groups[1].Value);
    }
}
