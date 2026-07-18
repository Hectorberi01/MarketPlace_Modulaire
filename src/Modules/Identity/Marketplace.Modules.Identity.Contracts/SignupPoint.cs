namespace Marketplace.Modules.Identity.Contracts;

/// <summary>
/// Un point de la courbe d'évolution des inscriptions : un jour (UTC) et le nombre
/// de comptes créés ce jour-là. Consommé par la console (KPI métier).
/// </summary>
public sealed record SignupPoint(DateTime Date, int Count);
