using FluentValidation;
using Marketplace.BuildingBlocks.Application.Messaging;
using Marketplace.BuildingBlocks.Domain.Results;
using Marketplace.Modules.Tax.Application.Abstractions;
using Marketplace.Modules.Tax.Contracts;
using Marketplace.Modules.Tax.Domain.Taxes;

namespace Marketplace.Modules.Tax.Application.Taxes;

/// <summary>Crée une règle fiscale (juridiction, catégorie optionnelle, taux, type).</summary>
public sealed record CreateTaxRuleCommand(string Jurisdiction, Guid? CategoryId, decimal Rate, string Type, DateTime? EffectiveFromUtc) : ICommand<Guid>;

/// <summary>Modifie une règle fiscale existante.</summary>
public sealed record UpdateTaxRuleCommand(Guid RuleId, string Jurisdiction, Guid? CategoryId, decimal Rate, string Type, DateTime? EffectiveFromUtc) : ICommand;

/// <summary>Désactive une règle fiscale.</summary>
public sealed record DeactivateTaxRuleCommand(Guid RuleId) : ICommand;

/// <summary>Réactive une règle fiscale désactivée.</summary>
public sealed record ReactivateTaxRuleCommand(Guid RuleId) : ICommand;

/// <summary>Supprime définitivement une règle fiscale.</summary>
public sealed record DeleteTaxRuleCommand(Guid RuleId) : ICommand;

/// <summary>Liste les règles fiscales (back-office).</summary>
public sealed record ListTaxRulesQuery : IQuery<IReadOnlyList<TaxRuleSummary>>;

/// <summary>Calcule la taxe applicable à un montant (aperçu).</summary>
public sealed record ComputeTaxQuery(string Jurisdiction, Guid CategoryId, decimal Amount) : IQuery<TaxResult>;

public sealed class CreateTaxRuleCommandValidator : AbstractValidator<CreateTaxRuleCommand>
{
    public CreateTaxRuleCommandValidator()
    {
        RuleFor(c => c.Jurisdiction).NotEmpty().MaximumLength(20);

        // ═════════════════════════════════════════════════════════════════════════
        // MÊME RÈGLE QU'À LA MODIFICATION, ET ELLE NE L'ÉTAIT PAS.
        //
        // Ce contrôle comparait trois chaînes en respectant la casse, là où le
        // gestionnaire d'édition, lui, passe par `Enum.TryParse(ignoreCase: true)`.
        // « VAT » était donc ACCEPTÉ en modification et REFUSÉ en création : la même
        // saisie était valide ou non selon le bouton, et le message — « Type de taxe
        // invalide. » — ne disait ni les valeurs admises, ni que la casse comptait.
        //
        // Côté console, le champ était libre et proposait pour exemple « TVA, VAT… »,
        // c'est-à-dire deux valeurs toutes deux refusées : aucune règle de taxe ne
        // pouvait être créée depuis l'administration. L'écran offre désormais une
        // liste fermée ; ce contrôle reste le rempart, aligné sur son jumeau.
        // ═════════════════════════════════════════════════════════════════════════
        // `Enum.IsDefined` EN PLUS DE `TryParse`, et ce n'est pas de la ceinture :
        // `TryParse` réussit sur une valeur NUMÉRIQUE, membre ou non. « 9 » passait donc,
        // et se persistait tel quel — la colonne est convertie en chaîne — pour se relire
        // ensuite sans erreur. `ComputeTax` ne teste que l'égalité à `Exempt` : une règle
        // « 9 » se serait comportée comme une TVA, définitivement, sans que rien ne le
        // signale. Le message promettait trois valeurs ; il en impose désormais trois.
        RuleFor(c => c.Type)
            .Must(v => Enum.TryParse<TaxType>(v, ignoreCase: true, out var t) && Enum.IsDefined(t))
            .WithMessage("Type de taxe invalide : attendu « Vat », « SalesTax » ou « Exempt ».");

        RuleFor(c => c.Rate).InclusiveBetween(0m, 1m);
    }
}

internal static class TaxMapper
{
    public static TaxRuleSummary ToSummary(TaxRule r) => new(
        r.Id.Value, r.Jurisdiction, r.CategoryId, r.Rate, r.Type.ToString(), r.EffectiveFromUtc, r.IsActive);
}

internal sealed class CreateTaxRuleCommandHandler : ICommandHandler<CreateTaxRuleCommand, Guid>
{
    private readonly ITaxRuleRepository _repository;
    private readonly ITaxUnitOfWork _unitOfWork;

    public CreateTaxRuleCommandHandler(ITaxRuleRepository repository, ITaxUnitOfWork unitOfWork)
    {
        _repository = repository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<Guid>> Handle(CreateTaxRuleCommand command, CancellationToken cancellationToken)
    {
        // `IsDefined` : sans lui, « 9 » se parse en `(TaxType)9` et s'enregistre.
        if (!Enum.TryParse<TaxType>(command.Type, ignoreCase: true, out var type) || !Enum.IsDefined(type))
        {
            return Result.Failure<Guid>(Error.Validation("tax.type_invalid", "Type de taxe inconnu."));
        }

        var result = TaxRule.Create(command.Jurisdiction, command.CategoryId, command.Rate, type, command.EffectiveFromUtc ?? DateTime.UtcNow);
        if (result.IsFailure)
        {
            return Result.Failure<Guid>(result.Error);
        }

        await _repository.AddAsync(result.Value, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return result.Value.Id.Value;
    }
}

internal sealed class DeactivateTaxRuleCommandHandler : ICommandHandler<DeactivateTaxRuleCommand>
{
    private readonly ITaxRuleRepository _repository;
    private readonly ITaxUnitOfWork _unitOfWork;

    public DeactivateTaxRuleCommandHandler(ITaxRuleRepository repository, ITaxUnitOfWork unitOfWork)
    {
        _repository = repository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(DeactivateTaxRuleCommand command, CancellationToken cancellationToken)
    {
        var rule = await _repository.GetByIdAsync(new TaxRuleId(command.RuleId), cancellationToken);
        if (rule is null)
        {
            return Result.Failure(Error.NotFound("tax.rule.not_found", "Règle fiscale introuvable."));
        }

        rule.Deactivate();
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

internal sealed class UpdateTaxRuleCommandHandler : ICommandHandler<UpdateTaxRuleCommand>
{
    private readonly ITaxRuleRepository _repository;
    private readonly ITaxUnitOfWork _unitOfWork;

    public UpdateTaxRuleCommandHandler(ITaxRuleRepository repository, ITaxUnitOfWork unitOfWork)
    {
        _repository = repository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(UpdateTaxRuleCommand command, CancellationToken cancellationToken)
    {
        // `IsDefined` : sans lui, « 9 » se parse en `(TaxType)9` et s'enregistre.
        if (!Enum.TryParse<TaxType>(command.Type, ignoreCase: true, out var type) || !Enum.IsDefined(type))
        {
            return Result.Failure(Error.Validation("tax.type_invalid", "Type de taxe inconnu."));
        }

        var rule = await _repository.GetByIdAsync(new TaxRuleId(command.RuleId), cancellationToken);
        if (rule is null)
        {
            return Result.Failure(Error.NotFound("tax.rule.not_found", "Règle fiscale introuvable."));
        }

        var result = rule.Update(command.Jurisdiction, command.CategoryId, command.Rate, type, command.EffectiveFromUtc ?? rule.EffectiveFromUtc);
        if (result.IsFailure)
        {
            return result;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

internal sealed class ReactivateTaxRuleCommandHandler : ICommandHandler<ReactivateTaxRuleCommand>
{
    private readonly ITaxRuleRepository _repository;
    private readonly ITaxUnitOfWork _unitOfWork;

    public ReactivateTaxRuleCommandHandler(ITaxRuleRepository repository, ITaxUnitOfWork unitOfWork)
    {
        _repository = repository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(ReactivateTaxRuleCommand command, CancellationToken cancellationToken)
    {
        var rule = await _repository.GetByIdAsync(new TaxRuleId(command.RuleId), cancellationToken);
        if (rule is null)
        {
            return Result.Failure(Error.NotFound("tax.rule.not_found", "Règle fiscale introuvable."));
        }

        rule.Reactivate();
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

internal sealed class DeleteTaxRuleCommandHandler : ICommandHandler<DeleteTaxRuleCommand>
{
    private readonly ITaxRuleRepository _repository;
    private readonly ITaxUnitOfWork _unitOfWork;

    public DeleteTaxRuleCommandHandler(ITaxRuleRepository repository, ITaxUnitOfWork unitOfWork)
    {
        _repository = repository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(DeleteTaxRuleCommand command, CancellationToken cancellationToken)
    {
        var rule = await _repository.GetByIdAsync(new TaxRuleId(command.RuleId), cancellationToken);
        if (rule is null)
        {
            return Result.Failure(Error.NotFound("tax.rule.not_found", "Règle fiscale introuvable."));
        }

        _repository.Remove(rule);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

internal sealed class ListTaxRulesQueryHandler : IQueryHandler<ListTaxRulesQuery, IReadOnlyList<TaxRuleSummary>>
{
    private readonly ITaxRuleRepository _repository;
    public ListTaxRulesQueryHandler(ITaxRuleRepository repository) => _repository = repository;

    public async Task<Result<IReadOnlyList<TaxRuleSummary>>> Handle(ListTaxRulesQuery query, CancellationToken cancellationToken)
    {
        var rules = await _repository.ListAsync(cancellationToken);
        IReadOnlyList<TaxRuleSummary> summaries = rules.Select(TaxMapper.ToSummary).ToList();
        return Result.Success(summaries);
    }
}

internal sealed class ComputeTaxQueryHandler : IQueryHandler<ComputeTaxQuery, TaxResult>
{
    private readonly ITaxRuleRepository _repository;
    public ComputeTaxQueryHandler(ITaxRuleRepository repository) => _repository = repository;

    public async Task<Result<TaxResult>> Handle(ComputeTaxQuery query, CancellationToken cancellationToken)
    {
        var candidates = await _repository.GetCandidatesAsync(query.Jurisdiction, query.CategoryId, cancellationToken);
        var rule = TaxResolver.Resolve(candidates, query.Jurisdiction, query.CategoryId, DateTime.UtcNow);

        var tax = rule?.ComputeTax(query.Amount) ?? 0m;
        var type = rule?.Type.ToString() ?? "None";
        return new TaxResult(query.Amount, tax, query.Amount + tax, type, rule?.Id.Value);
    }
}
