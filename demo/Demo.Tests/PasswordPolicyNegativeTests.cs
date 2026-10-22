using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Demo.Web;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Demo.Tests;

public sealed class PasswordPolicyNegativeTests
{
    public static IEnumerable<object[]> InvalidPasswords()
    {
        foreach (bool form in new[] { false, true })
        {
            yield return new object[] { new string('X', 11), form, "12" };
            yield return new object[] { new string('X', 129), form, "128" };
            yield return new object[] { new string(' ', 12), form, "" };
            yield return new object[] { new string(' ', 128), form, "" };
        }
        yield return new object[] { string.Concat(Enumerable.Repeat("\t\r\n", 4)), false, "" };
    }

    [Theory]
    [MemberData(nameof(InvalidPasswords))]
    public async Task Invalid_password_has_clear_error_and_never_creates_account(string password, bool useForm, string expectedLimit)
    {
        using DemoWebFactory factory = new();
        using HttpClient client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        const string email = "rejected@example.com";
        int before = factory.Accounts.Count;
        HttpResponseMessage response;
        if (useForm)
        {
            string page = await client.GetStringAsync("/register");
            Match token = Regex.Match(page, "name=\"__RequestVerificationToken\" value=\"([^\"]+)\"");
            Assert.True(token.Success, "Registration form must provide an antiforgery token.");
            using FormUrlEncodedContent form = new(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = WebUtility.HtmlDecode(token.Groups[1].Value),
                ["email"] = email,
                ["password"] = password
            });
            response = await client.PostAsync("/register", form);
        }
        else
        {
            response = await client.PostAsJsonAsync("/api/register", new RegistrationInput(email, password));
        }

        using (response)
        {
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            string body = await response.Content.ReadAsStringAsync();
            string error;
            if (useForm)
            {
                Match element = Regex.Match(body, "<[^>]*data-testid=\"form-error\"[^>]*>(.*?)</[^>]+>", RegexOptions.Singleline);
                Assert.True(element.Success, "Validation error must use the stable form-error selector.");
                error = WebUtility.HtmlDecode(Regex.Replace(element.Groups[1].Value, "<[^>]+>", ""));
            }
            else
            {
                using JsonDocument json = JsonDocument.Parse(body);
                error = json.RootElement.GetProperty("error").GetString() ?? "";
            }
            Assert.False(string.IsNullOrWhiteSpace(error));
            Assert.True(error.Contains("парол", StringComparison.OrdinalIgnoreCase) || error.Contains("password", StringComparison.OrdinalIgnoreCase), "Error must identify the password problem.");
            if (expectedLimit.Length > 0)
                Assert.Contains(expectedLimit, error);
            else
                Assert.True(error.Contains("пробел", StringComparison.OrdinalIgnoreCase) || error.Contains("непробель", StringComparison.OrdinalIgnoreCase) || error.Contains("whitespace", StringComparison.OrdinalIgnoreCase) || error.Contains("non-whitespace", StringComparison.OrdinalIgnoreCase), "Error must explain that a whitespace-only password is invalid.");
        }
        Assert.Equal(before, factory.Accounts.Count);
        Assert.Null(factory.Accounts.Get(email));
    }
}
