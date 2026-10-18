namespace Aspleo.Cqrs;

/// <summary>Solicitacao sem valor de retorno.</summary>
public interface IRequest;

/// <summary>Solicitacao com retorno tipado.</summary>
public interface IRequest<TResponse>;

