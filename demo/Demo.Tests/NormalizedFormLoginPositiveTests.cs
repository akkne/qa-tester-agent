using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Demo.Web;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Demo.Tests;

public sealed class NormalizedFormLoginPositiveTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Normalized_registration_supports_form_login_in_a_new_session(bool registerWithForm)
    {
        using var factory = new DemoWebFactory();
        var options = new WebApplicationFactoryClientOptions { AllowAutoRedirect = false };
        using var registrationClient = factory.CreateClient(options);
        const string password = "Synthetic-login-42!";
        const string email = "alice@example.com";
        using var registration = registerWithForm
            ? await registrationClient.PostAsync("/register", await FormAsync(registrationClient, "/register", "  Alice@Example.COM  ", password))
            : await registrationClient.PostAsJsonAsync("/api/register", new RegistrationInput("  Alice@Example.COM  ", password));
        Assert.Equal(registerWithForm ? HttpStatusCode.Redirect : HttpStatusCode.Created, registration.StatusCode);
        Assert.Equal(1, factory.Accounts.Count);
        var stored = factory.Accounts.Get(email);
        Assert.NotNull(stored);
        Assert.Equal(email, stored.Email);

        using var session = factory.CreateClient(options);
        using var login = await session.PostAsync("/login", await FormAsync(session, "/login", "  ALICE@EXAMPLE.COM  ", password));
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        using var me = await session.GetAsync("/api/me");
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        var view = await me.Content.ReadFromJsonAsync<AccountView>();
        Assert.NotNull(view);
        Assert.Equal(stored.Id, view.Id);
        Assert.Equal(email, view.Email);
        using var cabinet = await session.GetAsync("/cabinet");
        Assert.Equal(HttpStatusCode.OK, cabinet.StatusCode);
        Assert.Contains(email, await cabinet.Content.ReadAsStringAsync());
    }

    private static async Task<FormUrlEncodedContent> FormAsync(HttpClient client, string route, string email, string password)
    {
        string html = await client.GetStringAsync(route);
        Match token = Regex.Match(html, "name=\"__RequestVerificationToken\" value=\"([^\"]+)\"");
        Assert.True(token.Success, "Form must supply an antiforgery token.");
        return new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = WebUtility.HtmlDecode(token.Groups[1].Value),
            ["email"] = email,
            ["password"] = password
        });
    }
}
