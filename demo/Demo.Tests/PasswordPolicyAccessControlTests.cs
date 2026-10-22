using System.Net;
using System.Net.Http.Json;
using Demo.Web;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Demo.Tests;

public sealed class PasswordPolicyAccessControlTests
{
    [Theory]
    [InlineData(11, false)]
    [InlineData(129, false)]
    [InlineData(12, true)]
    [InlineData(128, true)]
    public async Task Rejected_credentials_cannot_authenticate_or_access_existing_account(int length, bool whitespaceOnly)
    {
        using DemoWebFactory factory = new();
        using HttpClient owner = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        string ownerPassword = new string('Q', 128);
        using HttpResponseMessage created = await owner.PostAsJsonAsync("/api/register", new RegistrationInput("owner@example.com", ownerPassword));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        AccountView? account = await created.Content.ReadFromJsonAsync<AccountView>();
        Assert.NotNull(account);
        using HttpResponseMessage ownerLogin = await owner.PostAsJsonAsync("/api/login", new LoginInput(account.Email, ownerPassword));
        Assert.Equal(HttpStatusCode.OK, ownerLogin.StatusCode);
        Assert.Contains(account.Email, await owner.GetStringAsync($"/cabinet/{account.Id}"));

        using HttpClient candidate = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        const string email = "candidate@example.com";
        string password = new string(whitespaceOnly ? ' ' : 'X', length);
        using HttpResponseMessage rejected = await candidate.PostAsJsonAsync("/api/register", new RegistrationInput(email, password));
        using HttpResponseMessage login = await candidate.PostAsJsonAsync("/api/login", new LoginInput(email, password));
        Assert.Equal(HttpStatusCode.Unauthorized, login.StatusCode);
        using HttpResponseMessage me = await candidate.GetAsync("/api/me");
        Assert.Equal(HttpStatusCode.Unauthorized, me.StatusCode);
        foreach (string route in new[] { "/cabinet", $"/cabinet/{account.Id}" })
        {
            using HttpResponseMessage page = await candidate.GetAsync(route);
            Assert.Equal(HttpStatusCode.Redirect, page.StatusCode);
            Assert.DoesNotContain(account.Email, await page.Content.ReadAsStringAsync());
        }
        using HttpResponseMessage protectedAccount = await candidate.GetAsync($"/api/accounts/{account.Id}");
        Assert.Equal(HttpStatusCode.Unauthorized, protectedAccount.StatusCode);
        Assert.DoesNotContain(account.Email, await protectedAccount.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        Assert.Null(factory.Accounts.Get(email));
        Assert.Equal(1, factory.Accounts.Count);
    }
}
