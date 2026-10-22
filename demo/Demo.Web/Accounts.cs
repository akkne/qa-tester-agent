using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Demo.Web;

public sealed class Account
{
    public Guid Id { get; set; }
    public string Email { get; set; } = "";
    public string PasswordHash { get; set; } = "";
}

public sealed class DemoDbContext(DbContextOptions<DemoDbContext> options) : DbContext(options)
{
    public DbSet<Account> Accounts => Set<Account>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Account>().HasKey(account => account.Id);
        modelBuilder.Entity<Account>().HasIndex(account => account.Email).IsUnique();
        modelBuilder.Entity<Account>().Property(account => account.Email).HasMaxLength(254);
    }
}

public interface IAccountStore
{
    Task<bool> TryCreateAsync(Account account);
    Task<Account?> FindByEmailAsync(string email);
    Task<Account?> FindByIdAsync(Guid id);
}

public sealed class PostgresAccountStore(DemoDbContext database) : IAccountStore
{
    public async Task<bool> TryCreateAsync(Account account)
    {
        database.Accounts.Add(account);
        try
        {
            await database.SaveChangesAsync();
            return true;
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            return false;
        }
    }

    public Task<Account?> FindByEmailAsync(string email) =>
        database.Accounts.AsNoTracking().SingleOrDefaultAsync(account => account.Email == email);

    public Task<Account?> FindByIdAsync(Guid id) =>
        database.Accounts.AsNoTracking().SingleOrDefaultAsync(account => account.Id == id);
}

public sealed class AccountService(IAccountStore store)
{
    private readonly PasswordHasher<Account> _passwordHasher = new();

    public async Task<RegistrationResult> RegisterAsync(string? emailInput, string? password)
    {
        string email = (emailInput ?? "").Trim().ToLowerInvariant();
        if (email.Length is 0 or > 254 || !new EmailAddressAttribute().IsValid(email))
            return new(null, "Укажите корректный email.", false);
        if (password is null || password.Length < 12)
            return new(null, "Пароль должен содержать не менее 12 символов.", false);
        if (password.Length > 128)
            return new(null, "Пароль должен содержать не более 128 символов.", false);
        if (string.IsNullOrWhiteSpace(password))
            return new(null, "Пароль должен содержать хотя бы один непробельный символ.", false);

        Account account = new() { Id = Guid.NewGuid(), Email = email };
        account.PasswordHash = _passwordHasher.HashPassword(account, password);
        if (!await store.TryCreateAsync(account))
            return new(null, "Пользователь с таким email уже зарегистрирован.", true);
        return new(account, null, false);
    }

    public async Task<Account?> AuthenticateAsync(string? emailInput, string? password)
    {
        string email = (emailInput ?? "").Trim().ToLowerInvariant();
        if (password is null) return null;
        Account? account = await store.FindByEmailAsync(email);
        if (account is null) return null;
        PasswordVerificationResult result = _passwordHasher.VerifyHashedPassword(account, account.PasswordHash, password);
        return result == PasswordVerificationResult.Failed ? null : account;
    }
}

public sealed record RegistrationInput(string? Email, string? Password);
public sealed record LoginInput(string? Email, string? Password);
public sealed record AccountView(Guid Id, string Email);
public sealed record ErrorView(string Error);
public sealed record RegistrationResult(Account? Account, string? Error, bool IsDuplicate);
