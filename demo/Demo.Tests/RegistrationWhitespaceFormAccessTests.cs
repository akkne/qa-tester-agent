using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Demo.Web;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Demo.Tests;

public sealed class RegistrationWhitespaceFormAccessTests
{
    [Theory]
    [InlineData(12)]
    [InlineData(128)]
    public async Task Rejected_form_credentials_cannot_login_or_read_owner_data(int length)
    {
        using DemoWebFactory factory = new();
        using HttpClient owner = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        const string ownerEmail = "protected-owner@example.test";
        using HttpResponseMessage created = await owner.PostAsJsonAsync("/api/register", new RegistrationInput(ownerEmail, "OwnerPassword-123!"));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        AccountView? account = await created.Content.ReadFromJsonAsync<AccountView>();
        Assert.NotNull(account);

        using HttpClient candidate = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        const string email = "rejected-form@example.test";
        string password = new string(' ', length);
        string html = await candidate.GetStringAsync("/register");
        Match token = Regex.Match(html, "name=\"__RequestVerificationToken\" value=\"([^\"]+)\"");
        Assert.True(token.Success, "Registration must provide an antiforgery token.");
        using FormUrlEncodedContent form = new(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = WebUtility.HtmlDecode(token.Groups[1].Value),
            ["email"] = email,
            ["password"] = password
        });
        using HttpResponseMessage registration = await candidate.PostAsync("/register", form);
        using HttpResponseMessage login = await candidate.PostAsJsonAsync("/api/login", new LoginInput(email, password));
        Assert.Equal(HttpStatusCode.Unauthorized, login.StatusCode);
        foreach (string route in new[] { "/api/me", $"/api/accounts/{account.Id}" })
        {
            using HttpResponseMessage response = await candidate.GetAsync(route);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.DoesNotContain(ownerEmail, await response.Content.ReadAsStringAsync());
        }
        foreach (string route in new[] { "/cabinet", $"/cabinet/{account.Id}" })
        {
            using HttpResponseMessage response = await candidate.GetAsync(route);
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            Assert.NotNull(response.Headers.Location);
            Uri destination = new(candidate.BaseAddress!, response.Headers.Location!);
            Assert.Equal("/login", destination.AbsolutePath);
            Assert.DoesNotContain(ownerEmail, response.Headers.Location!.ToString());
            Assert.DoesNotContain(ownerEmail, await response.Content.ReadAsStringAsync());
        }
        Assert.Equal(HttpStatusCode.BadRequest, registration.StatusCode);
        Assert.Null(factory.Accounts.Get(email));
        Assert.Equal(1, factory.Accounts.Count);
    }
}
