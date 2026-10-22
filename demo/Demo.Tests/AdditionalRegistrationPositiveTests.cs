using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Demo.Web;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Demo.Tests;

public sealed class AdditionalRegistrationPositiveTests
{
    [Theory]
    [InlineData(12, false)]
    [InlineData(128, false)]
    [InlineData(12, true)]
    [InlineData(128, true)]
    public async Task Leading_nonwhitespace_password_is_preserved_and_email_is_normalized(int length, bool useForm)
    {
        using DemoWebFactory factory = new();
        using HttpClient client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        string password = string.Concat("Z", new string(' ', length - 1));
        HttpResponseMessage response;
        if (useForm)
        {
            string html = await client.GetStringAsync("/register");
            Match token = Regex.Match(html, "name=\"__RequestVerificationToken\" value=\"([^\"]+)\"");
            Assert.True(token.Success);
            using FormUrlEncodedContent form = new(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = WebUtility.HtmlDecode(token.Groups[1].Value),
                ["email"] = "  Alice@Example.COM  ",
                ["password"] = password
            });
            response = await client.PostAsync("/register", form);
        }
        else
        {
            response = await client.PostAsJsonAsync("/api/register", new RegistrationInput("  Alice@Example.COM  ", password));
        }
        using (response)
            Assert.Equal(useForm ? HttpStatusCode.Redirect : HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(1, factory.Accounts.Count);
        Account account = Assert.IsType<Account>(factory.Accounts.Get("alice@example.com"));
        Assert.Equal("alice@example.com", account.Email);

        using HttpClient session = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        using HttpResponseMessage login = await session.PostAsJsonAsync("/api/login", new LoginInput("alice@example.com", password));
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        using HttpResponseMessage me = await session.GetAsync("/api/me");
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        AccountView? view = await me.Content.ReadFromJsonAsync<AccountView>();
        Assert.NotNull(view);
        Assert.Equal(account.Id, view.Id);
        Assert.Equal(account.Email, view.Email);
        Assert.Contains(account.Email, await session.GetStringAsync("/cabinet"));
    }
}
