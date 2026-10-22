using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Demo.Web;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Demo.Tests;

public sealed class DuplicateRegistrationCredentialNegativeTests
{
    [Theory]
    [InlineData("original@example.com")]
    [InlineData("ORIGINAL@EXAMPLE.COM")]
    [InlineData("  Original@Example.COM  ")]
    public async Task Duplicate_registration_preserves_original_identity_and_credentials(string duplicateEmail)
    {
        using DemoWebFactory factory = new();
        using HttpClient client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        string originalPassword = new string('A', 24);
        string replacementPassword = new string('B', 24);
        using HttpResponseMessage created = await client.PostAsJsonAsync("/api/register", new RegistrationInput("original@example.com", originalPassword));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Account original = factory.Accounts.Get("original@example.com")!;
        Assert.NotNull(original);
        Guid originalId = original.Id;
        string originalHash = original.PasswordHash;

        using HttpResponseMessage duplicate = await client.PostAsJsonAsync("/api/register", new RegistrationInput(duplicateEmail, replacementPassword));
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        using JsonDocument errorJson = JsonDocument.Parse(await duplicate.Content.ReadAsStringAsync());
        string error = errorJson.RootElement.GetProperty("error").GetString() ?? "";
        Assert.True(error.Contains("уже", StringComparison.OrdinalIgnoreCase) || error.Contains("already", StringComparison.OrdinalIgnoreCase), "Duplicate registration must explain that the email is already registered.");
        Assert.Equal(1, factory.Accounts.Count);
        Account retained = factory.Accounts.Get("original@example.com")!;
        Assert.NotNull(retained);
        Assert.Equal(originalId, retained.Id);
        Assert.Equal(originalHash, retained.PasswordHash);

        using HttpClient rejected = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        using HttpResponseMessage denied = await rejected.PostAsJsonAsync("/api/login", new LoginInput("original@example.com", replacementPassword));
        Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
        using HttpResponseMessage anonymous = await rejected.GetAsync("/api/me");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        using HttpClient owner = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        using HttpResponseMessage login = await owner.PostAsJsonAsync("/api/login", new LoginInput("original@example.com", originalPassword));
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        using HttpResponseMessage me = await owner.GetAsync("/api/me");
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        AccountView? view = await me.Content.ReadFromJsonAsync<AccountView>();
        Assert.NotNull(view);
        Assert.Equal(originalId, view.Id);
    }
}
