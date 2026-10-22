using System.Net;
using System.Net.Http.Json;
using Demo.Web;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Demo.Tests;

public sealed class RegistrationWhitespaceAccessGeneratedTests
{
    [Theory]
    [InlineData('\t', 12)]
    [InlineData('\u00a0', 128)]
    [InlineData('\u2003', 12)]
    public async Task Rejected_registration_leaves_client_anonymous_before_and_after_login(char whitespace, int length)
    {
        using DemoWebFactory factory = new();
        using HttpClient owner = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        string ownerCredential = new string('O', 24);
        const string ownerEmail = "protected-owner@example.com";
        using HttpResponseMessage created = await owner.PostAsJsonAsync("/api/register", new RegistrationInput(ownerEmail, ownerCredential));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        AccountView? account = await created.Content.ReadFromJsonAsync<AccountView>();
        Assert.NotNull(account);

        using HttpClient candidate = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        const string candidateEmail = "denied-whitespace@example.com";
        string rejectedCredential = new string(whitespace, length);
        using HttpResponseMessage rejected = await candidate.PostAsJsonAsync("/api/register", new RegistrationInput(candidateEmail, rejectedCredential));
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        await AssertDenied(candidate, account);
        using HttpResponseMessage login = await candidate.PostAsJsonAsync("/api/login", new LoginInput(candidateEmail, rejectedCredential));
        Assert.Equal(HttpStatusCode.Unauthorized, login.StatusCode);
        await AssertDenied(candidate, account);
        Assert.Null(factory.Accounts.Get(candidateEmail));
        Assert.Equal(1, factory.Accounts.Count);
    }

    private static async Task AssertDenied(HttpClient client, AccountView owner)
    {
        foreach (string route in new[] { "/api/me", $"/api/accounts/{owner.Id}" })
        {
            using HttpResponseMessage response = await client.GetAsync(route);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.DoesNotContain(owner.Email, await response.Content.ReadAsStringAsync());
        }
        foreach (string route in new[] { "/cabinet", $"/cabinet/{owner.Id}" })
        {
            using HttpResponseMessage response = await client.GetAsync(route);
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            Assert.NotNull(response.Headers.Location);
            Assert.Contains("/login", response.Headers.Location.ToString());
            Assert.DoesNotContain(owner.Email, await response.Content.ReadAsStringAsync());
        }
    }
}
