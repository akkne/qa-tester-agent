using Microsoft.Extensions.DependencyInjection;
using QaAgent.Application;
using QaAgent.Application.Abstractions;
using QaAgent.Host.Composition;
using QaAgent.Infrastructure.Time;

namespace QaAgent.IntegrationTests;

public sealed class CompositionTests
{
    [Fact]
    public void Host_registers_the_clock_and_run_factory()
    {
        ServiceCollection services = new();
        services.AddQaAgentCore();
        using ServiceProvider provider = services.BuildServiceProvider(validateScopes: true);

        Assert.IsType<SystemClock>(provider.GetRequiredService<IClock>());
        Assert.NotNull(provider.GetRequiredService<RunFactory>());
    }
}
