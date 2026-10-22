using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Demo.Tests;

public sealed class RegistrationWhitespaceFormNegativeTests
{
    [Theory]
    [InlineData(12)]
    [InlineData(128)]
    public async Task Whitespace_only_form_password_returns_clear_error_without_creating_account(int length)
    {
        using DemoWebFactory factory = new();
        using HttpClient client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        const string email = "whitespace-form@example.test";
        string html = await client.GetStringAsync("/register");
        Match token = Regex.Match(html, "name=\"__RequestVerificationToken\" value=\"([^\"]+)\"");
        Assert.True(token.Success, "Registration must provide an antiforgery token.");
        using FormUrlEncodedContent form = new(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = WebUtility.HtmlDecode(token.Groups[1].Value),
            ["email"] = email,
            ["password"] = new string(' ', length)
        });
        using HttpResponseMessage response = await client.PostAsync("/register", form);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Null(response.Headers.Location);
        Assert.Equal("/register", response.RequestMessage!.RequestUri!.AbsolutePath);
        string body = await response.Content.ReadAsStringAsync();
        Match error = Regex.Match(body, "<[^>]*data-testid=\"form-error\"[^>]*>(.*?)</[^>]+>", RegexOptions.Singleline);
        Assert.True(error.Success, "Rejected registration must render the stable form-error element.");
        string message = WebUtility.HtmlDecode(Regex.Replace(error.Groups[1].Value, "<[^>]+>", ""));
        Assert.Contains("непробельный", message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, factory.Accounts.Count);
        Assert.Null(factory.Accounts.Get(email));
    }
}
