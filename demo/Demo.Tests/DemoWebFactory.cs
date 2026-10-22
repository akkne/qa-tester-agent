using System.Collections.Concurrent;
using Demo.Web;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Demo.Tests;

public sealed class DemoWebFactory : WebApplicationFactory<Program>
{
    public MemoryAccountStore Accounts { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IAccountStore>();
            services.AddSingleton<IAccountStore>(Accounts);
        });
    }
}

public sealed class MemoryAccountStore : IAccountStore
{
    private readonly ConcurrentDictionary<string, Account> _accounts = new(StringComparer.Ordinal);

    public int Count => _accounts.Count;
    public Account? Get(string email) => _accounts.GetValueOrDefault(email);

    public Task<bool> TryCreateAsync(Account account) =>
        Task.FromResult(_accounts.TryAdd(account.Email, account));

    public Task<Account?> FindByEmailAsync(string email) =>
        Task.FromResult(_accounts.GetValueOrDefault(email));

    public Task<Account?> FindByIdAsync(Guid id) =>
        Task.FromResult(_accounts.Values.SingleOrDefault(account => account.Id == id));
}
