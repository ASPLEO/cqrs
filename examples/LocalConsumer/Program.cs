using Aspleo.Cqrs;
using Aspleo.Cqrs.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

namespace Aspleo.Cqrs.Examples;

internal static class Program
{
    public static async Task Main()
    {
        var services = new ServiceCollection();
        services.AddScoped<Counter>();
        services.AddCqrs(typeof(Program).Assembly);
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var scope = provider.CreateScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IDispatcher>();
        await dispatcher.Send(new IncrementCommand());
        var result = await dispatcher.Send(new ReadCounterQuery());
        if (result != 1) throw new InvalidOperationException($"Resultado inesperado: {result}");
        Console.WriteLine($"Consumo NuGet validado: contador = {result}.");
    }
}

internal sealed record IncrementCommand : IRequest;
internal sealed record ReadCounterQuery : IRequest<int>;
internal sealed class Counter { public int Value { get; set; } }

internal sealed class IncrementHandler(Counter counter) : IRequestHandler<IncrementCommand>
{
    public Task Handle(IncrementCommand request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        counter.Value++;
        return Task.CompletedTask;
    }
}

internal sealed class ReadCounterHandler(Counter counter) : IRequestHandler<ReadCounterQuery, int>
{
    public Task<int> Handle(ReadCounterQuery request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(counter.Value);
    }
}
