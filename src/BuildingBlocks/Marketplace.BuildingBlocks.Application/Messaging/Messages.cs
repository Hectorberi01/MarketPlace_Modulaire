using MediatR;
using Marketplace.BuildingBlocks.Domain.Results;

namespace Marketplace.BuildingBlocks.Application.Messaging;

// CQRS au-dessus de MediatR. Les commandes/queries renvoient toujours un Result
// (le pattern Result est constant à toutes les phases — cf. dossier).

/// <summary>Commande sans valeur de retour (effet de bord uniquement).</summary>
public interface ICommand : IRequest<Result>
{
}

/// <summary>Commande renvoyant une valeur (ex : l'Id créé).</summary>
public interface ICommand<TResponse> : IRequest<Result<TResponse>>
{
}

/// <summary>Query : lecture pure, renvoie toujours une valeur.</summary>
public interface IQuery<TResponse> : IRequest<Result<TResponse>>
{
}

public interface ICommandHandler<in TCommand> : IRequestHandler<TCommand, Result>
    where TCommand : ICommand
{
}

public interface ICommandHandler<in TCommand, TResponse> : IRequestHandler<TCommand, Result<TResponse>>
    where TCommand : ICommand<TResponse>
{
}

public interface IQueryHandler<in TQuery, TResponse> : IRequestHandler<TQuery, Result<TResponse>>
    where TQuery : IQuery<TResponse>
{
}
