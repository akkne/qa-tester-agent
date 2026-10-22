using System.Net;
using System.Net.Http.Json;
using Demo.Web;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Demo.Tests;

public sealed class RegistrationWhitespaceNegativeGeneratedTests
{
    [Theory]
    [InlineData('\t', 12)]
    [InlineData('\t', 128)]
    [InlineData('\u00a0', 12)]
    [InlineData('\u00a0', 128)]
    [InlineData('\u2003', 12)]
    [InlineData('\u2003', 128)]
    public async Task Whitespace_rejection_does_not_reserve_email(char whitespace, int length)
    {
        using DemoWebFactory factory = new();
        using HttpClient client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        const string email = "retry-whitespace@example.com";
        string rejectedCredential = new string(whitespace, length);
        using HttpResponseMessage rejected = await client.PostAsJsonAsync("/api/register", new RegistrationInput(email, rejectedCredential));
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        ErrorView? error = await rejected.Content.ReadFromJsonAsync<ErrorView>();
        Assert.NotNull(error);
        Assert.False(string.IsNullOrWhiteSpace(error.Error));
        Assert.True(error.Error.Contains("парол", StringComparison.OrdinalIgnoreCase) || error.Error.Contains("password", StringComparison.OrdinalIgnoreCase));
        Assert.True(error.Error.Contains("пробел", StringComparison.OrdinalIgnoreCase) || error.Error.Contains("whitespace", StringComparison.OrdinalIgnoreCase));
        Assert.Null(factory.Accounts.Get(email));
        Assert.Equal(0, factory.Accounts.Count);

        string acceptedCredential = new string('R', 24);
        using HttpResponseMessage retry = await client.PostAsJsonAsync("/api/register", new RegistrationInput(email, acceptedCredential));
        Assert.Equal(HttpStatusCode.Created, retry.StatusCode);
        Assert.Equal(1, factory.Accounts.Count);
        Assert.NotNull(factory.Accounts.Get(email));
        using HttpResponseMessage login = await client.PostAsJsonAsync("/api/login", new LoginInput(email, acceptedCredential));
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
    }
}
