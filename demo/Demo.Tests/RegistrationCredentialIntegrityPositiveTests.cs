using System.Net;
using System.Net.Http.Json;
using Demo.Web;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Demo.Tests;

public sealed class RegistrationCredentialIntegrityPositiveTests
{
    [Theory]
    [InlineData(12)]
    [InlineData(128)]
    public async Task Normalized_email_and_exact_boundary_password_authenticate_without_exposing_credentials(int length)
    {
        using DemoWebFactory factory = new();
        using HttpClient client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        string password = " " + new string('Z', length - 2) + " ";
        using HttpResponseMessage registration = await client.PostAsJsonAsync("/api/register", new RegistrationInput("  Mixed@Example.COM  ", password));
        Assert.Equal(HttpStatusCode.Created, registration.StatusCode);
        Assert.Equal(1, factory.Accounts.Count);
        Account account = factory.Accounts.Get("mixed@example.com")!;
        Assert.NotNull(account);
        Assert.Equal("mixed@example.com", account.Email);
        Assert.NotEqual(password, account.PasswordHash);
        PasswordHasher<Account> hasher = new();
        Assert.NotEqual(PasswordVerificationResult.Failed, hasher.VerifyHashedPassword(account, account.PasswordHash, password));
        Assert.Equal(PasswordVerificationResult.Failed, hasher.VerifyHashedPassword(account, account.PasswordHash, password.Trim()));

        using HttpResponseMessage login = await client.PostAsJsonAsync("/api/login", new LoginInput(" MIXED@EXAMPLE.COM ", password));
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        using HttpResponseMessage me = await client.GetAsync("/api/me");
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        AccountView? identity = await me.Content.ReadFromJsonAsync<AccountView>();
        Assert.NotNull(identity);
        Assert.Equal(account.Id, identity.Id);
        Assert.Equal(account.Email, identity.Email);
        using HttpResponseMessage own = await client.GetAsync($"/api/accounts/{account.Id}");
        Assert.Equal(HttpStatusCode.OK, own.StatusCode);
        string cabinet = await client.GetStringAsync("/cabinet");
        Assert.Contains(account.Email, cabinet);
        foreach (string body in new[] { await registration.Content.ReadAsStringAsync(), await login.Content.ReadAsStringAsync(), await me.Content.ReadAsStringAsync(), await own.Content.ReadAsStringAsync(), cabinet })
        {
            Assert.DoesNotContain(password, body);
            Assert.DoesNotContain(account.PasswordHash, body);
        }
    }
}
