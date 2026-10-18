using Aspleo.Cqrs.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Aspleo.Cqrs.Tests;

public sealed class RegistrationTests
{
    [Fact]
    public void AddCqrs_is_idempotent_and_scoped()
    {
        var services = new ServiceCollection();
        services.AddCqrs(typeof(RegistrationTests).Assembly, typeof(RegistrationTests).Assembly);
        services.AddCqrs(typeof(RegistrationTests).Assembly);
        Assert.Single(services, x => x.ServiceType == typeof(IDispatcher));
        Assert.All(services, x => Assert.Equal(ServiceLifetime.Scoped, x.Lifetime));
        Assert.Single(services, x => x.ServiceType == typeof(IRequestHandler<TokenCommand>));
    }

    [Fact]
    public void Invalid_arguments_do_not_mutate_collection()
    {
        var services = new ServiceCollection();
        Assert.Throws<ArgumentNullException>(() => services.AddCqrs(null!));
        Assert.Throws<ArgumentException>(() => services.AddCqrs(new System.Reflection.Assembly[] { null! }));
        Assert.Throws<ArgumentNullException>(() => RequestDispatcherServiceCollectionExtensions.AddCqrs(null!));
        Assert.Empty(services);
    }

    [Theory]
    [InlineData(ServiceLifetime.Singleton)]
    [InlineData(ServiceLifetime.Transient)]
    public void Rejects_incompatible_existing_lifetime(ServiceLifetime lifetime)
    {
        IServiceCollection services = new ServiceCollection();
        services.Add(new ServiceDescriptor(typeof(IRequestHandler<TokenCommand>), typeof(TokenCommandHandler), lifetime));
        Assert.Throws<InvalidOperationException>(() => services.AddCqrs(typeof(RegistrationTests).Assembly));
        Assert.Single(services);
    }

    [Fact]
    public void Rejects_duplicate_identical_manual_registrations()
    {
        var services = new ServiceCollection();
        services.AddScoped<IRequestHandler<TokenCommand>, TokenCommandHandler>();
        services.AddScoped<IRequestHandler<TokenCommand>, TokenCommandHandler>();
        Assert.Throws<InvalidOperationException>(() => services.AddCqrs(typeof(RegistrationTests).Assembly));
        Assert.Equal(2, services.Count);
    }

    [Fact]
    public async Task Forwards_token_to_void_handler_and_preserves_async_typed_cancellation()
    {
        var services = new ServiceCollection().AddCqrs(typeof(RegistrationTests).Assembly);
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var scope = provider.CreateScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IDispatcher>();
        using var cts = new CancellationTokenSource();
        var command = new TokenCommand();
        await dispatcher.Send(command, cts.Token);
        Assert.Equal(cts.Token, command.Received);
        var pending = dispatcher.Send(new WaitQuery(), cts.Token);
        await cts.CancelAsync();
        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        Assert.Equal(cts.Token, error.CancellationToken);
    }

    [Fact]
    public async Task Void_dispatch_rejects_manual_duplicates()
    {
        var services = new ServiceCollection().AddCqrs(typeof(RegistrationTests).Assembly);
        services.AddScoped<IRequestHandler<TokenCommand>, TokenCommandHandler>();
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            scope.ServiceProvider.GetRequiredService<IDispatcher>().Send(new TokenCommand()));
        Assert.Contains(nameof(TokenCommand), error.Message);
    }

    internal sealed class TokenCommand : IRequest { public CancellationToken Received { get; set; } }
    internal sealed class TokenCommandHandler : IRequestHandler<TokenCommand>
    {
        public Task Handle(TokenCommand request, CancellationToken cancellationToken)
        {
            request.Received = cancellationToken;
            return Task.CompletedTask;
        }
    }

    internal sealed record WaitQuery : IRequest<int>;
    internal sealed class WaitQueryHandler : IRequestHandler<WaitQuery, int>
    {
        public async Task<int> Handle(WaitQuery request, CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return 1;
        }
    }
}
