namespace HbaExpress.Dispatch.Application.Jobs;

/// <summary>
/// Ce que la marketplace attend sur <c>POST /api/delivery/courses/completed</c>.
///
/// Type À NOUS, comme les charges entrantes : il a la forme de sa route, il vit ici, et
/// le jour où elle change, la correction tient dans ce fichier.
/// </summary>
/// <param name="DeliveryJobId">
/// LA CLÉ D'IDEMPOTENCE côté marketplace : deux annonces portant le même identifiant ne
/// paient qu'une fois. C'est elle qui rend le réessai du drain sans danger.
/// </param>
public sealed record CourierDeliveryCompletedPayload(
    Guid CourierId,
    Guid OrderId,
    Guid DeliveryJobId,
    decimal Amount,
    string Currency);
