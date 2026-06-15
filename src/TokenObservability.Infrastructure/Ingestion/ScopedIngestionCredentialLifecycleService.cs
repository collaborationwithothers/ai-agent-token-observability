using System.Security.Cryptography;
using System.Text;
using TokenObservability.Domain.Authorization;
using TokenObservability.Domain.Ingestion;
using TokenObservability.Domain.Tenancy;
using TokenObservability.Infrastructure.Persistence;

namespace TokenObservability.Infrastructure.Ingestion;

public sealed class ScopedIngestionCredentialLifecycleService(
    InMemoryTenantMetadataStore tenantMetadataStore,
    ITenantMetadataClock? clock = null)
{
    private const string SecretPrefix = "aito_live_";
    private const int CredentialPrefixLength = 16;
    private readonly ITenantMetadataClock clock = clock ?? new SystemTenantMetadataClock();

    public async Task<IssuedScopedIngestionCredential> CreateAsync(
        CustomerOrganizationId customerOrganizationId,
        IssueScopedIngestionCredentialRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        RequireTenantAdministrator(request.ActorEffectiveRole);

        var secret = CreateSecret();
        var credential = await tenantMetadataStore.CreateScopedIngestionCredentialAsync(
            customerOrganizationId,
            new CreateScopedIngestionCredentialRequest(
                request.HarnessSetupProfileId,
                request.ProductUserId,
                HashSecret(secret),
                CreateCredentialPrefix(secret),
                request.AllowedHarness,
                request.AllowedScopes,
                request.ExpiresAtUtc,
                request.CreatedByProductUserId,
                request.ActorEffectiveRole,
                request.CorrelationId,
                request.AuditEventId));

        return new IssuedScopedIngestionCredential(credential, secret);
    }

    public async Task<IssuedScopedIngestionCredential> RotateAsync(
        CustomerOrganizationId customerOrganizationId,
        ScopedIngestionCredentialId scopedIngestionCredentialId,
        RotateScopedIngestionCredentialCommand request)
    {
        ArgumentNullException.ThrowIfNull(request);
        RequireTenantAdministrator(request.ActorEffectiveRole);

        var secret = CreateSecret();
        var credential = await tenantMetadataStore.RotateScopedIngestionCredentialAsync(
            customerOrganizationId,
            scopedIngestionCredentialId,
            new RotateScopedIngestionCredentialRequest(
                HashSecret(secret),
                CreateCredentialPrefix(secret),
                request.ExpiresAtUtc,
                request.ChangedByProductUserId,
                request.ActorEffectiveRole,
                request.CorrelationId,
                request.AuditEventId));

        return new IssuedScopedIngestionCredential(credential, secret);
    }

    public Task<ScopedIngestionCredential> DisableAsync(
        CustomerOrganizationId customerOrganizationId,
        ScopedIngestionCredentialId scopedIngestionCredentialId,
        ScopedIngestionCredentialLifecycleRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        RequireTenantAdministrator(request.ActorEffectiveRole);

        return tenantMetadataStore.DisableScopedIngestionCredentialAsync(
            customerOrganizationId,
            scopedIngestionCredentialId,
            request);
    }

    public Task<ScopedIngestionCredential> RevokeAsync(
        CustomerOrganizationId customerOrganizationId,
        ScopedIngestionCredentialId scopedIngestionCredentialId,
        ScopedIngestionCredentialLifecycleRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        RequireTenantAdministrator(request.ActorEffectiveRole);

        return tenantMetadataStore.RevokeScopedIngestionCredentialAsync(
            customerOrganizationId,
            scopedIngestionCredentialId,
            request);
    }

    public async Task<ScopedIngestionCredentialValidationResult> ValidateAsync(
        ValidateScopedIngestionCredentialRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var normalizedSecret = NormalizeSecret(request.Secret);
        var declaredHarness = NormalizeRequiredText(request.DeclaredHarness);
        var harnessSetupProfileId = NormalizeRequiredText(request.HarnessSetupProfileId);

        if (normalizedSecret is null ||
            declaredHarness is null ||
            harnessSetupProfileId is null)
        {
            return ScopedIngestionCredentialValidationResult.Denied(
                ScopedIngestionCredentialValidationFailureReason.Malformed);
        }

        var expectedHash = HashSecret(normalizedSecret);
        var credentials = await tenantMetadataStore.ListScopedIngestionCredentialsAsync(request.CustomerOrganizationId);
        var matchedCredentials = credentials
            .Where(candidate => CredentialHashEquals(candidate.CredentialHash, expectedHash))
            .ToArray();
        var credential = matchedCredentials.SingleOrDefault(candidate =>
            candidate.Status == ScopedIngestionCredentialStatus.Active) ?? matchedCredentials.FirstOrDefault();

        if (credential is null)
        {
            return ScopedIngestionCredentialValidationResult.Denied(
                ScopedIngestionCredentialValidationFailureReason.Missing);
        }

        var customerOrganization = await tenantMetadataStore.FindCustomerOrganizationAsync(credential.CustomerOrganizationId);

        if (customerOrganization is null || customerOrganization.Status != CustomerOrganizationStatus.Active)
        {
            await RecordFailedAccessAsync(credential, "invalid_tenant", request.CorrelationId);
            return ScopedIngestionCredentialValidationResult.Denied(
                ScopedIngestionCredentialValidationFailureReason.InvalidTenant);
        }

        if (!StringComparer.Ordinal.Equals(ToWireHarness(credential.AllowedHarness), declaredHarness))
        {
            await RecordFailedAccessAsync(credential, "wrong_harness", request.CorrelationId);
            return ScopedIngestionCredentialValidationResult.Denied(
                ScopedIngestionCredentialValidationFailureReason.WrongHarness);
        }

        if (!StringComparer.Ordinal.Equals(credential.HarnessSetupProfileId, harnessSetupProfileId))
        {
            await RecordFailedAccessAsync(credential, "wrong_harness_profile", request.CorrelationId);
            return ScopedIngestionCredentialValidationResult.Denied(
                ScopedIngestionCredentialValidationFailureReason.WrongHarnessProfile);
        }

        if (credential.Status != ScopedIngestionCredentialStatus.Active)
        {
            var failureReason = credential.Status switch
            {
                ScopedIngestionCredentialStatus.Disabled => ScopedIngestionCredentialValidationFailureReason.Disabled,
                ScopedIngestionCredentialStatus.Revoked => ScopedIngestionCredentialValidationFailureReason.Revoked,
                ScopedIngestionCredentialStatus.Expired => ScopedIngestionCredentialValidationFailureReason.Expired,
                _ => ScopedIngestionCredentialValidationFailureReason.Inactive
            };
            await RecordFailedAccessAsync(credential, ToReasonCode(failureReason), request.CorrelationId);
            return ScopedIngestionCredentialValidationResult.Denied(failureReason);
        }

        if (credential.ExpiresAtUtc <= clock.UtcNow.ToUniversalTime())
        {
            await RecordFailedAccessAsync(credential, "expired", request.CorrelationId);
            return ScopedIngestionCredentialValidationResult.Denied(
                ScopedIngestionCredentialValidationFailureReason.Expired);
        }

        return ScopedIngestionCredentialValidationResult.Allowed(credential);
    }

    public async Task<ScopedIngestionCredentialValidationResult> ValidateForIngestionAsync(
        ValidateScopedIngestionCredentialForIngestionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var normalizedSecret = NormalizeSecret(request.Secret);
        if (normalizedSecret is null)
        {
            return ScopedIngestionCredentialValidationResult.Denied(
                ScopedIngestionCredentialValidationFailureReason.Malformed);
        }

        var declaredHarness = NormalizeRequiredText(request.DeclaredHarness);
        var harnessSetupProfileId = NormalizeRequiredText(request.HarnessSetupProfileId);
        var expectedHash = HashSecret(normalizedSecret);
        var credentials = await tenantMetadataStore.ListScopedIngestionCredentialsForValidationAsync();
        var matchedCredentials = credentials
            .Where(candidate => CredentialHashEquals(candidate.CredentialHash, expectedHash))
            .ToArray();
        var activeCredentials = matchedCredentials
            .Where(candidate => candidate.Status == ScopedIngestionCredentialStatus.Active)
            .ToArray();
        var credential = FindCredentialForIngestionValidation(
            matchedCredentials,
            activeCredentials,
            harnessSetupProfileId);

        if (credential is null)
        {
            return ScopedIngestionCredentialValidationResult.Denied(
                ScopedIngestionCredentialValidationFailureReason.Missing);
        }

        if (activeCredentials.Length > 1)
        {
            await RecordFailedAccessAsync(credential, "ambiguous_credential", request.CorrelationId);
            return ScopedIngestionCredentialValidationResult.Denied(
                ScopedIngestionCredentialValidationFailureReason.Ambiguous);
        }

        var customerOrganization = await tenantMetadataStore.FindCustomerOrganizationAsync(credential.CustomerOrganizationId);

        if (customerOrganization is null || customerOrganization.Status != CustomerOrganizationStatus.Active)
        {
            await RecordFailedAccessAsync(credential, "invalid_tenant", request.CorrelationId);
            return ScopedIngestionCredentialValidationResult.Denied(
                ScopedIngestionCredentialValidationFailureReason.InvalidTenant);
        }

        if (credential.Status != ScopedIngestionCredentialStatus.Active)
        {
            var failureReason = credential.Status switch
            {
                ScopedIngestionCredentialStatus.Disabled => ScopedIngestionCredentialValidationFailureReason.Disabled,
                ScopedIngestionCredentialStatus.Revoked => ScopedIngestionCredentialValidationFailureReason.Revoked,
                ScopedIngestionCredentialStatus.Expired => ScopedIngestionCredentialValidationFailureReason.Expired,
                _ => ScopedIngestionCredentialValidationFailureReason.Inactive
            };
            await RecordFailedAccessAsync(credential, ToReasonCode(failureReason), request.CorrelationId);
            return ScopedIngestionCredentialValidationResult.Denied(failureReason);
        }

        if (credential.ExpiresAtUtc <= clock.UtcNow.ToUniversalTime())
        {
            await RecordFailedAccessAsync(credential, "expired", request.CorrelationId);
            return ScopedIngestionCredentialValidationResult.Denied(
                ScopedIngestionCredentialValidationFailureReason.Expired);
        }

        if (declaredHarness is null || harnessSetupProfileId is null)
        {
            await RecordFailedAccessAsync(credential, "malformed_harness_context", request.CorrelationId);
            return ScopedIngestionCredentialValidationResult.Denied(
                ScopedIngestionCredentialValidationFailureReason.MalformedHarnessContext);
        }

        if (!StringComparer.Ordinal.Equals(ToWireHarness(credential.AllowedHarness), declaredHarness))
        {
            await RecordFailedAccessAsync(credential, "wrong_harness", request.CorrelationId);
            return ScopedIngestionCredentialValidationResult.Denied(
                ScopedIngestionCredentialValidationFailureReason.WrongHarness);
        }

        if (!StringComparer.Ordinal.Equals(credential.HarnessSetupProfileId, harnessSetupProfileId))
        {
            await RecordFailedAccessAsync(credential, "wrong_harness_profile", request.CorrelationId);
            return ScopedIngestionCredentialValidationResult.Denied(
                ScopedIngestionCredentialValidationFailureReason.WrongHarnessProfile);
        }

        return ScopedIngestionCredentialValidationResult.Allowed(credential);
    }

    private static ScopedIngestionCredential? FindCredentialForIngestionValidation(
        IReadOnlyList<ScopedIngestionCredential> matchedCredentials,
        IReadOnlyList<ScopedIngestionCredential> activeCredentials,
        string? harnessSetupProfileId)
    {
        if (activeCredentials.Count == 1)
        {
            return activeCredentials[0];
        }

        if (activeCredentials.Count > 1 && harnessSetupProfileId is not null)
        {
            var profileActiveCredentials = activeCredentials
                .Where(candidate => StringComparer.Ordinal.Equals(candidate.HarnessSetupProfileId, harnessSetupProfileId))
                .ToArray();

            if (profileActiveCredentials.Length == 1)
            {
                return profileActiveCredentials[0];
            }
        }

        return activeCredentials.FirstOrDefault() ?? matchedCredentials.FirstOrDefault();
    }

    private Task RecordFailedAccessAsync(
        ScopedIngestionCredential credential,
        string reasonCode,
        string correlationId)
    {
        return tenantMetadataStore.RecordScopedIngestionCredentialFailedAccessAsync(
            credential.CustomerOrganizationId,
            credential.ScopedIngestionCredentialId,
            reasonCode,
            correlationId);
    }

    private static void RequireTenantAdministrator(ProductRole actorEffectiveRole)
    {
        if (actorEffectiveRole != ProductRole.PlatformAdmin)
        {
            throw new UnauthorizedAccessException("Scoped ingestion credential lifecycle requires tenant administrator privileges.");
        }
    }

    private static string CreateSecret()
    {
        Span<byte> bytes = stackalloc byte[32];
        RandomNumberGenerator.Fill(bytes);
        return SecretPrefix + Convert.ToBase64String(bytes).Replace("+", "-", StringComparison.Ordinal)
            .Replace("/", "_", StringComparison.Ordinal)
            .TrimEnd('=');
    }

    private static string CreateCredentialPrefix(string secret)
    {
        return secret[..Math.Min(CredentialPrefixLength, secret.Length)];
    }

    private static string HashSecret(string secret)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(secret));
        return "sha256:" + Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static bool CredentialHashEquals(string storedCredentialHash, string expectedCredentialHash)
    {
        var storedBytes = Encoding.ASCII.GetBytes(storedCredentialHash);
        var expectedBytes = Encoding.ASCII.GetBytes(expectedCredentialHash);
        return CryptographicOperations.FixedTimeEquals(storedBytes, expectedBytes);
    }

    private static string? NormalizeSecret(string secret)
    {
        if (string.IsNullOrWhiteSpace(secret))
        {
            return null;
        }

        var normalized = secret.Trim();
        return normalized.StartsWith(SecretPrefix, StringComparison.Ordinal) &&
            normalized.Length >= SecretPrefix.Length + 24 &&
            normalized.All(static character =>
                char.IsAsciiLetterOrDigit(character) ||
                character is '_' or '-')
            ? normalized
            : null;
    }

    private static string? NormalizeRequiredText(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static string ToWireHarness(CodingAgentHarness harness)
    {
        return harness switch
        {
            CodingAgentHarness.CodexCli => "codex-cli",
            _ => throw new ArgumentOutOfRangeException(nameof(harness), harness, null)
        };
    }

    private static string ToReasonCode(ScopedIngestionCredentialValidationFailureReason failureReason)
    {
        return failureReason switch
        {
            ScopedIngestionCredentialValidationFailureReason.Disabled => "disabled",
            ScopedIngestionCredentialValidationFailureReason.Revoked => "revoked",
            ScopedIngestionCredentialValidationFailureReason.Expired => "expired",
            ScopedIngestionCredentialValidationFailureReason.Inactive => "inactive",
            ScopedIngestionCredentialValidationFailureReason.WrongHarness => "wrong_harness",
            ScopedIngestionCredentialValidationFailureReason.WrongHarnessProfile => "wrong_harness_profile",
            ScopedIngestionCredentialValidationFailureReason.InvalidTenant => "invalid_tenant",
            ScopedIngestionCredentialValidationFailureReason.MalformedHarnessContext => "malformed_harness_context",
            ScopedIngestionCredentialValidationFailureReason.Ambiguous => "ambiguous_credential",
            ScopedIngestionCredentialValidationFailureReason.Malformed => "malformed",
            ScopedIngestionCredentialValidationFailureReason.Missing => "missing",
            _ => "denied"
        };
    }
}

public sealed record IssueScopedIngestionCredentialRequest(
    string HarnessSetupProfileId,
    ProductUserId ProductUserId,
    CodingAgentHarness AllowedHarness,
    IReadOnlyList<ProductScope> AllowedScopes,
    DateTimeOffset ExpiresAtUtc,
    ProductUserId CreatedByProductUserId,
    ProductRole ActorEffectiveRole,
    string CorrelationId,
    string AuditEventId);

public sealed record RotateScopedIngestionCredentialCommand(
    DateTimeOffset ExpiresAtUtc,
    ProductUserId ChangedByProductUserId,
    ProductRole ActorEffectiveRole,
    string CorrelationId,
    string AuditEventId);

public sealed record ValidateScopedIngestionCredentialRequest(
    CustomerOrganizationId CustomerOrganizationId,
    string Secret,
    string DeclaredHarness,
    string HarnessSetupProfileId,
    string CorrelationId);

public sealed record ValidateScopedIngestionCredentialForIngestionRequest(
    string Secret,
    string DeclaredHarness,
    string HarnessSetupProfileId,
    string CorrelationId);

public sealed record IssuedScopedIngestionCredential(
    ScopedIngestionCredential Credential,
    string Secret);

public sealed record ScopedIngestionCredentialValidationResult(
    bool IsValid,
    ScopedIngestionCredentialValidationFailureReason FailureReason,
    ScopedIngestionCredential? Credential,
    ProductUserId? ProductUserId)
{
    public static ScopedIngestionCredentialValidationResult Allowed(ScopedIngestionCredential credential)
    {
        return new ScopedIngestionCredentialValidationResult(
            IsValid: true,
            ScopedIngestionCredentialValidationFailureReason.None,
            credential,
            credential.ProductUserId);
    }

    public static ScopedIngestionCredentialValidationResult Denied(
        ScopedIngestionCredentialValidationFailureReason failureReason)
    {
        return new ScopedIngestionCredentialValidationResult(
            IsValid: false,
            failureReason,
            Credential: null,
            ProductUserId: null);
    }
}

public enum ScopedIngestionCredentialValidationFailureReason
{
    None,
    Malformed,
    Missing,
    Disabled,
    Revoked,
    Expired,
    Inactive,
    WrongHarness,
    WrongHarnessProfile,
    InvalidTenant,
    MalformedHarnessContext,
    Ambiguous
}
