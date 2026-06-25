using System.Text.Json;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Marketplace.Modules.Sellers.Domain.Sellers;

namespace Marketplace.Modules.Sellers.Infrastructure.Persistence;

/// <summary>Convertit le Value Object PayoutAccount en jsonb.</summary>
internal sealed class PayoutAccountJsonConverter : ValueConverter<PayoutAccount, string>
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public PayoutAccountJsonConverter()
        : base(value => Serialize(value), json => Deserialize(json))
    {
    }

    private static string Serialize(PayoutAccount account)
        => JsonSerializer.Serialize(new Dto((int)account.Provider, account.AccountNumber, account.AccountName), Options);

    private static PayoutAccount Deserialize(string json)
    {
        var dto = JsonSerializer.Deserialize<Dto>(json, Options)!;
        return PayoutAccount.Create((PayoutProvider)dto.Provider, dto.AccountNumber, dto.AccountName).Value;
    }

    private sealed record Dto(int Provider, string AccountNumber, string AccountName);
}
