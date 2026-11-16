using System.Collections.Concurrent;

namespace CunaServer.Data;

/// <summary>
/// Implementación en memoria de <see cref="IFamilyAccountRepository"/> para pruebas automatizadas
/// y modo de desarrollo sin conexión a Supabase.
/// </summary>
public sealed class InMemoryFamilyAccountRepository : IFamilyAccountRepository
{
    private const int MaxActiveProfilesPerFamily = 6;
    private const int MaxVerificationAttempts = 5;

    private readonly ConcurrentDictionary<Guid, FamilyRecord> _families = new();
    private readonly ConcurrentDictionary<Guid, UserMembership> _members = new();
    private readonly ConcurrentDictionary<Guid, CaregiverProfileRecord> _profiles = new();
    private readonly ConcurrentDictionary<Guid, InvitationRecord> _invitations = new();
    private readonly ConcurrentDictionary<Guid, TelegramLinkRecord> _telegramLinks = new();
    private readonly ConcurrentDictionary<Guid, FeedingReminderRecord> _reminders = new();
    private readonly ConcurrentQueue<SecurityAuditEntry> _securityAudit = new();
    private readonly object _sync = new();

    #region Familias y membresías (RF21, RF22)

    /// <inheritdoc />
    public Task<UserMembership?> GetUserMembershipAsync(Guid userId, CancellationToken ct)
    {
        _members.TryGetValue(userId, out var membership);
        return Task.FromResult(membership);
    }

    /// <inheritdoc />
    public Task<FamilyRecord?> GetFamilyAsync(Guid familyId, CancellationToken ct)
    {
        _families.TryGetValue(familyId, out var family);
        return Task.FromResult(family);
    }

    /// <inheritdoc />
    public Task<FamilyRecord> CreateFamilyWithPrincipalAsync(
        Guid userId,
        string email,
        string familyName,
        string initialProfileName,
        string initialAvatar,
        CancellationToken ct)
    {
        lock (_sync)
        {
            if (_members.TryGetValue(userId, out var existing) &&
                _families.TryGetValue(existing.FamilyId, out var existingFamily))
            {
                return Task.FromResult(existingFamily);
            }

            var now = DateTimeOffset.UtcNow;
            var family = new FamilyRecord(Guid.NewGuid(), familyName, now);
            var member = new UserMembership(userId, family.Id, UserRoles.Principal, email, now);
            var profile = new CaregiverProfileRecord(
                Guid.NewGuid(),
                family.Id,
                initialProfileName,
                initialAvatar,
                true,
                null,
                now);

            _families[family.Id] = family;
            _members[userId] = member;
            _profiles[profile.Id] = profile;

            return Task.FromResult(family);
        }
    }

    /// <inheritdoc />
    public Task<FamilyRecord?> UpdateFamilyNameAsync(Guid familyId, string newName, CancellationToken ct)
    {
        lock (_sync)
        {
            if (!_families.TryGetValue(familyId, out var current))
            {
                return Task.FromResult<FamilyRecord?>(null);
            }

            var updated = current with { Nombre = newName };
            _families[familyId] = updated;
            return Task.FromResult<FamilyRecord?>(updated);
        }
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<UserMembership>> ListFamilyMembersAsync(Guid familyId, CancellationToken ct)
    {
        var list = _members.Values
            .Where(m => m.FamilyId == familyId)
            .OrderBy(m => m.CreadoEn)
            .ToList();
        return Task.FromResult<IReadOnlyList<UserMembership>>(list);
    }

    /// <inheritdoc />
    /// <remarks>D6 / RF22: Al revocar a un miembro secundario también se elimina inmediatamente su vínculo de Telegram.</remarks>
    public Task<bool> RevokeSecondaryMemberAsync(Guid familyId, Guid memberUserId, CancellationToken ct)
    {
        lock (_sync)
        {
            if (!_members.TryGetValue(memberUserId, out var member))
            {
                return Task.FromResult(false);
            }

            if (member.FamilyId != familyId || member.Rol != UserRoles.Secundario)
            {
                return Task.FromResult(false);
            }

            _telegramLinks.TryRemove(memberUserId, out _);
            return Task.FromResult(_members.TryRemove(memberUserId, out _));
        }
    }

    #endregion

    #region Perfiles de cuidador (RF23)

    /// <inheritdoc />
    public Task<IReadOnlyList<CaregiverProfileRecord>> ListActiveProfilesAsync(Guid familyId, CancellationToken ct)
    {
        var list = _profiles.Values
            .Where(p => p.FamilyId == familyId && p.Activo)
            .OrderBy(p => p.CreadoEn)
            .ToList();
        return Task.FromResult<IReadOnlyList<CaregiverProfileRecord>>(list);
    }

    /// <inheritdoc />
    public Task<CaregiverProfileRecord?> GetProfileAsync(Guid profileId, CancellationToken ct)
    {
        _profiles.TryGetValue(profileId, out var profile);
        return Task.FromResult(profile);
    }

    /// <inheritdoc />
    public Task<CaregiverProfileRecord> CreateProfileAsync(Guid familyId, string nombre, string avatar, CancellationToken ct)
    {
        lock (_sync)
        {
            if (CountActiveProfiles(familyId) >= MaxActiveProfilesPerFamily)
            {
                throw new InvalidOperationException("RF23: No se permiten más de 6 perfiles de cuidador activos por familia.");
            }

            var profile = new CaregiverProfileRecord(
                Guid.NewGuid(),
                familyId,
                nombre,
                avatar,
                true,
                null,
                DateTimeOffset.UtcNow);

            _profiles[profile.Id] = profile;
            return Task.FromResult(profile);
        }
    }

    /// <inheritdoc />
    public Task<CaregiverProfileRecord?> UpdateProfileAsync(
        Guid familyId,
        Guid profileId,
        string nombre,
        string avatar,
        CancellationToken ct)
    {
        lock (_sync)
        {
            if (!_profiles.TryGetValue(profileId, out var current) || current.FamilyId != familyId || !current.Activo)
            {
                return Task.FromResult<CaregiverProfileRecord?>(null);
            }

            var updated = current with { Nombre = nombre, Avatar = avatar };
            _profiles[profileId] = updated;
            return Task.FromResult<CaregiverProfileRecord?>(updated);
        }
    }

    /// <inheritdoc />
    public Task<bool> SoftDeleteProfileAsync(Guid familyId, Guid profileId, CancellationToken ct)
    {
        lock (_sync)
        {
            if (!_profiles.TryGetValue(profileId, out var current) || current.FamilyId != familyId || !current.Activo)
            {
                return Task.FromResult(false);
            }

            _profiles[profileId] = current with
            {
                Activo = false,
                EliminadoEn = DateTimeOffset.UtcNow
            };
            return Task.FromResult(true);
        }
    }

    #endregion

    #region Invitaciones y auditoría de seguridad (RF22)

    /// <inheritdoc />
    public Task<InvitationRecord> CreateInvitationAsync(
        Guid familyId,
        Guid createdByUserId,
        string hashCodigo,
        DateTimeOffset expiraEn,
        CancellationToken ct)
    {
        var record = new InvitationRecord(
            Guid.NewGuid(),
            familyId,
            createdByUserId,
            hashCodigo,
            expiraEn,
            0,
            false,
            null,
            null,
            DateTimeOffset.UtcNow);

        _invitations[record.Id] = record;
        return Task.FromResult(record);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<InvitationRecord>> ListInvitationsAsync(Guid familyId, CancellationToken ct)
    {
        var list = _invitations.Values
            .Where(i => i.FamilyId == familyId)
            .OrderByDescending(i => i.CreadoEn)
            .ToList();
        return Task.FromResult<IReadOnlyList<InvitationRecord>>(list);
    }

    /// <inheritdoc />
    /// <remarks>Compara hashes SHA-256 en tiempo constante para evitar fugas por temporización.</remarks>
    public Task<InvitationRecord?> GetInvitationByHashAsync(string hashCodigo, CancellationToken ct)
    {
        var match = _invitations.Values.FirstOrDefault(i =>
            CryptoTokenHelper.ConstantTimeHashEquals(i.HashCodigo, hashCodigo));
        return Task.FromResult(match);
    }

    /// <inheritdoc />
    public Task IncrementInvitationAttemptAsync(Guid invitationId, CancellationToken ct)
    {
        lock (_sync)
        {
            if (_invitations.TryGetValue(invitationId, out var current) && current.Intentos < MaxVerificationAttempts)
            {
                _invitations[invitationId] = current with { Intentos = current.Intentos + 1 };
            }
        }
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<UserMembership> AcceptInvitationAsync(
        Guid invitationId,
        Guid familyId,
        Guid userId,
        string email,
        string? profileName,
        string? avatar,
        CancellationToken ct)
    {
        lock (_sync)
        {
            if (!_invitations.TryGetValue(invitationId, out var inv) || inv.Usada)
            {
                throw new InvalidOperationException("La invitación ya fue utilizada.");
            }

            var now = DateTimeOffset.UtcNow;
            _invitations[invitationId] = inv with
            {
                Usada = true,
                UsadaPor = userId,
                UsadaEn = now
            };

            var membership = new UserMembership(userId, familyId, UserRoles.Secundario, email, now);
            _members[userId] = membership;

            TryCreateOptionalInitialProfile(familyId, profileName, avatar, now);
            return Task.FromResult(membership);
        }
    }

    /// <inheritdoc />
    public Task LogSecurityEventAsync(SecurityAuditEntry entry, CancellationToken ct)
    {
        _securityAudit.Enqueue(entry);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<SecurityAuditEntry>> ListSecurityEventsAsync(Guid familyId, CancellationToken ct)
    {
        var list = _securityAudit
            .Where(e => e.FamilyId == familyId)
            .OrderByDescending(e => e.Ts)
            .ToList();
        return Task.FromResult<IReadOnlyList<SecurityAuditEntry>>(list);
    }

    #endregion

    #region Telegram y recordatorios de alimentación (RF18, RF19, RF38, RF39)

    /// <inheritdoc />
    public Task<TelegramLinkRecord?> GetTelegramLinkByUserAsync(Guid userId, CancellationToken ct)
    {
        _telegramLinks.TryGetValue(userId, out var link);
        return Task.FromResult(link);
    }

    /// <inheritdoc />
    public Task<TelegramLinkRecord?> GetTelegramLinkByChatIdAsync(long chatId, CancellationToken ct)
    {
        var link = _telegramLinks.Values.FirstOrDefault(l =>
            l.ChatId == chatId && _members.ContainsKey(l.UsuarioId));
        return Task.FromResult(link);
    }

    /// <inheritdoc />
    public Task<TelegramLinkRecord> SaveTelegramLinkCodeAsync(
        Guid userId,
        string hashCodigo,
        DateTimeOffset expiraEn,
        CancellationToken ct)
    {
        lock (_sync)
        {
            if (_telegramLinks.TryGetValue(userId, out var existing))
            {
                var updated = existing with
                {
                    HashCodigo = hashCodigo,
                    CodigoExpiraEn = expiraEn,
                    CodigoUsado = false,
                    Intentos = 0
                };
                _telegramLinks[userId] = updated;
                return Task.FromResult(updated);
            }

            var created = new TelegramLinkRecord(
                Guid.NewGuid(),
                userId,
                null,
                hashCodigo,
                expiraEn,
                false,
                0,
                null,
                DateTimeOffset.UtcNow);
            _telegramLinks[userId] = created;
            return Task.FromResult(created);
        }
    }

    /// <inheritdoc />
    public Task<TelegramLinkRecord?> CompleteTelegramLinkByCodeAsync(
        string candidateHash,
        long chatId,
        DateTimeOffset now,
        CancellationToken ct)
    {
        lock (_sync)
        {
            var pending = _telegramLinks.Values
                .Where(l =>
                    !l.CodigoUsado &&
                    !string.IsNullOrWhiteSpace(l.HashCodigo) &&
                    l.CodigoExpiraEn.HasValue &&
                    now < l.CodigoExpiraEn.Value &&
                    l.Intentos < MaxVerificationAttempts &&
                    _members.ContainsKey(l.UsuarioId))
                .ToList();

            var match = pending.FirstOrDefault(l =>
                CryptoTokenHelper.ConstantTimeHashEquals(l.HashCodigo!, candidateHash));

            if (match is null)
            {
                return Task.FromResult<TelegramLinkRecord?>(null);
            }

            // Si ese chat_id estaba vinculado a otro usuario anterior, desasociarlo para mantener unicidad
            foreach (var other in _telegramLinks.Values.Where(l => l.ChatId == chatId && l.UsuarioId != match.UsuarioId).ToList())
            {
                _telegramLinks[other.UsuarioId] = other with { ChatId = null, VinculadoEn = null };
            }

            var linked = match with
            {
                ChatId = chatId,
                HashCodigo = null,
                CodigoUsado = true,
                VinculadoEn = now
            };
            _telegramLinks[match.UsuarioId] = linked;
            return Task.FromResult<TelegramLinkRecord?>(linked);
        }
    }

    /// <inheritdoc />
    public Task<bool> DeleteTelegramLinkByUserAsync(Guid userId, CancellationToken ct)
    {
        return Task.FromResult(_telegramLinks.TryRemove(userId, out _));
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<long>> ListLinkedTelegramChatsByFamilyAsync(Guid familyId, CancellationToken ct)
    {
        var memberIds = _members.Values
            .Where(m => m.FamilyId == familyId)
            .Select(m => m.Id)
            .ToHashSet();

        var chats = _telegramLinks.Values
            .Where(l => memberIds.Contains(l.UsuarioId) && l.ChatId.HasValue)
            .Select(l => l.ChatId!.Value)
            .Distinct()
            .ToList();

        return Task.FromResult<IReadOnlyList<long>>(chats);
    }

    /// <inheritdoc />
    public Task<FeedingReminderRecord> GetOrCreateFeedingReminderAsync(Guid familyId, CancellationToken ct)
    {
        var record = _reminders.GetOrAdd(familyId, CreateDefaultReminder);
        return Task.FromResult(record);
    }

    /// <inheritdoc />
    public Task<FeedingReminderRecord> UpdateFeedingReminderAsync(
        Guid familyId,
        int intervaloHoras,
        bool activo,
        DateTimeOffset? ultimoEnvio,
        CancellationToken ct)
    {
        lock (_sync)
        {
            var current = _reminders.GetOrAdd(familyId, CreateDefaultReminder);
            var updated = current with
            {
                IntervaloHoras = intervaloHoras,
                Activo = activo,
                UltimoEnvio = ultimoEnvio ?? current.UltimoEnvio,
                ActualizadoEn = DateTimeOffset.UtcNow
            };
            _reminders[familyId] = updated;
            return Task.FromResult(updated);
        }
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<FeedingReminderRecord>> ListActiveFeedingRemindersAsync(CancellationToken ct)
    {
        var list = _reminders.Values.Where(r => r.Activo).ToList();
        return Task.FromResult<IReadOnlyList<FeedingReminderRecord>>(list);
    }

    #endregion

    #region Helpers privados

    private int CountActiveProfiles(Guid familyId) =>
        _profiles.Values.Count(p => p.FamilyId == familyId && p.Activo);

    private void TryCreateOptionalInitialProfile(Guid familyId, string? profileName, string? avatar, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(profileName) || CountActiveProfiles(familyId) >= MaxActiveProfilesPerFamily)
        {
            return;
        }

        var trimmed = profileName.Trim();
        if (trimmed.Length is < 1 or > 30)
        {
            return;
        }

        var profile = new CaregiverProfileRecord(
            Guid.NewGuid(),
            familyId,
            trimmed,
            string.IsNullOrWhiteSpace(avatar) ? "default" : avatar.Trim(),
            true,
            null,
            now);
        _profiles[profile.Id] = profile;
    }

    private static FeedingReminderRecord CreateDefaultReminder(Guid familyId) =>
        new(
            Guid.NewGuid(),
            familyId,
            IntervaloHoras: 3,
            Activo: true,
            UltimoEnvio: null,
            ActualizadoEn: DateTimeOffset.UtcNow);

    #endregion
}
