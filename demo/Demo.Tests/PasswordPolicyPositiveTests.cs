using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Demo.Web;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Demo.Tests;

public sealed class PasswordPolicyPositiveTests
{
    [Theory]
    [InlineData(12, false, false)]
    [InlineData(128, false, false)]
    [InlineData(12, true, false)]
    [InlineData(128, true, false)]
    [InlineData(12, false, true)]
    [InlineData(128, false, true)]
    [InlineData(12, true, true)]
    [InlineData(128, true, true)]
    public async Task Valid_boundary_password_is_created_and_authenticates(int length, bool oneNonWhitespace, bool useForm)
    {
        using DemoWebFactory factory = new();
        using HttpClient client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        string email = "boundary@example.com";
        string password = oneNonWhitespace ? new string(' ', length - 1) + "Z" : new string('Z', length);
        int before = factory.Accounts.Count;
        HttpResponseMessage registration;
        if (useForm)
        {
            string html = await client.GetStringAsync("/register");
            Match match = Regex.Match(html, "name=\"__RequestVerificationToken\" value=\"([^\"]+)\"");
            Assert.True(match.Success, "Registration form must provide an antiforgery token.");
            using FormUrlEncodedContent form = new(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = WebUtility.HtmlDecode(match.Groups[1].Value),
                ["email"] = email,
                ["password"] = password
            });
            registration = await client.PostAsync("/register", form);
        }
        else
        {
            registration = await client.PostAsJsonAsync("/api/register", new RegistrationInput(email, password));
        }

        using (registration)
        {
            Assert.Equal(useForm ? HttpStatusCode.Redirect : HttpStatusCode.Created, registration.StatusCode);
            if (useForm)
                Assert.Equal("/login?registered=1", registration.Headers.Location?.ToString());
        }
        Assert.Equal(before + 1, factory.Accounts.Count);
        Assert.NotNull(factory.Accounts.Get(email));

        using HttpClient session = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        using HttpResponseMessage login = await session.PostAsJsonAsync("/api/login", new LoginInput(email, password));
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        using HttpResponseMessage me = await session.GetAsync("/api/me");
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        AccountView? view = await me.Content.ReadFromJsonAsync<AccountView>();
        Assert.NotNull(view);
        Assert.Equal(email, view.Email);
        Assert.Equal(factory.Accounts.Get(email)!.Id, view.Id);
        Assert.Contains(email, await session.GetStringAsync("/cabinet"));
    }
}
