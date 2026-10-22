using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Demo.Web;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Demo.Tests;

public sealed class DuplicateRegistrationIntegrityNegativeTests
{
    [Theory]
    [InlineData(false, "alice@example.com")]
    [InlineData(false, "  ALICE@EXAMPLE.COM  ")]
    [InlineData(true, "alice@example.com")]
    [InlineData(true, "  ALICE@EXAMPLE.COM  ")]
    public async Task Duplicate_does_not_replace_original_identity_or_password(bool useForm, string duplicateEmail)
    {
        using var factory = new DemoWebFactory();
        var options = new WebApplicationFactoryClientOptions { AllowAutoRedirect = false };
        using var client = factory.CreateClient(options);
        const string email = "alice@example.com";
        const string originalPassword = "Original-synthetic-42!";
        const string replacementPassword = "Replacement-synthetic-73!";
        using var created = await client.PostAsJsonAsync("/api/register", new RegistrationInput(email, originalPassword));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var original = factory.Accounts.Get(email);
        Assert.NotNull(original);
        Guid originalId = original.Id;
        string originalHash = original.PasswordHash;

        HttpResponseMessage duplicate;
        if (useForm)
        {
            string html = await client.GetStringAsync("/register");
            Match token = Regex.Match(html, "name=\"__RequestVerificationToken\" value=\"([^\"]+)\"");
            Assert.True(token.Success);
            using var form = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = WebUtility.HtmlDecode(token.Groups[1].Value),
                ["email"] = duplicateEmail,
                ["password"] = replacementPassword
            });
            duplicate = await client.PostAsync("/register", form);
        }
        else
        {
            duplicate = await client.PostAsJsonAsync("/api/register", new RegistrationInput(duplicateEmail, replacementPassword));
        }

        using (duplicate)
        {
            Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
            string body = await duplicate.Content.ReadAsStringAsync();
            string error;
            if (useForm)
            {
                Match element = Regex.Match(body, "<[^>]*data-testid=\"form-error\"[^>]*>(.*?)</[^>]+>", RegexOptions.Singleline);
                Assert.True(element.Success);
                error = WebUtility.HtmlDecode(Regex.Replace(element.Groups[1].Value, "<[^>]+>", ""));
            }
            else
            {
                using var json = JsonDocument.Parse(body);
                error = json.RootElement.GetProperty("error").GetString() ?? "";
            }
            Assert.Contains("уже зарегистрирован", error, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(originalPassword, body);
            Assert.DoesNotContain(replacementPassword, body);
            Assert.DoesNotContain(originalHash, body);
        }
        Assert.Equal(1, factory.Accounts.Count);
        var preserved = factory.Accounts.Get(email);
        Assert.NotNull(preserved);
        Assert.Equal(originalId, preserved.Id);
        Assert.Equal(originalHash, preserved.PasswordHash);

        using var rejectedSession = factory.CreateClient(options);
        using var denied = await rejectedSession.PostAsJsonAsync("/api/login", new LoginInput(email, replacementPassword));
        Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
        using var unauthorized = await rejectedSession.GetAsync("/api/me");
        Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);
        using var owner = factory.CreateClient(options);
        using var login = await owner.PostAsJsonAsync("/api/login", new LoginInput(email, originalPassword));
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var view = await owner.GetFromJsonAsync<AccountView>("/api/me");
        Assert.NotNull(view);
        Assert.Equal(originalId, view.Id);
    }
}
