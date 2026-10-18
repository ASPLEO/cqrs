using System.Threading;
using System.Threading.Tasks;

namespace Aspleo.Cqrs;

public interface IDispatcher
{
    Task Send(IRequest request, CancellationToken cancellationToken = default);
    Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default);
}

