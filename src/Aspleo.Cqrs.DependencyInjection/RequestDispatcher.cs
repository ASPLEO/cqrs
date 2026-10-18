using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Aspleo.Cqrs;

namespace Aspleo.Cqrs.DependencyInjection;

internal sealed class RequestDispatcher(IServiceProvider serviceProvider) : IDispatcher
{
    private static readonly ConcurrentDictionary<Type, IVoidInvoker> VoidInvokers = new();
    private static readonly ConcurrentDictionary<(Type Request, Type Response), object> ResponseInvokers = new();

    public Task Send(IRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        var invoker = VoidInvokers.GetOrAdd(request.GetType(), type =>
            (IVoidInvoker)Activator.CreateInstance(typeof(VoidInvoker<>).MakeGenericType(type), nonPublic: true)!);
        return invoker.Handle(serviceProvider, request, cancellationToken);
    }

    public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        var invoker = (IResponseInvoker<TResponse>)ResponseInvokers.GetOrAdd((request.GetType(), typeof(TResponse)), key =>
            Activator.CreateInstance(typeof(ResponseInvoker<,>).MakeGenericType(key.Request, key.Response), nonPublic: true)!);
        return invoker.Handle(serviceProvider, request, cancellationToken);
    }

    private static THandler Resolve<THandler>(IServiceProvider provider, Type requestType)
    {
        var handlers = provider.GetServices<THandler>().ToArray();
        return handlers.Length switch
        {
            1 => handlers[0],
            0 => throw new InvalidOperationException($"Nenhum handler registrado para {requestType.FullName}."),
            _ => throw new InvalidOperationException($"Mais de um handler registrado para {requestType.FullName}.")
        };
    }

    // O cache guarda apenas invocadores sem estado. Os handlers sao resolvidos no escopo atual a cada envio.
    // A chamada tipada a Handle preserva as excecoes originais, sem MethodInfo.Invoke ou dynamic.
    private interface IVoidInvoker
    {
        Task Handle(IServiceProvider provider, IRequest request, CancellationToken cancellationToken);
    }

    private interface IResponseInvoker<TResponse>
    {
        Task<TResponse> Handle(IServiceProvider provider, IRequest<TResponse> request, CancellationToken cancellationToken);
    }

    private sealed class VoidInvoker<TRequest> : IVoidInvoker where TRequest : IRequest
    {
        public Task Handle(IServiceProvider provider, IRequest request, CancellationToken cancellationToken)
            => Resolve<IRequestHandler<TRequest>>(provider, typeof(TRequest)).Handle((TRequest)request, cancellationToken);
    }

    private sealed class ResponseInvoker<TRequest, TResponse> : IResponseInvoker<TResponse> where TRequest : IRequest<TResponse>
    {
        public Task<TResponse> Handle(IServiceProvider provider, IRequest<TResponse> request, CancellationToken cancellationToken)
            => Resolve<IRequestHandler<TRequest, TResponse>>(provider, typeof(TRequest)).Handle((TRequest)request, cancellationToken);
    }
}

