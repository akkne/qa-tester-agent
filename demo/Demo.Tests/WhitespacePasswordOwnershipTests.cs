using System.Net;
using System.Net.Http.Json;
using Demo.Web;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Demo.Tests;

public sealed class WhitespacePasswordOwnershipTests
{
    [Theory]
    [InlineData(12)]
    [InlineData(64)]
    [InlineData(128)]
    public async Task Accepted_whitespace_padded_credentials_do_not_bypass_account_ownership(int length)
    {
        using DemoWebFactory factory = new();
        using HttpClient first = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        using HttpClient second = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        string credential = "Z" + new string(' ', length - 1);
        const string firstEmail = "first-owner@example.com";
        const string secondEmail = "second-owner@example.com";
        using HttpResponseMessage firstRegistration = await first.PostAsJsonAsync("/api/register", new RegistrationInput(firstEmail, credential));
        using HttpResponseMessage secondRegistration = await second.PostAsJsonAsync("/api/register", new RegistrationInput(secondEmail, credential));
        Assert.Equal(HttpStatusCode.Created, firstRegistration.StatusCode);
        Assert.Equal(HttpStatusCode.Created, secondRegistration.StatusCode);
        Account? firstAccount = factory.Accounts.Get(firstEmail);
        Account? secondAccount = factory.Accounts.Get(secondEmail);
        Assert.NotNull(firstAccount);
        Assert.NotNull(secondAccount);
        Assert.NotEqual(firstAccount!.Id, secondAccount!.Id);
        using HttpResponseMessage firstLogin = await first.PostAsJsonAsync("/api/login", new LoginInput(firstEmail, credential));
        using HttpResponseMessage secondLogin = await second.PostAsJsonAsync("/api/login", new LoginInput(secondEmail, credential));
        Assert.Equal(HttpStatusCode.OK, firstLogin.StatusCode);
        Assert.Equal(HttpStatusCode.OK, secondLogin.StatusCode);

        foreach (var pair in new[] { (Client: first, Own: firstAccount, Other: secondAccount), (Client: second, Own: secondAccount, Other: firstAccount) })
        {
            using HttpResponseMessage me = await pair.Client.GetAsync("/api/me");
            Assert.Equal(HttpStatusCode.OK, me.StatusCode);
            AccountView? identity = await me.Content.ReadFromJsonAsync<AccountView>();
            Assert.NotNull(identity);
            Assert.Equal(pair.Own.Id, identity!.Id);
            using HttpResponseMessage ownPage = await pair.Client.GetAsync($"/cabinet/{pair.Own.Id}");
            Assert.Equal(HttpStatusCode.OK, ownPage.StatusCode);
            Assert.Contains(pair.Own.Email, await ownPage.Content.ReadAsStringAsync());
            foreach (string route in new[] { $"/cabinet/{pair.Other.Id}", $"/api/accounts/{pair.Other.Id}" })
            {
                using HttpResponseMessage denied = await pair.Client.GetAsync(route);
                Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
                string body = await denied.Content.ReadAsStringAsync();
                Assert.DoesNotContain(pair.Other.Email, body);
                Assert.DoesNotContain(pair.Other.PasswordHash, body);
            }
        }
        Assert.Equal(2, factory.Accounts.Count);
    }
}
