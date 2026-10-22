using System.Net;
using System.Net.Http.Json;
using Demo.Web;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Demo.Tests;

public sealed class RegistrationWhitespacePositiveGeneratedTests
{
    [Theory]
    [InlineData(12)]
    [InlineData(24)]
    [InlineData(128)]
    public async Task One_nonwhitespace_character_preserves_the_entire_password(int length)
    {
        using DemoWebFactory factory = new();
        using HttpClient client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        string credential = "\t" + new string(' ', length - 3) + "Z\t";
        const string email = "mixed-whitespace@example.com";
        using HttpResponseMessage registration = await client.PostAsJsonAsync("/api/register", new RegistrationInput(email, credential));
        Assert.Equal(HttpStatusCode.Created, registration.StatusCode);
        Assert.Equal(1, factory.Accounts.Count);
        Account? stored = factory.Accounts.Get(email);
        Assert.NotNull(stored);
        Assert.NotEqual(credential, stored.PasswordHash);
        Assert.NotEqual(PasswordVerificationResult.Failed, new PasswordHasher<Account>().VerifyHashedPassword(stored, stored.PasswordHash, credential));
        Assert.Equal(PasswordVerificationResult.Failed, new PasswordHasher<Account>().VerifyHashedPassword(stored, stored.PasswordHash, credential.Trim()));
        Assert.DoesNotContain(stored.PasswordHash, await registration.Content.ReadAsStringAsync());

        using HttpResponseMessage login = await client.PostAsJsonAsync("/api/login", new LoginInput(email, credential));
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        using HttpResponseMessage me = await client.GetAsync("/api/me");
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        AccountView? identity = await me.Content.ReadFromJsonAsync<AccountView>();
        Assert.NotNull(identity);
        Assert.Equal(stored.Id, identity.Id);
        Assert.Equal(email, identity.Email);
        Assert.Contains(email, await client.GetStringAsync("/cabinet"));
    }
}
