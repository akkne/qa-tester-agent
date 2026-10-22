using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Demo.Web;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Demo.Tests;

public sealed class RegistrationWhitespaceFormPositiveTests
{
    [Theory]
    [InlineData(12)]
    [InlineData(128)]
    public async Task Form_accepts_password_with_one_nonwhitespace_character(int length)
    {
        using DemoWebFactory factory = new();
        using HttpClient client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        const string email = "one-character@example.test";
        string password = new string(' ', length - 1) + "Z";
        string html = await client.GetStringAsync("/register");
        Match token = Regex.Match(html, "name=\"__RequestVerificationToken\" value=\"([^\"]+)\"");
        Assert.True(token.Success, "Registration must provide an antiforgery token.");
        using FormUrlEncodedContent form = new(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = WebUtility.HtmlDecode(token.Groups[1].Value),
            ["email"] = email,
            ["password"] = password
        });
        using HttpResponseMessage registration = await client.PostAsync("/register", form);
        Assert.Equal(HttpStatusCode.Redirect, registration.StatusCode);
        Assert.Equal("/login?registered=1", registration.Headers.Location?.ToString());
        Assert.Equal(1, factory.Accounts.Count);
        Account? stored = factory.Accounts.Get(email);
        Assert.NotNull(stored);

        using HttpClient session = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        using HttpResponseMessage login = await session.PostAsJsonAsync("/api/login", new LoginInput(email, password));
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        using HttpResponseMessage me = await session.GetAsync("/api/me");
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        AccountView? identity = await me.Content.ReadFromJsonAsync<AccountView>();
        Assert.NotNull(identity);
        Assert.Equal(stored.Id, identity.Id);
        Assert.Equal(email, identity.Email);
        Assert.Contains(email, await session.GetStringAsync("/cabinet"));
    }
}
