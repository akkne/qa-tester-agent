using System.Net;
using System.Net.Http.Json;
using Demo.Web;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Demo.Tests;

public sealed class BidirectionalAccountIsolationAccessControlTests
{
    [Fact]
    public async Task Anonymous_and_other_users_cannot_read_accounts_in_either_direction()
    {
        using DemoWebFactory factory = new();
        using HttpClient alice = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        using HttpClient bob = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        using HttpClient anonymous = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        string alicePassword = new string('A', 24);
        string bobPassword = new string('B', 24);
        AccountView a = await Register(alice, "isolation-a@example.com", alicePassword);
        AccountView b = await Register(bob, "isolation-b@example.com", bobPassword);
        foreach (AccountView target in new[] { a, b })
        {
            using HttpResponseMessage api = await anonymous.GetAsync($"/api/accounts/{target.Id}");
            Assert.Equal(HttpStatusCode.Unauthorized, api.StatusCode);
            Assert.DoesNotContain(target.Email, await api.Content.ReadAsStringAsync());
            using HttpResponseMessage page = await anonymous.GetAsync($"/cabinet/{target.Id}");
            Assert.Equal(HttpStatusCode.Redirect, page.StatusCode);
            Assert.DoesNotContain(target.Email, await page.Content.ReadAsStringAsync());
        }
        using HttpResponseMessage anonymousCabinet = await anonymous.GetAsync("/cabinet");
        Assert.Equal(HttpStatusCode.Redirect, anonymousCabinet.StatusCode);
        using HttpResponseMessage anonymousMe = await anonymous.GetAsync("/api/me");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousMe.StatusCode);
        using HttpResponseMessage aliceLogin = await alice.PostAsJsonAsync("/api/login", new LoginInput(a.Email, alicePassword));
        using HttpResponseMessage bobLogin = await bob.PostAsJsonAsync("/api/login", new LoginInput(b.Email, bobPassword));
        Assert.Equal(HttpStatusCode.OK, aliceLogin.StatusCode);
        Assert.Equal(HttpStatusCode.OK, bobLogin.StatusCode);
        await CheckIsolation(alice, a, b, factory.Accounts.Get(b.Email)!.PasswordHash, bobPassword);
        await CheckIsolation(bob, b, a, factory.Accounts.Get(a.Email)!.PasswordHash, alicePassword);
    }

    private static async Task<AccountView> Register(HttpClient client, string email, string password)
    {
        using HttpResponseMessage response = await client.PostAsJsonAsync("/api/register", new RegistrationInput(email, password));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        AccountView? account = await response.Content.ReadFromJsonAsync<AccountView>();
        Assert.NotNull(account);
        return account;
    }

    private static async Task CheckIsolation(HttpClient client, AccountView owner, AccountView other, string otherHash, string otherPassword)
    {
        using HttpResponseMessage ownApi = await client.GetAsync($"/api/accounts/{owner.Id}");
        Assert.Equal(HttpStatusCode.OK, ownApi.StatusCode);
        AccountView? own = await ownApi.Content.ReadFromJsonAsync<AccountView>();
        Assert.NotNull(own);
        Assert.Equal(owner.Id, own.Id);
        Assert.Equal(owner.Email, own.Email);
        Assert.Contains(owner.Email, await client.GetStringAsync($"/cabinet/{owner.Id}"));
        foreach (string route in new[] { $"/api/accounts/{other.Id}", $"/cabinet/{other.Id}" })
        {
            using HttpResponseMessage forbidden = await client.GetAsync(route);
            Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
            string body = await forbidden.Content.ReadAsStringAsync();
            Assert.DoesNotContain(other.Email, body);
            Assert.DoesNotContain(otherHash, body);
            Assert.DoesNotContain(otherPassword, body);
        }
        using HttpResponseMessage me = await client.GetAsync("/api/me");
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        AccountView? identity = await me.Content.ReadFromJsonAsync<AccountView>();
        Assert.NotNull(identity);
        Assert.Equal(owner.Id, identity.Id);
        string cabinet = await client.GetStringAsync("/cabinet");
        Assert.Contains(owner.Email, cabinet);
        Assert.DoesNotContain(other.Email, cabinet);
    }
}
