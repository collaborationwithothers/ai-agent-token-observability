using Npgsql;
using TokenObservability.Domain.Tenancy;

namespace TokenObservability.Infrastructure.Persistence;

public sealed class PostgreSqlProductApiIdempotencyStore(
    NpgsqlDataSource dataSource,
    ITenantMetadataClock clock) : IProductApiIdempotencyStore
{
    public async Task<ProductApiIdempotencyReservation> ReserveProductApiIdempotencyRecordAsync(
        ReserveProductApiIdempotencyRecordRequest request)
    {
        var now = clock.UtcNow.ToUniversalTime();
        await using var connection = await dataSource.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        await using (var deleteExpired = new NpgsqlCommand("""
            DELETE FROM product_api_idempotency
            WHERE customer_organization_id = @customer_organization_id
              AND product_user_id = @product_user_id
              AND route = @route
              AND idempotency_key = @idempotency_key
              AND expires_at_utc <= @now
            """, connection, transaction))
        {
            deleteExpired.Parameters.AddWithValue("customer_organization_id", request.CustomerOrganizationId.Value);
            deleteExpired.Parameters.AddWithValue("product_user_id", request.ProductUserId.Value);
            deleteExpired.Parameters.AddWithValue("route", request.Route);
            deleteExpired.Parameters.AddWithValue("idempotency_key", request.IdempotencyKey);
            deleteExpired.Parameters.AddWithValue("now", now);
            await deleteExpired.ExecuteNonQueryAsync();
        }

        await using var insert = new NpgsqlCommand("""
            INSERT INTO product_api_idempotency (
                customer_organization_id,
                product_user_id,
                route,
                idempotency_key,
                request_hash,
                operation_id,
                response_status_code,
                response_location,
                response_json,
                created_at_utc,
                expires_at_utc,
                completed_at_utc)
            VALUES (
                @customer_organization_id,
                @product_user_id,
                @route,
                @idempotency_key,
                @request_hash,
                NULL,
                NULL,
                NULL,
                NULL,
                @created_at_utc,
                @expires_at_utc,
                NULL)
            ON CONFLICT (customer_organization_id, product_user_id, route, idempotency_key) DO NOTHING
            """, connection, transaction);
        AddReservationParameters(insert, request, now);
        var inserted = await insert.ExecuteNonQueryAsync();
        if (inserted == 1)
        {
            var reserved = await FindRecordAsync(
                request.CustomerOrganizationId,
                request.ProductUserId,
                request.Route,
                request.IdempotencyKey,
                connection,
                transaction) ??
                throw new InvalidOperationException("Product API idempotency reservation was not persisted.");
            await transaction.CommitAsync();
            return new ProductApiIdempotencyReservation(ProductApiIdempotencyReservationState.Reserved, reserved);
        }

        var existing = await FindRecordAsync(
            request.CustomerOrganizationId,
            request.ProductUserId,
            request.Route,
            request.IdempotencyKey,
            connection,
            transaction);
        if (existing is null)
        {
            await transaction.RollbackAsync();
            throw new InvalidOperationException("Product API idempotency reservation was not acquired.");
        }

        await transaction.CommitAsync();
        if (!StringComparer.Ordinal.Equals(existing.RequestHash, request.RequestHash))
        {
            return new ProductApiIdempotencyReservation(ProductApiIdempotencyReservationState.Conflict, existing);
        }

        return existing.IsCompleted
            ? new ProductApiIdempotencyReservation(ProductApiIdempotencyReservationState.Replay, existing)
            : new ProductApiIdempotencyReservation(ProductApiIdempotencyReservationState.InProgress, existing);
    }

    public async Task<ProductApiIdempotencyRecord> CompleteProductApiIdempotencyRecordAsync(
        CompleteProductApiIdempotencyRecordRequest request)
    {
        var now = clock.UtcNow.ToUniversalTime();
        await using var command = dataSource.CreateCommand("""
            UPDATE product_api_idempotency
            SET operation_id = @operation_id,
                response_status_code = @response_status_code,
                response_location = @response_location,
                response_json = @response_json::jsonb,
                completed_at_utc = @completed_at_utc
            WHERE customer_organization_id = @customer_organization_id
              AND product_user_id = @product_user_id
              AND route = @route
              AND idempotency_key = @idempotency_key
              AND request_hash = @request_hash
              AND completed_at_utc IS NULL
            """);
        command.Parameters.AddWithValue("customer_organization_id", request.CustomerOrganizationId.Value);
        command.Parameters.AddWithValue("product_user_id", request.ProductUserId.Value);
        command.Parameters.AddWithValue("route", request.Route);
        command.Parameters.AddWithValue("idempotency_key", request.IdempotencyKey);
        command.Parameters.AddWithValue("request_hash", request.RequestHash);
        command.Parameters.AddWithValue("operation_id", request.OperationId);
        command.Parameters.AddWithValue("response_status_code", request.ResponseStatusCode);
        command.Parameters.AddWithValue("response_location", (object?)request.ResponseLocation ?? DBNull.Value);
        command.Parameters.AddWithValue("response_json", request.ResponseJson);
        command.Parameters.AddWithValue("completed_at_utc", now);
        var updated = await command.ExecuteNonQueryAsync();
        if (updated != 1)
        {
            throw new InvalidOperationException("Product API idempotency reservation was not completed.");
        }

        return await FindRecordAsync(
                request.CustomerOrganizationId,
                request.ProductUserId,
                request.Route,
                request.IdempotencyKey) ??
            throw new InvalidOperationException("Product API idempotency record was not found after completion.");
    }

    private async Task<ProductApiIdempotencyRecord?> FindRecordAsync(
        CustomerOrganizationId customerOrganizationId,
        ProductUserId productUserId,
        string route,
        string idempotencyKey,
        NpgsqlConnection? connection = null,
        NpgsqlTransaction? transaction = null)
    {
        var command = connection is null
            ? dataSource.CreateCommand("""
                SELECT request_hash, operation_id, response_status_code, response_location, response_json::text, created_at_utc, expires_at_utc, completed_at_utc
                FROM product_api_idempotency
                WHERE customer_organization_id = @customer_organization_id
                  AND product_user_id = @product_user_id
                  AND route = @route
                  AND idempotency_key = @idempotency_key
                  AND expires_at_utc > @now
                """)
            : new NpgsqlCommand("""
            SELECT request_hash, operation_id, response_status_code, response_location, response_json::text, created_at_utc, expires_at_utc, completed_at_utc
            FROM product_api_idempotency
            WHERE customer_organization_id = @customer_organization_id
              AND product_user_id = @product_user_id
              AND route = @route
              AND idempotency_key = @idempotency_key
              AND expires_at_utc > @now
            """, connection, transaction);
        await using var disposableCommand = command;
        command.Parameters.AddWithValue("customer_organization_id", customerOrganizationId.Value);
        command.Parameters.AddWithValue("product_user_id", productUserId.Value);
        command.Parameters.AddWithValue("route", route);
        command.Parameters.AddWithValue("idempotency_key", idempotencyKey);
        command.Parameters.AddWithValue("now", clock.UtcNow.ToUniversalTime());

        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            return null;
        }

        return new ProductApiIdempotencyRecord(
            customerOrganizationId,
            productUserId,
            route,
            idempotencyKey,
            reader.GetString(0),
            reader.IsDBNull(1) ? null : reader.GetString(1),
            reader.IsDBNull(2) ? null : reader.GetInt32(2),
            reader.IsDBNull(3) ? null : reader.GetString(3),
            reader.IsDBNull(4) ? null : reader.GetString(4),
            reader.GetFieldValue<DateTimeOffset>(5),
            reader.GetFieldValue<DateTimeOffset>(6),
            reader.IsDBNull(7) ? null : reader.GetFieldValue<DateTimeOffset>(7));
    }

    private static void AddReservationParameters(
        NpgsqlCommand command,
        ReserveProductApiIdempotencyRecordRequest request,
        DateTimeOffset now)
    {
        command.Parameters.AddWithValue("customer_organization_id", request.CustomerOrganizationId.Value);
        command.Parameters.AddWithValue("product_user_id", request.ProductUserId.Value);
        command.Parameters.AddWithValue("route", request.Route);
        command.Parameters.AddWithValue("idempotency_key", request.IdempotencyKey);
        command.Parameters.AddWithValue("request_hash", request.RequestHash);
        command.Parameters.AddWithValue("created_at_utc", now);
        command.Parameters.AddWithValue("expires_at_utc", request.ExpiresAtUtc.ToUniversalTime());
    }
}
