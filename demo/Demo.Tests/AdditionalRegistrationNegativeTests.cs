using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Demo.Web;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Demo.Tests;

public sealed class AdditionalRegistrationNegativeTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Empty_password_has_clear_error_and_creates_no_account(bool useForm)
    {
        using DemoWebFactory factory = new();
        using HttpClient client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        const string email = "empty-password@example.com";
        HttpResponseMessage response;
        if (useForm)
        {
            string html = await client.GetStringAsync("/register");
            Match token = Regex.Match(html, "name=\"__RequestVerificationToken\" value=\"([^\"]+)\"");
            Assert.True(token.Success);
            using FormUrlEncodedContent form = new(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = WebUtility.HtmlDecode(token.Groups[1].Value),
                ["email"] = email,
                ["password"] = string.Empty
            });
            response = await client.PostAsync("/register", form);
        }
        else
        {
            response = await client.PostAsJsonAsync("/api/register", new RegistrationInput(email, string.Empty));
        }
        using (response)
        {
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            string body = await response.Content.ReadAsStringAsync();
            string error;
            if (useForm)
            {
                Match element = Regex.Match(body, "<[^>]*data-testid=\"form-error\"[^>]*>(.*?)</[^>]+>", RegexOptions.Singleline);
                Assert.True(element.Success, "Password validation must expose form-error.");
                error = WebUtility.HtmlDecode(Regex.Replace(element.Groups[1].Value, "<[^>]+>", ""));
            }
            else
            {
                using JsonDocument json = JsonDocument.Parse(body);
                error = json.RootElement.GetProperty("error").GetString() ?? string.Empty;
            }
            Assert.False(string.IsNullOrWhiteSpace(error));
            Assert.True(error.Contains("парол", StringComparison.OrdinalIgnoreCase) || error.Contains("password", StringComparison.OrdinalIgnoreCase), "Error must identify the password problem.");
        }
        Assert.Equal(0, factory.Accounts.Count);
        Assert.Null(factory.Accounts.Get(email));
    }
}
