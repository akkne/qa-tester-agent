using System.Net;
using System.Net.Http.Json;
using Demo.Web;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Demo.Tests;

public sealed class SymmetricAccountIsolationAccessControlTests
{
    [Fact]
    public async Task Independent_sessions_allow_own_account_and_deny_foreign_account_in_both_directions()
    {
        using var factory = new DemoWebFactory();
        var options = new WebApplicationFactoryClientOptions { AllowAutoRedirect = false };
        using var alice = factory.CreateClient(options);
        using var bob = factory.CreateClient(options);
        using var anonymous = factory.CreateClient(options);
        const string password = "Synthetic-isolation-42!";
        AccountView a = await CreateAndLoginAsync(alice, "alice@example.com", password);
        AccountView b = await CreateAndLoginAsync(bob, "bob@example.com", password);

        foreach (var pair in new[] { (Client: alice, Own: a, Other: b), (Client: bob, Own: b, Other: a) })
        {
            foreach (string route in new[] { "/api/me", $"/api/accounts/{pair.Own.Id}" })
            {
                using var response = await pair.Client.GetAsync(route);
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                var view = await response.Content.ReadFromJsonAsync<AccountView>();
                Assert.NotNull(view);
                Assert.Equal(pair.Own.Id, view.Id);
                Assert.Equal(pair.Own.Email, view.Email);
            }
            foreach (string route in new[] { "/cabinet", $"/cabinet/{pair.Own.Id}" })
            {
                using var response = await pair.Client.GetAsync(route);
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                string body = await response.Content.ReadAsStringAsync();
                Assert.Contains(pair.Own.Email, body);
                Assert.DoesNotContain(pair.Other.Email, body);
            }
            foreach (string route in new[] { $"/cabinet/{pair.Other.Id}", $"/api/accounts/{pair.Other.Id}" })
            {
                using var response = await pair.Client.GetAsync(route);
                Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
                Assert.DoesNotContain(pair.Other.Email, await response.Content.ReadAsStringAsync());
            }
        }

        foreach (string route in new[] { "/cabinet", $"/cabinet/{a.Id}", $"/cabinet/{b.Id}", "/api/me", $"/api/accounts/{a.Id}", $"/api/accounts/{b.Id}" })
        {
            using var response = await anonymous.GetAsync(route);
            Assert.Equal(route.StartsWith("/api/", StringComparison.Ordinal) ? HttpStatusCode.Unauthorized : HttpStatusCode.Redirect, response.StatusCode);
            string body = await response.Content.ReadAsStringAsync();
            Assert.DoesNotContain(a.Email, body);
            Assert.DoesNotContain(b.Email, body);
        }
    }

    private static async Task<AccountView> CreateAndLoginAsync(HttpClient client, string email, string password)
    {
        using var created = await client.PostAsJsonAsync("/api/register", new RegistrationInput(email, password));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var account = await created.Content.ReadFromJsonAsync<AccountView>();
        Assert.NotNull(account);
        using var login = await client.PostAsJsonAsync("/api/login", new LoginInput(email, password));
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        return account;
    }
}
