using System.Text.Json;

namespace CunaServer.Data;

/// <summary>
/// Implementación de <see cref="IFamilyAccountRepository"/> respaldada por Supabase PostgREST
/// mediante <see cref="PostgrestClientHelper"/> y la clave secreta del servidor.
/// </summary>
public sealed class PostgrestFamilyAccountRepository : IFamilyAccountRepository
{
    private const int MaxActiveProfilesPerFamily = 6;
    private const int MaxVerificationAttempts = 5;

    private readonly PostgrestClientHelper _client;

    /// <summary>
    /// Inicializa el repositorio PostgREST configurando el cliente HTTP con <c>SUPABASE_URL</c> y <c>SUPABASE_SECRET_KEY</c>.
    /// </summary>
    public PostgrestFamilyAccountRepository(HttpClient httpClient, IConfiguration configuration)
    {
        _client = new PostgrestClientHelper(httpClient, configuration);
    }

    #region Familias y membresías (RF21, RF22)

    /// <inheritdoc />
    public async Task<UserMembership?> GetUserMembershipAsync(Guid userId, CancellationToken ct)
    {
        var dto = await _client.GetFirstOrDefaultAsync<UserMembershipDto>(
            $"usuarios?id=eq.{userId}&select=id,family_id,rol,email,creado_en&limit=1",
            ct);
        return dto?.ToModel();
    }

    /// <inheritdoc />
    public async Task<FamilyRecord?> GetFamilyAsync(Guid familyId, CancellationToken ct)
    {
        var dto = await _client.GetFirstOrDefaultAsync<FamilyDto>(
            $"familias?id=eq.{familyId}&select=id,nombre,creado_en&limit=1",
            ct);
        return dto?.ToModel();
    }

    /// <inheritdoc />
    public async Task<FamilyRecord> CreateFamilyWithPrincipalAsync(
        Guid userId,
        string email,
        string familyName,
        string initialProfileName,
        string initialAvatar,
        CancellationToken ct)
    {
        var existing = await GetUserMembershipAsync(userId, ct);
        if (existing is not null)
        {
            var existingFamily = await GetFamilyAsync(existing.FamilyId, ct);
            if (existingFamily is not null)
            {
                return existingFamily;
            }
        }

        var familyDto = await _client.PostAndReadSingleAsync<object, FamilyDto>(
            "familias",
            new { nombre = familyName },
            ct);
        var family = familyDto.ToModel();

        await _client.PostAsync(
            "usuarios",
            new
            {
                id = userId,
                family_id = family.Id,
                rol = UserRoles.Principal,
                email
            },
            ct);

        await CreateProfileAsync(family.Id, initialProfileName, initialAvatar, ct);
        return family;
    }

    /// <inheritdoc />
    public async Task<FamilyRecord?> UpdateFamilyNameAsync(Guid familyId, string newName, CancellationToken ct)
    {
        var dto = await _client.PatchAndReadFirstAsync<object, FamilyDto>(
            $"familias?id=eq.{familyId}",
            new { nombre = newName },
            ct);
        return dto?.ToModel();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<UserMembership>> ListFamilyMembersAsync(Guid familyId, CancellationToken ct)
    {
        var rows = await _client.GetListAsync<UserMembershipDto>(
            $"usuarios?family_id=eq.{familyId}&select=id,family_id,rol,email,creado_en&order=creado_en.asc",
            ct);
        return rows.Select(r => r.ToModel()).ToList();
    }

    /// <inheritdoc />
    public async Task<bool> RevokeSecondaryMemberAsync(Guid familyId, Guid memberUserId, CancellationToken ct)
    {
        var deleted = await _client.DeleteAndReadListAsync<UserMembershipDto>(
            $"usuarios?id=eq.{memberUserId}&family_id=eq.{familyId}&rol=eq.{UserRoles.Secundario}",
            ct);
        return deleted.Count > 0;
    }

    #endregion

    #region Perfiles de cuidador (RF23)

    /// <inheritdoc />
    public async Task<IReadOnlyList<CaregiverProfileRecord>> ListActiveProfilesAsync(Guid familyId, CancellationToken ct)
    {
        var rows = await _client.GetListAsync<CaregiverProfileDto>(
            $"perfiles_cuidador?family_id=eq.{familyId}&activo=is.true&select=id,family_id,nombre,avatar,activo,eliminado_en,creado_en&order=creado_en.asc",
            ct);
        return rows.Select(r => r.ToModel()).ToList();
    }

    /// <inheritdoc />
    public async Task<CaregiverProfileRecord?> GetProfileAsync(Guid profileId, CancellationToken ct)
    {
        var dto = await _client.GetFirstOrDefaultAsync<CaregiverProfileDto>(
            $"perfiles_cuidador?id=eq.{profileId}&select=id,family_id,nombre,avatar,activo,eliminado_en,creado_en&limit=1",
            ct);
        return dto?.ToModel();
    }

    /// <inheritdoc />
    public async Task<CaregiverProfileRecord> CreateProfileAsync(Guid familyId, string nombre, string avatar, CancellationToken ct)
    {
        var active = await ListActiveProfilesAsync(familyId, ct);
        if (active.Count >= MaxActiveProfilesPerFamily)
        {
            throw new InvalidOperationException("RF23: No se permiten más de 6 perfiles de cuidador activos por familia.");
        }

        var created = await _client.PostAndReadSingleAsync<object, CaregiverProfileDto>(
            "perfiles_cuidador",
            new
            {
                family_id = familyId,
                nombre,
                avatar,
                activo = true
            },
            ct);
        return created.ToModel();
    }

    /// <inheritdoc />
    public async Task<CaregiverProfileRecord?> UpdateProfileAsync(
        Guid familyId,
        Guid profileId,
        string nombre,
        string avatar,
        CancellationToken ct)
    {
        var dto = await _client.PatchAndReadFirstAsync<object, CaregiverProfileDto>(
            $"perfiles_cuidador?id=eq.{profileId}&family_id=eq.{familyId}&activo=is.true",
            new { nombre, avatar },
            ct);
        return dto?.ToModel();
    }

    /// <inheritdoc />
    public async Task<bool> SoftDeleteProfileAsync(Guid familyId, Guid profileId, CancellationToken ct)
    {
        var rows = await _client.PatchAndReadListAsync<object, CaregiverProfileDto>(
            $"perfiles_cuidador?id=eq.{profileId}&family_id=eq.{familyId}&activo=is.true",
            new
            {
                activo = false,
                eliminado_en = DateTimeOffset.UtcNow
            },
            ct);
        return rows.Count > 0;
    }

    #endregion

    #region Invitaciones y auditoría de seguridad (RF22)

    /// <inheritdoc />
    public async Task<InvitationRecord> CreateInvitationAsync(
        Guid familyId,
        Guid createdByUserId,
        string hashCodigo,
        DateTimeOffset expiraEn,
        CancellationToken ct)
    {
        var created = await _client.PostAndReadSingleAsync<object, InvitationDto>(
            "invitaciones",
            new
            {
                family_id = familyId,
                creado_por = createdByUserId,
                hash_codigo = hashCodigo,
                expira_en = expiraEn,
                intentos = 0,
                usada = false
            },
            ct);
        return created.ToModel();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<InvitationRecord>> ListInvitationsAsync(Guid familyId, CancellationToken ct)
    {
        var rows = await _client.GetListAsync<InvitationDto>(
            $"invitaciones?family_id=eq.{familyId}&select=id,family_id,creado_por,hash_codigo,expira_en,intentos,usada,usada_por,usada_en,creado_en&order=creado_en.desc",
            ct);
        return rows.Select(r => r.ToModel()).ToList();
    }

    /// <inheritdoc />
    public async Task<InvitationRecord?> GetInvitationByHashAsync(string hashCodigo, CancellationToken ct)
    {
        var encodedHash = Uri.EscapeDataString(hashCodigo);
        var dto = await _client.GetFirstOrDefaultAsync<InvitationDto>(
            $"invitaciones?hash_codigo=eq.{encodedHash}&select=id,family_id,creado_por,hash_codigo,expira_en,intentos,usada,usada_por,usada_en,creado_en&limit=1",
            ct);
        return dto?.ToModel();
    }

    /// <inheritdoc />
    public async Task IncrementInvitationAttemptAsync(Guid invitationId, CancellationToken ct)
    {
        var current = await _client.GetFirstOrDefaultAsync<InvitationDto>(
            $"invitaciones?id=eq.{invitationId}&select=id,family_id,creado_por,hash_codigo,expira_en,intentos,usada,usada_por,usada_en,creado_en&limit=1",
            ct);
        if (current is null || current.Intentos >= MaxVerificationAttempts)
        {
            return;
        }

        await _client.PatchAsync(
            $"invitaciones?id=eq.{invitationId}",
            new { intentos = current.Intentos + 1 },
            ct);
    }

    /// <inheritdoc />
    public async Task<UserMembership> AcceptInvitationAsync(
        Guid invitationId,
        Guid familyId,
        Guid userId,
        string email,
        string? profileName,
        string? avatar,
        CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        await _client.PatchAsync(
            $"invitaciones?id=eq.{invitationId}&usada=is.false",
            new
            {
                usada = true,
                usada_por = userId,
                usada_en = now
            },
            ct);

        var created = await _client.PostAndReadSingleAsync<object, UserMembershipDto>(
            "usuarios",
            new
            {
                id = userId,
                family_id = familyId,
                rol = UserRoles.Secundario,
                email
            },
            ct);

        await TryCreateOptionalInitialProfileAsync(familyId, profileName, avatar, ct);
        return created.ToModel();
    }

    /// <inheritdoc />
    public Task LogSecurityEventAsync(SecurityAuditEntry entry, CancellationToken ct) =>
        _client.LogSecurityEventAsync(entry, ct);

    /// <inheritdoc />
    public async Task<IReadOnlyList<SecurityAuditEntry>> ListSecurityEventsAsync(Guid familyId, CancellationToken ct)
    {
        var rows = await _client.GetListAsync<SecurityAuditDto>(
            $"bitacora_seguridad?family_id=eq.{familyId}&select=family_id,usuario_id,dispositivo_id,evento,ip_origen,detalle,ts&order=ts.desc&limit=50",
            ct);
        return rows.Select(r => new SecurityAuditEntry(
            r.FamilyId,
            r.UsuarioId,
            r.DispositivoId,
            r.Evento,
            r.IpOrigen,
            ExtractSecurityDetail(r.Detalle),
            r.Ts)).ToList();
    }

    #endregion

    #region Telegram y recordatorios de alimentación (RF18, RF19, RF38, RF39)

    /// <inheritdoc />
    public async Task<TelegramLinkRecord?> GetTelegramLinkByUserAsync(Guid userId, CancellationToken ct)
    {
        var dto = await _client.GetFirstOrDefaultAsync<TelegramLinkDto>(
            $"telegram_vinculos?usuario_id=eq.{userId}&limit=1",
            ct);
        return dto?.ToModel();
    }

    /// <inheritdoc />
    public async Task<TelegramLinkRecord?> GetTelegramLinkByChatIdAsync(long chatId, CancellationToken ct)
    {
        var dto = await _client.GetFirstOrDefaultAsync<TelegramLinkDto>(
            $"telegram_vinculos?chat_id=eq.{chatId}&limit=1",
            ct);
        return dto?.ToModel();
    }

    /// <inheritdoc />
    public async Task<TelegramLinkRecord> SaveTelegramLinkCodeAsync(
        Guid userId,
        string hashCodigo,
        DateTimeOffset expiraEn,
        CancellationToken ct)
    {
        var existing = await GetTelegramLinkByUserAsync(userId, ct);
        if (existing is not null)
        {
            var updated = await _client.PatchAndReadSingleAsync<object, TelegramLinkDto>(
                $"telegram_vinculos?usuario_id=eq.{userId}",
                new
                {
                    hash_codigo = hashCodigo,
                    codigo_expira_en = expiraEn,
                    codigo_usado = false,
                    intentos = 0
                },
                ct);
            return updated.ToModel();
        }

        var created = await _client.PostAndReadSingleAsync<object, TelegramLinkDto>(
            "telegram_vinculos",
            new
            {
                usuario_id = userId,
                hash_codigo = hashCodigo,
                codigo_expira_en = expiraEn,
                codigo_usado = false,
                intentos = 0
            },
            ct);
        return created.ToModel();
    }

    /// <inheritdoc />
    public async Task<TelegramLinkRecord?> CompleteTelegramLinkByCodeAsync(
        string candidateHash,
        long chatId,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var rows = await _client.GetListAsync<TelegramLinkDto>(
            "telegram_vinculos?codigo_usado=is.false&intentos=lt.5",
            ct);

        var match = rows
            .Where(r => !string.IsNullOrWhiteSpace(r.HashCodigo) && r.CodigoExpiraEn.HasValue && now < r.CodigoExpiraEn.Value)
            .FirstOrDefault(r => CryptoTokenHelper.ConstantTimeHashEquals(r.HashCodigo!, candidateHash));

        if (match is null)
        {
            return null;
        }

        // Evitar conflicto con restricción UNIQUE(chat_id) si ese Telegram ya estaba vinculado a otro usuario
        await _client.PatchAsync(
            $"telegram_vinculos?chat_id=eq.{chatId}&id=neq.{match.Id}",
            new
            {
                chat_id = (long?)null,
                vinculado_en = (DateTimeOffset?)null
            },
            ct,
            ensureSuccess: false);

        var updated = await _client.PatchAndReadFirstAsync<object, TelegramLinkDto>(
            $"telegram_vinculos?id=eq.{match.Id}",
            new
            {
                chat_id = chatId,
                hash_codigo = (string?)null,
                codigo_usado = true,
                vinculado_en = now
            },
            ct);
        return updated?.ToModel();
    }

    /// <inheritdoc />
    public Task<bool> DeleteTelegramLinkByUserAsync(Guid userId, CancellationToken ct) =>
        _client.DeleteAsync($"telegram_vinculos?usuario_id=eq.{userId}", ct);

    /// <inheritdoc />
    public async Task<IReadOnlyList<long>> ListLinkedTelegramChatsByFamilyAsync(Guid familyId, CancellationToken ct)
    {
        var members = await ListFamilyMembersAsync(familyId, ct);
        if (members.Count == 0)
        {
            return Array.Empty<long>();
        }

        var idsFilter = string.Join(",", members.Select(m => m.Id));
        var rows = await _client.GetListAsync<TelegramChatIdDto>(
            $"telegram_vinculos?usuario_id=in.({idsFilter})&chat_id=not.is.null&select=chat_id",
            ct);

        return rows
            .Where(r => r.ChatId.HasValue)
            .Select(r => r.ChatId!.Value)
            .Distinct()
            .ToList();
    }

    /// <inheritdoc />
    public async Task<FeedingReminderRecord> GetOrCreateFeedingReminderAsync(Guid familyId, CancellationToken ct)
    {
        var existing = await _client.GetFirstOrDefaultAsync<FeedingReminderDto>(
            $"recordatorios?family_id=eq.{familyId}&limit=1",
            ct);
        if (existing is not null)
        {
            return existing.ToModel();
        }

        var created = await _client.PostAndReadSingleAsync<object, FeedingReminderDto>(
            "recordatorios",
            new
            {
                family_id = familyId,
                intervalo_h = 3,
                activo = true
            },
            ct);
        return created.ToModel();
    }

    /// <inheritdoc />
    public async Task<FeedingReminderRecord> UpdateFeedingReminderAsync(
        Guid familyId,
        int intervaloHoras,
        bool activo,
        DateTimeOffset? ultimoEnvio,
        CancellationToken ct)
    {
        _ = await GetOrCreateFeedingReminderAsync(familyId, ct);

        var updated = await _client.PatchAndReadSingleAsync<object, FeedingReminderDto>(
            $"recordatorios?family_id=eq.{familyId}",
            new
            {
                intervalo_h = intervaloHoras,
                activo,
                ultimo_envio = ultimoEnvio,
                actualizado_en = DateTimeOffset.UtcNow
            },
            ct);
        return updated.ToModel();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<FeedingReminderRecord>> ListActiveFeedingRemindersAsync(CancellationToken ct)
    {
        var rows = await _client.GetListAsync<FeedingReminderDto>("recordatorios?activo=is.true", ct);
        return rows.Select(r => r.ToModel()).ToList();
    }

    #endregion

    #region Helpers privados y DTOs PostgREST

    private async Task TryCreateOptionalInitialProfileAsync(
        Guid familyId,
        string? profileName,
        string? avatar,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(profileName))
        {
            return;
        }

        var active = await ListActiveProfilesAsync(familyId, ct);
        if (active.Count >= MaxActiveProfilesPerFamily)
        {
            return;
        }

        var trimmed = profileName.Trim();
        if (trimmed.Length is >= 1 and <= 30)
        {
            await CreateProfileAsync(
                familyId,
                trimmed,
                string.IsNullOrWhiteSpace(avatar) ? "default" : avatar.Trim(),
                ct);
        }
    }

    private static string? ExtractSecurityDetail(JsonElement? detalle)
    {
        if (!detalle.HasValue || detalle.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        if (detalle.Value.ValueKind == JsonValueKind.Object &&
            detalle.Value.TryGetProperty("info", out var infoProp))
        {
            return infoProp.ValueKind == JsonValueKind.String ? infoProp.GetString() : infoProp.GetRawText();
        }

        return detalle.Value.ValueKind == JsonValueKind.String
            ? detalle.Value.GetString()
            : detalle.Value.GetRawText();
    }

    private sealed record FamilyDto(Guid Id, string Nombre, DateTimeOffset CreadoEn)
    {
        public FamilyRecord ToModel() => new(Id, Nombre, CreadoEn);
    }

    private sealed record UserMembershipDto(Guid Id, Guid FamilyId, string Rol, string Email, DateTimeOffset CreadoEn)
    {
        public UserMembership ToModel() => new(Id, FamilyId, Rol, Email, CreadoEn);
    }

    private sealed record CaregiverProfileDto(
        Guid Id,
        Guid FamilyId,
        string Nombre,
        string Avatar,
        bool Activo,
        DateTimeOffset? EliminadoEn,
        DateTimeOffset CreadoEn)
    {
        public CaregiverProfileRecord ToModel() => new(Id, FamilyId, Nombre, Avatar, Activo, EliminadoEn, CreadoEn);
    }

    private sealed record InvitationDto(
        Guid Id,
        Guid FamilyId,
        Guid? CreadoPor,
        string HashCodigo,
        DateTimeOffset ExpiraEn,
        int Intentos,
        bool Usada,
        Guid? UsadaPor,
        DateTimeOffset? UsadaEn,
        DateTimeOffset CreadoEn)
    {
        public InvitationRecord ToModel() => new(
            Id, FamilyId, CreadoPor, HashCodigo, ExpiraEn, Intentos, Usada, UsadaPor, UsadaEn, CreadoEn);
    }

    private sealed record SecurityAuditDto(
        Guid? FamilyId,
        Guid? UsuarioId,
        Guid? DispositivoId,
        string Evento,
        string? IpOrigen,
        JsonElement? Detalle,
        DateTimeOffset Ts);

    private sealed record TelegramChatIdDto(long? ChatId);

    private sealed record TelegramLinkDto(
        Guid Id,
        Guid UsuarioId,
        long? ChatId,
        string? HashCodigo,
        DateTimeOffset? CodigoExpiraEn,
        bool CodigoUsado,
        int Intentos,
        DateTimeOffset? VinculadoEn,
        DateTimeOffset CreadoEn)
    {
        public TelegramLinkRecord ToModel() => new(
            Id, UsuarioId, ChatId, HashCodigo, CodigoExpiraEn, CodigoUsado, Intentos, VinculadoEn, CreadoEn);
    }

    private sealed record FeedingReminderDto(
        Guid Id,
        Guid FamilyId,
        int IntervaloH,
        bool Activo,
        DateTimeOffset? UltimoEnvio,
        DateTimeOffset ActualizadoEn)
    {
        public FeedingReminderRecord ToModel() => new(
            Id, FamilyId, IntervaloH, Activo, UltimoEnvio, ActualizadoEn);
    }

    #endregion
}
