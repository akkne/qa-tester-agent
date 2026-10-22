using System.Net;
using System.Net.Http.Json;
using Demo.Web;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Demo.Tests;

public sealed class WhitespacePasswordAcceptanceTests
{
    [Theory]
    [InlineData(12, 0)]
    [InlineData(12, 6)]
    [InlineData(12, 11)]
    [InlineData(64, 0)]
    [InlineData(64, 32)]
    [InlineData(64, 63)]
    [InlineData(128, 0)]
    [InlineData(128, 64)]
    [InlineData(128, 127)]
    public async Task One_non_whitespace_character_anywhere_preserves_exact_password(int length, int position)
    {
        using DemoWebFactory factory = new();
        using HttpClient client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        char[] characters = new string(' ', length).ToCharArray();
        characters[position] = 'Z';
        string credential = new(characters);
        const string email = "single-character@example.com";

        using HttpResponseMessage registration = await client.PostAsJsonAsync("/api/register", new RegistrationInput(email, credential));
        Assert.Equal(HttpStatusCode.Created, registration.StatusCode);
        Assert.Equal(1, factory.Accounts.Count);
        Assert.NotNull(factory.Accounts.Get(email));

        using HttpResponseMessage shortenedLogin = await client.PostAsJsonAsync("/api/login", new LoginInput(email, credential.Trim()));
        Assert.Equal(HttpStatusCode.Unauthorized, shortenedLogin.StatusCode);
        using HttpResponseMessage anonymous = await client.GetAsync("/api/me");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);

        using HttpResponseMessage exactLogin = await client.PostAsJsonAsync("/api/login", new LoginInput(email, credential));
        Assert.Equal(HttpStatusCode.OK, exactLogin.StatusCode);
        using HttpResponseMessage me = await client.GetAsync("/api/me");
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        AccountView? identity = await me.Content.ReadFromJsonAsync<AccountView>();
        Assert.NotNull(identity);
        Assert.Equal(factory.Accounts.Get(email)!.Id, identity!.Id);
        Assert.Equal(email, identity.Email);
        Assert.Contains(email, await client.GetStringAsync("/cabinet"));
    }
}
