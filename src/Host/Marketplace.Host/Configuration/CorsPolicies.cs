namespace Marketplace.Host.Configuration;

/// <summary>
/// Noms des trois politiques CORS, une par surface. Elles ne sont PAS fusionnées :
/// sur une origine unique, une liste commune autoriserait la boutique à appeler
/// /api/admin depuis le navigateur.
/// </summary>
public static class CorsPolicies
{
    public const string Client = "client-web";
    public const string Seller = "seller-console";
    public const string Admin = "admin-console";
}
