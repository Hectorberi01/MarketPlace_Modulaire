namespace Marketplace.BuildingBlocks.Infrastructure.Serialization;

/// <summary>
/// Nom de type stable pour la persistance outbox : « FullName, AssemblyName »
/// sans numéro de version, pour survivre aux montées de version d'assembly.
/// </summary>
public static class EventTypeName
{
    public static string Of(Type type) => $"{type.FullName}, {type.Assembly.GetName().Name}";

    public static Type Resolve(string typeName)
        => Type.GetType(typeName)
           ?? throw new InvalidOperationException($"Type d'event introuvable : {typeName}");
}
