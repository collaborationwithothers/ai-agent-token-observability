using TokenObservability.Domain.Tenancy;

namespace TokenObservability.Infrastructure.Persistence;

public interface IProductApiIdempotencyStore
{
    Task<ProductApiIdempotencyReservation> ReserveProductApiIdempotencyRecordAsync(
        ReserveProductApiIdempotencyRecordRequest request);

    Task<ProductApiIdempotencyRecord> CompleteProductApiIdempotencyRecordAsync(
        CompleteProductApiIdempotencyRecordRequest request);
}
