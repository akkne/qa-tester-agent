using System.Net;
using System.Net.Http.Json;
using Demo.Web;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Demo.Tests;

public sealed class AdditionalRegistrationAccessControlTests
{
    [Fact]
    public async Task Ownership_is_enforced_in_both_directions_without_disclosing_other_account()
    {
        using DemoWebFactory factory = new();
        using HttpClient alice = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        using HttpClient bob = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        string credential = new string('Q', 24);
        AccountView a = await CreateAndLogin(alice, "alice@example.com", credential);
        AccountView b = await CreateAndLogin(bob, "bob@example.com", credential);
        await Verify(alice, a, b);
        await Verify(bob, b, a);
        Assert.Equal(2, factory.Accounts.Count);
    }

    private static async Task<AccountView> CreateAndLogin(HttpClient client, string email, string credential)
    {
        using HttpResponseMessage registration = await client.PostAsJsonAsync("/api/register", new RegistrationInput(email, credential));
        Assert.Equal(HttpStatusCode.Created, registration.StatusCode);
        AccountView? account = await registration.Content.ReadFromJsonAsync<AccountView>();
        Assert.NotNull(account);
        using HttpResponseMessage login = await client.PostAsJsonAsync("/api/login", new LoginInput(email, credential));
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        return account;
    }

    private static async Task Verify(HttpClient client, AccountView own, AccountView other)
    {
        using HttpResponseMessage me = await client.GetAsync("/api/me");
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        AccountView? profile = await me.Content.ReadFromJsonAsync<AccountView>();
        Assert.NotNull(profile);
        Assert.Equal(own.Id, profile.Id);
        Assert.Equal(own.Email, profile.Email);
        using HttpResponseMessage cabinet = await client.GetAsync($"/cabinet/{own.Id}");
        Assert.Equal(HttpStatusCode.OK, cabinet.StatusCode);
        string ownPage = await cabinet.Content.ReadAsStringAsync();
        Assert.Contains(own.Email, ownPage);
        Assert.DoesNotContain(other.Email, ownPage);
        foreach (string route in new[] { $"/cabinet/{other.Id}", $"/api/accounts/{other.Id}" })
        {
            using HttpResponseMessage denied = await client.GetAsync(route);
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
            string body = await denied.Content.ReadAsStringAsync();
            Assert.DoesNotContain(other.Email, body);
            Assert.DoesNotContain(other.Id.ToString(), body);
        }
    }
}
