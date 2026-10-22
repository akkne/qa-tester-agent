using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Demo.Web;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Demo.Tests;

public sealed class WhitespacePasswordRejectionTests
{
    [Theory]
    [InlineData(12, false)]
    [InlineData(64, false)]
    [InlineData(128, false)]
    [InlineData(12, true)]
    [InlineData(64, true)]
    [InlineData(128, true)]
    public async Task Mixed_whitespace_is_rejected_and_email_can_be_used_after_correction(int length, bool useForm)
    {
        using DemoWebFactory factory = new();
        using HttpClient client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        const string email = "correctable@example.com";
        string credential = string.Concat(Enumerable.Range(0, length).Select(index => index % 3 == 0 ? "\t" : index % 3 == 1 ? "\r" : "\n"));
        HttpResponseMessage response;
        if (useForm)
        {
            string page = await client.GetStringAsync("/register");
            Match token = Regex.Match(page, "name=\"__RequestVerificationToken\" value=\"([^\"]+)\"");
            Assert.True(token.Success, "Registration must provide an antiforgery token.");
            using FormUrlEncodedContent form = new(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = WebUtility.HtmlDecode(token.Groups[1].Value),
                ["email"] = email,
                ["password"] = credential
            });
            response = await client.PostAsync("/register", form);
        }
        else
        {
            response = await client.PostAsJsonAsync("/api/register", new RegistrationInput(email, credential));
        }

        using (response)
        {
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            string error;
            if (useForm)
            {
                string html = await response.Content.ReadAsStringAsync();
                Match element = Regex.Match(html, "<[^>]*data-testid=\"form-error\"[^>]*>(.*?)</[^>]+>", RegexOptions.Singleline);
                Assert.True(element.Success, "A visible form-error element is required.");
                error = WebUtility.HtmlDecode(Regex.Replace(element.Groups[1].Value, "<[^>]+>", ""));
            }
            else
            {
                ErrorView? view = await response.Content.ReadFromJsonAsync<ErrorView>();
                Assert.NotNull(view);
                error = view!.Error;
            }
            Assert.False(string.IsNullOrWhiteSpace(error));
            Assert.True(error.Contains("парол", StringComparison.OrdinalIgnoreCase) || error.Contains("password", StringComparison.OrdinalIgnoreCase), error);
            Assert.True(error.Contains("непробель", StringComparison.OrdinalIgnoreCase) || error.Contains("пробел", StringComparison.OrdinalIgnoreCase) || error.Contains("whitespace", StringComparison.OrdinalIgnoreCase), error);
        }
        Assert.Equal(0, factory.Accounts.Count);
        Assert.Null(factory.Accounts.Get(email));

        string corrected = new string('R', length);
        using HttpResponseMessage retry = await client.PostAsJsonAsync("/api/register", new RegistrationInput(email, corrected));
        Assert.Equal(HttpStatusCode.Created, retry.StatusCode);
        Assert.Equal(1, factory.Accounts.Count);
        Assert.NotNull(factory.Accounts.Get(email));
    }
}
