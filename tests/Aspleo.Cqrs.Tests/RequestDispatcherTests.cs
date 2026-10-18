using System.Reflection;
using System.Reflection.Emit;
using Microsoft.Extensions.DependencyInjection;
using Aspleo.Cqrs;

using Aspleo.Cqrs.DependencyInjection;

using Xunit;

namespace Aspleo.Cqrs.Tests;

public sealed class RequestDispatcherTests
{
    [Fact]
    public async Task Resolve_handler_interno_com_dependencia_scoped_e_retorna_resultado()
    {
        using var provider = Services().BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var scope = provider.CreateScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IDispatcher>();
        Assert.Equal("resposta", await dispatcher.Send(new ReadRequest("resposta")));
        Assert.Equal(1, scope.ServiceProvider.GetRequiredService<Probe>().Calls);
    }

    [Fact]
    public async Task Command_sem_retorno_executa_handler()
    {
        using var provider = Services().BuildServiceProvider();
        using var scope = provider.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IDispatcher>().Send(new WriteRequest());
        Assert.Equal(1, scope.ServiceProvider.GetRequiredService<Probe>().Calls);
    }

    [Fact]
    public async Task Preserva_tipos_de_valor_interfaces_e_resultado_nulo()
    {
        using var provider = Services().BuildServiceProvider();
        using var scope = provider.CreateScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IDispatcher>();
        Assert.Equal(42, await dispatcher.Send(new ValueRequest()));
        Assert.Equal(new[] { "item" }, await dispatcher.Send(new ListRequest()));
        Assert.Null(await dispatcher.Send(new NullableRequest()));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Preserva_excecao_original_com_retorno(bool asynchronous)
    {
        using var provider = Services().BuildServiceProvider();
        using var scope = provider.CreateScope();
        var error = new TestValidationException("Dados inválidos.");
        var dispatcher = scope.ServiceProvider.GetRequiredService<IDispatcher>();
        var received = await Assert.ThrowsAsync<TestValidationException>(() =>
            dispatcher.Send(new ThrowRequest(error, asynchronous)));
        Assert.Same(error, received);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Preserva_excecao_original_sem_retorno(bool asynchronous)
    {
        using var provider = Services().BuildServiceProvider();
        using var scope = provider.CreateScope();
        var error = new TestConflictException("Conflito.");
        var dispatcher = scope.ServiceProvider.GetRequiredService<IDispatcher>();
        var received = await Assert.ThrowsAsync<TestConflictException>(() =>
            dispatcher.Send(new VoidThrowRequest(error, asynchronous)));
        Assert.Same(error, received);
    }

    [Fact]
    public async Task Token_ja_cancelado_impede_execucao_dos_dois_tipos_de_handler()
    {
        using var provider = Services().BuildServiceProvider();
        using var scope = provider.CreateScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IDispatcher>();
        var cancellation = new CancellationToken(canceled: true);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => dispatcher.Send(new WriteRequest(), cancellation));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => dispatcher.Send(new ReadRequest("x"), cancellation));
        Assert.Equal(0, scope.ServiceProvider.GetRequiredService<Probe>().Calls);
    }

    [Fact]
    public async Task Encaminha_token_e_preserva_cancelamento_durante_handler()
    {
        using var provider = Services().BuildServiceProvider();
        using var scope = provider.CreateScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IDispatcher>();
        using var cts = new CancellationTokenSource();
        Assert.Equal(cts.Token, await dispatcher.Send(new TokenRequest(), cts.Token));
        var pending = dispatcher.Send(new WaitRequest(), cts.Token);
        await cts.CancelAsync();
        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        Assert.Equal(cts.Token, exception.CancellationToken);
    }

    [Fact]
    public async Task Rejeita_requests_nulos()
    {
        using var provider = Services().BuildServiceProvider();
        using var scope = provider.CreateScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IDispatcher>();
        await Assert.ThrowsAsync<ArgumentNullException>(() => dispatcher.Send((IRequest)null!));
        await Assert.ThrowsAsync<ArgumentNullException>(() => dispatcher.Send<string>(null!));
    }

    [Fact]
    public async Task Falta_de_handler_tem_diagnostico_claro()
    {
        using var provider = Services().BuildServiceProvider();
        using var scope = provider.CreateScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IDispatcher>();
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => dispatcher.Send(new MissingRequest()));
        Assert.Contains(nameof(MissingRequest), error.Message);
        Assert.Contains("Nenhum handler", error.Message);
        await Assert.ThrowsAsync<InvalidOperationException>(() => dispatcher.Send(new MissingVoidRequest()));
    }

    [Fact]
    public async Task Cache_nao_reutiliza_handlers_ou_dependencias_entre_escopos_e_providers()
    {
        using var firstProvider = Services().BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var secondProvider = Services().BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var firstScope = firstProvider.CreateScope();
        using var secondScope = firstProvider.CreateScope();
        using var thirdScope = secondProvider.CreateScope();
        var first = firstScope.ServiceProvider.GetRequiredService<IDispatcher>();
        Assert.Same(first, firstScope.ServiceProvider.GetRequiredService<IDispatcher>());
        await first.Send(new ReadRequest("primeiro"));
        await first.Send(new ReadRequest("primeiro"));
        await secondScope.ServiceProvider.GetRequiredService<IDispatcher>().Send(new ReadRequest("segundo"));
        await thirdScope.ServiceProvider.GetRequiredService<IDispatcher>().Send(new ReadRequest("terceiro"));
        var firstProbe = firstScope.ServiceProvider.GetRequiredService<Probe>();
        var secondProbe = secondScope.ServiceProvider.GetRequiredService<Probe>();
        var thirdProbe = thirdScope.ServiceProvider.GetRequiredService<Probe>();
        Assert.NotSame(firstProbe, secondProbe);
        Assert.NotSame(firstProbe, thirdProbe);
        Assert.Equal(2, firstProbe.Calls);
        Assert.Equal(1, secondProbe.Calls);
        Assert.Equal(1, thirdProbe.Calls);
    }

    [Fact]
    public void Registro_repetido_nao_duplica_dispatcher_ou_handlers()
    {
        var services = Services();
        services.AddRequestDispatcher();
        services.AddRequestHandlers(typeof(RequestDispatcherTests).Assembly, typeof(RequestDispatcherTests).Assembly);
        Assert.Single(services, item => item.ServiceType == typeof(IDispatcher));
        Assert.Single(services, item => item.ServiceType == typeof(IRequestHandler<ReadRequest, string>));
        Assert.Single(services, item => item.ServiceType == typeof(IRequestHandler<WriteRequest>));
    }

    [Fact]
    public void Detecta_duplicidade_entre_assemblies_antes_de_registrar_handlers()
    {
        var dynamicAssembly = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("Duplicados" + Guid.NewGuid().ToString("N")), AssemblyBuilderAccess.Run);
        var module = dynamicAssembly.DefineDynamicModule("Handlers");
        var parent = typeof(DuplicateHandlerBase<int>);
        module.DefineType("FirstHandler", TypeAttributes.Public, parent).CreateType();
        module.DefineType("SecondHandler", TypeAttributes.Public, parent).CreateType();
        var services = new ServiceCollection();
        Assert.Throws<InvalidOperationException>(() => services.AddRequestHandlers(dynamicAssembly));
        Assert.Empty(services);
    }

    [Fact]
    public void Detecta_registro_manual_conflitante_e_nao_altera_collection()
    {
        var services = new ServiceCollection();
        services.AddScoped<IRequestHandler<ReadRequest, string>, AlternateReadHandler<int>>();
        Assert.Throws<InvalidOperationException>(() => services.AddRequestHandlers(typeof(RequestDispatcherTests).Assembly));
        Assert.Single(services);
    }

    [Fact]
    public async Task Dispatcher_tambem_rejeita_duplicidade_introduzida_apos_registro()
    {
        var services = Services();
        services.AddScoped<IRequestHandler<ReadRequest, string>, AlternateReadHandler<int>>();
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => scope.ServiceProvider.GetRequiredService<IDispatcher>()
            .Send(new ReadRequest("x")));
        Assert.Contains("Mais de um handler", error.Message);
    }

    private static IServiceCollection Services()
    {
        var services = new ServiceCollection();
        services.AddScoped<Probe>();
        services.AddRequestHandlers(typeof(RequestDispatcherTests).Assembly);
        services.AddRequestDispatcher();
        return services;
    }

    internal sealed class Probe { public int Calls { get; set; } }
    internal sealed record ReadRequest(string Text) : IRequest<string>;
    internal sealed record WriteRequest : IRequest;
    internal sealed record ValueRequest : IRequest<int>;
    internal sealed record NullableRequest : IRequest<string?>;
    internal sealed record ListRequest : IRequest<IReadOnlyList<string>>;
    internal sealed record TokenRequest : IRequest<CancellationToken>;
    internal sealed record WaitRequest : IRequest;
    internal sealed record MissingRequest : IRequest<string>;
    internal sealed record MissingVoidRequest : IRequest;
    internal sealed record ThrowRequest(Exception Error, bool Asynchronous) : IRequest<string>;
    internal sealed record VoidThrowRequest(Exception Error, bool Asynchronous) : IRequest;

    internal sealed class ReadHandler(Probe probe) : IRequestHandler<ReadRequest, string>
    {
        public Task<string> Handle(ReadRequest request, CancellationToken cancellationToken)
        {
            probe.Calls++;
            return Task.FromResult(request.Text);
        }
    }

    internal sealed class WriteHandler(Probe probe) : IRequestHandler<WriteRequest>
    {
        public Task Handle(WriteRequest request, CancellationToken cancellationToken)
        {
            probe.Calls++;
            return Task.CompletedTask;
        }
    }

    internal sealed class ValueHandler : IRequestHandler<ValueRequest, int>
    {
        public Task<int> Handle(ValueRequest request, CancellationToken cancellationToken) => Task.FromResult(42);
    }

    internal sealed class NullableHandler : IRequestHandler<NullableRequest, string?>
    {
        public Task<string?> Handle(NullableRequest request, CancellationToken cancellationToken) => Task.FromResult<string?>(null);
    }

    internal sealed class ListHandler : IRequestHandler<ListRequest, IReadOnlyList<string>>
    {
        public Task<IReadOnlyList<string>> Handle(ListRequest request, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<string>>(new[] { "item" });
    }

    internal sealed class TokenHandler : IRequestHandler<TokenRequest, CancellationToken>
    {
        public Task<CancellationToken> Handle(TokenRequest request, CancellationToken cancellationToken)
            => Task.FromResult(cancellationToken);
    }

    internal sealed class WaitHandler : IRequestHandler<WaitRequest>
    {
        public Task Handle(WaitRequest request, CancellationToken cancellationToken) => Task.Delay(Timeout.Infinite, cancellationToken);
    }

    internal sealed class ThrowHandler : IRequestHandler<ThrowRequest, string>
    {
        public Task<string> Handle(ThrowRequest request, CancellationToken cancellationToken)
            => request.Asynchronous ? FailAsync(request.Error) : throw request.Error;
        private static async Task<string> FailAsync(Exception error) { await Task.Yield(); throw error; }
    }

    internal sealed class VoidThrowHandler : IRequestHandler<VoidThrowRequest>
    {
        public Task Handle(VoidThrowRequest request, CancellationToken cancellationToken)
            => request.Asynchronous ? FailAsync(request.Error) : throw request.Error;
        private static async Task FailAsync(Exception error) { await Task.Yield(); throw error; }
    }

    internal sealed class AlternateReadHandler<TUnused> : IRequestHandler<ReadRequest, string>
    {
        public Task<string> Handle(ReadRequest request, CancellationToken cancellationToken) => Task.FromResult("outro");
    }

    public sealed record DuplicateRequest : IRequest;
    public class DuplicateHandlerBase<TUnused> : IRequestHandler<DuplicateRequest>
    {
        public Task Handle(DuplicateRequest request, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}

internal sealed class TestValidationException(string message) : Exception(message);
internal sealed class TestConflictException(string message) : Exception(message);

