using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CunaServer.Data;

public interface IFamilyAccountRepository
{
    Task<UserMembership?> GetUserMembershipAsync(Guid userId, CancellationToken ct);
    Task<FamilyRecord?> GetFamilyAsync(Guid familyId, CancellationToken ct);
    Task<FamilyRecord> CreateFamilyWithPrincipalAsync(
        Guid userId,
        string email,
        string familyName,
        string initialProfileName,
        string initialAvatar,
        CancellationToken ct);
    Task<FamilyRecord?> UpdateFamilyNameAsync(Guid familyId, string newName, CancellationToken ct);

    Task<IReadOnlyList<UserMembership>> ListFamilyMembersAsync(Guid familyId, CancellationToken ct);
    Task<bool> RevokeSecondaryMemberAsync(Guid familyId, Guid memberUserId, CancellationToken ct);

    Task<IReadOnlyList<CaregiverProfileRecord>> ListActiveProfilesAsync(Guid familyId, CancellationToken ct);
    Task<CaregiverProfileRecord?> GetProfileAsync(Guid profileId, CancellationToken ct);
    Task<CaregiverProfileRecord> CreateProfileAsync(Guid familyId, string nombre, string avatar, CancellationToken ct);
    Task<CaregiverProfileRecord?> UpdateProfileAsync(Guid familyId, Guid profileId, string nombre, string avatar, CancellationToken ct);
    Task<bool> SoftDeleteProfileAsync(Guid familyId, Guid profileId, CancellationToken ct);

    Task<InvitationRecord> CreateInvitationAsync(Guid familyId, Guid createdByUserId, string hashCodigo, DateTimeOffset expiraEn, CancellationToken ct);
    Task<IReadOnlyList<InvitationRecord>> ListInvitationsAsync(Guid familyId, CancellationToken ct);
    Task<InvitationRecord?> GetInvitationByHashAsync(string hashCodigo, CancellationToken ct);
    Task IncrementInvitationAttemptAsync(Guid invitationId, CancellationToken ct);
    Task<UserMembership> AcceptInvitationAsync(
        Guid invitationId,
        Guid familyId,
        Guid userId,
        string email,
        string? profileName,
        string? avatar,
        CancellationToken ct);

    Task LogSecurityEventAsync(SecurityAuditEntry entry, CancellationToken ct);
    Task<IReadOnlyList<SecurityAuditEntry>> ListSecurityEventsAsync(Guid familyId, CancellationToken ct);
}

public sealed class InMemoryFamilyAccountRepository : IFamilyAccountRepository
{
    private readonly ConcurrentDictionary<Guid, FamilyRecord> _families = new();
    private readonly ConcurrentDictionary<Guid, UserMembership> _members = new();
    private readonly ConcurrentDictionary<Guid, CaregiverProfileRecord> _profiles = new();
    private readonly ConcurrentDictionary<Guid, InvitationRecord> _invitations = new();
    private readonly ConcurrentQueue<SecurityAuditEntry> _securityAudit = new();
    private readonly object _sync = new();

    public Task<UserMembership?> GetUserMembershipAsync(Guid userId, CancellationToken ct)
    {
        _members.TryGetValue(userId, out var membership);
        return Task.FromResult(membership);
    }

    public Task<FamilyRecord?> GetFamilyAsync(Guid familyId, CancellationToken ct)
    {
        _families.TryGetValue(familyId, out var family);
        return Task.FromResult(family);
    }

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
            if (_members.TryGetValue(userId, out var existing) && _families.TryGetValue(existing.FamilyId, out var existingFamily))
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

    public Task<IReadOnlyList<UserMembership>> ListFamilyMembersAsync(Guid familyId, CancellationToken ct)
    {
        var list = _members.Values
            .Where(m => m.FamilyId == familyId)
            .OrderBy(m => m.CreadoEn)
            .ToList();
        return Task.FromResult<IReadOnlyList<UserMembership>>(list);
    }

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

            return Task.FromResult(_members.TryRemove(memberUserId, out _));
        }
    }

    public Task<IReadOnlyList<CaregiverProfileRecord>> ListActiveProfilesAsync(Guid familyId, CancellationToken ct)
    {
        var list = _profiles.Values
            .Where(p => p.FamilyId == familyId && p.Activo)
            .OrderBy(p => p.CreadoEn)
            .ToList();
        return Task.FromResult<IReadOnlyList<CaregiverProfileRecord>>(list);
    }

    public Task<CaregiverProfileRecord?> GetProfileAsync(Guid profileId, CancellationToken ct)
    {
        _profiles.TryGetValue(profileId, out var profile);
        return Task.FromResult(profile);
    }

    public Task<CaregiverProfileRecord> CreateProfileAsync(Guid familyId, string nombre, string avatar, CancellationToken ct)
    {
        lock (_sync)
        {
            var activeCount = _profiles.Values.Count(p => p.FamilyId == familyId && p.Activo);
            if (activeCount >= 6)
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

    public Task<IReadOnlyList<InvitationRecord>> ListInvitationsAsync(Guid familyId, CancellationToken ct)
    {
        var list = _invitations.Values
            .Where(i => i.FamilyId == familyId)
            .OrderByDescending(i => i.CreadoEn)
            .ToList();
        return Task.FromResult<IReadOnlyList<InvitationRecord>>(list);
    }

    public Task<InvitationRecord?> GetInvitationByHashAsync(string hashCodigo, CancellationToken ct)
    {
        var match = _invitations.Values.FirstOrDefault(i =>
            CryptoTokenHelper.ConstantTimeHashEquals(i.HashCodigo, hashCodigo));
        return Task.FromResult(match);
    }

    public Task IncrementInvitationAttemptAsync(Guid invitationId, CancellationToken ct)
    {
        lock (_sync)
        {
            if (_invitations.TryGetValue(invitationId, out var current) && current.Intentos < 5)
            {
                _invitations[invitationId] = current with { Intentos = current.Intentos + 1 };
            }
        }
        return Task.CompletedTask;
    }

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

            if (!string.IsNullOrWhiteSpace(profileName))
            {
                var activeCount = _profiles.Values.Count(p => p.FamilyId == familyId && p.Activo);
                if (activeCount < 6)
                {
                    var trimmed = profileName.Trim();
                    if (trimmed.Length is >= 1 and <= 30)
                    {
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
                }
            }

            return Task.FromResult(membership);
        }
    }

    public Task LogSecurityEventAsync(SecurityAuditEntry entry, CancellationToken ct)
    {
        _securityAudit.Enqueue(entry);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<SecurityAuditEntry>> ListSecurityEventsAsync(Guid familyId, CancellationToken ct)
    {
        var list = _securityAudit
            .Where(e => e.FamilyId == familyId)
            .OrderByDescending(e => e.Ts)
            .ToList();
        return Task.FromResult<IReadOnlyList<SecurityAuditEntry>>(list);
    }
}

public sealed class PostgrestFamilyAccountRepository : IFamilyAccountRepository
{
    private readonly HttpClient _http;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public PostgrestFamilyAccountRepository(HttpClient httpClient, IConfiguration configuration)
    {
        _http = httpClient;
        var supabaseUrl = (configuration["SUPABASE_URL"] ?? "").TrimEnd('/');
        var secretKey = configuration["SUPABASE_SECRET_KEY"] ?? "";

        if (!string.IsNullOrWhiteSpace(supabaseUrl))
        {
            _http.BaseAddress = new Uri($"{supabaseUrl}/rest/v1/");
        }

        if (!string.IsNullOrWhiteSpace(secretKey))
        {
            _http.DefaultRequestHeaders.Remove("apikey");
            _http.DefaultRequestHeaders.Add("apikey", secretKey);
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", secretKey);
        }
    }

    public async Task<UserMembership?> GetUserMembershipAsync(Guid userId, CancellationToken ct)
    {
        var rows = await _http.GetFromJsonAsync<List<UserMembershipDto>>(
            $"usuarios?id=eq.{userId}&select=id,family_id,rol,email,creado_en&limit=1",
            JsonOptions,
            ct);
        var dto = rows?.FirstOrDefault();
        return dto?.ToModel();
    }

    public async Task<FamilyRecord?> GetFamilyAsync(Guid familyId, CancellationToken ct)
    {
        var rows = await _http.GetFromJsonAsync<List<FamilyDto>>(
            $"familias?id=eq.{familyId}&select=id,nombre,creado_en&limit=1",
            JsonOptions,
            ct);
        return rows?.FirstOrDefault()?.ToModel();
    }

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

        using var familyReq = new HttpRequestMessage(HttpMethod.Post, "familias");
        familyReq.Headers.Add("Prefer", "return=representation");
        familyReq.Content = JsonContent.Create(new { nombre = familyName }, options: JsonOptions);
        using var familyRes = await _http.SendAsync(familyReq, ct);
        familyRes.EnsureSuccessStatusCode();
        var createdFamilies = await familyRes.Content.ReadFromJsonAsync<List<FamilyDto>>(JsonOptions, ct);
        var family = createdFamilies!.Single().ToModel();

        using var userReq = new HttpRequestMessage(HttpMethod.Post, "usuarios");
        userReq.Content = JsonContent.Create(new
        {
            id = userId,
            family_id = family.Id,
            rol = UserRoles.Principal,
            email
        }, options: JsonOptions);
        using var userRes = await _http.SendAsync(userReq, ct);
        userRes.EnsureSuccessStatusCode();

        await CreateProfileAsync(family.Id, initialProfileName, initialAvatar, ct);
        return family;
    }

    public async Task<FamilyRecord?> UpdateFamilyNameAsync(Guid familyId, string newName, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Patch, $"familias?id=eq.{familyId}");
        req.Headers.Add("Prefer", "return=representation");
        req.Content = JsonContent.Create(new { nombre = newName }, options: JsonOptions);
        using var res = await _http.SendAsync(req, ct);
        res.EnsureSuccessStatusCode();
        var rows = await res.Content.ReadFromJsonAsync<List<FamilyDto>>(JsonOptions, ct);
        return rows?.FirstOrDefault()?.ToModel();
    }

    public async Task<IReadOnlyList<UserMembership>> ListFamilyMembersAsync(Guid familyId, CancellationToken ct)
    {
        var rows = await _http.GetFromJsonAsync<List<UserMembershipDto>>(
            $"usuarios?family_id=eq.{familyId}&select=id,family_id,rol,email,creado_en&order=creado_en.asc",
            JsonOptions,
            ct) ?? [];
        return rows.Select(r => r.ToModel()).ToList();
    }

    public async Task<bool> RevokeSecondaryMemberAsync(Guid familyId, Guid memberUserId, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(
            HttpMethod.Delete,
            $"usuarios?id=eq.{memberUserId}&family_id=eq.{familyId}&rol=eq.{UserRoles.Secundario}");
        req.Headers.Add("Prefer", "return=representation");
        using var res = await _http.SendAsync(req, ct);
        res.EnsureSuccessStatusCode();
        var deleted = await res.Content.ReadFromJsonAsync<List<UserMembershipDto>>(JsonOptions, ct);
        return deleted is { Count: > 0 };
    }

    public async Task<IReadOnlyList<CaregiverProfileRecord>> ListActiveProfilesAsync(Guid familyId, CancellationToken ct)
    {
        var rows = await _http.GetFromJsonAsync<List<CaregiverProfileDto>>(
            $"perfiles_cuidador?family_id=eq.{familyId}&activo=is.true&select=id,family_id,nombre,avatar,activo,eliminado_en,creado_en&order=creado_en.asc",
            JsonOptions,
            ct) ?? [];
        return rows.Select(r => r.ToModel()).ToList();
    }

    public async Task<CaregiverProfileRecord?> GetProfileAsync(Guid profileId, CancellationToken ct)
    {
        var rows = await _http.GetFromJsonAsync<List<CaregiverProfileDto>>(
            $"perfiles_cuidador?id=eq.{profileId}&select=id,family_id,nombre,avatar,activo,eliminado_en,creado_en&limit=1",
            JsonOptions,
            ct);
        return rows?.FirstOrDefault()?.ToModel();
    }

    public async Task<CaregiverProfileRecord> CreateProfileAsync(Guid familyId, string nombre, string avatar, CancellationToken ct)
    {
        var active = await ListActiveProfilesAsync(familyId, ct);
        if (active.Count >= 6)
        {
            throw new InvalidOperationException("RF23: No se permiten más de 6 perfiles de cuidador activos por familia.");
        }

        using var req = new HttpRequestMessage(HttpMethod.Post, "perfiles_cuidador");
        req.Headers.Add("Prefer", "return=representation");
        req.Content = JsonContent.Create(new
        {
            family_id = familyId,
            nombre,
            avatar,
            activo = true
        }, options: JsonOptions);
        using var res = await _http.SendAsync(req, ct);
        res.EnsureSuccessStatusCode();
        var rows = await res.Content.ReadFromJsonAsync<List<CaregiverProfileDto>>(JsonOptions, ct);
        return rows!.Single().ToModel();
    }

    public async Task<CaregiverProfileRecord?> UpdateProfileAsync(
        Guid familyId,
        Guid profileId,
        string nombre,
        string avatar,
        CancellationToken ct)
    {
        using var req = new HttpRequestMessage(
            HttpMethod.Patch,
            $"perfiles_cuidador?id=eq.{profileId}&family_id=eq.{familyId}&activo=is.true");
        req.Headers.Add("Prefer", "return=representation");
        req.Content = JsonContent.Create(new { nombre, avatar }, options: JsonOptions);
        using var res = await _http.SendAsync(req, ct);
        res.EnsureSuccessStatusCode();
        var rows = await res.Content.ReadFromJsonAsync<List<CaregiverProfileDto>>(JsonOptions, ct);
        return rows?.FirstOrDefault()?.ToModel();
    }

    public async Task<bool> SoftDeleteProfileAsync(Guid familyId, Guid profileId, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(
            HttpMethod.Patch,
            $"perfiles_cuidador?id=eq.{profileId}&family_id=eq.{familyId}&activo=is.true");
        req.Headers.Add("Prefer", "return=representation");
        req.Content = JsonContent.Create(new
        {
            activo = false,
            eliminado_en = DateTimeOffset.UtcNow
        }, options: JsonOptions);
        using var res = await _http.SendAsync(req, ct);
        res.EnsureSuccessStatusCode();
        var rows = await res.Content.ReadFromJsonAsync<List<CaregiverProfileDto>>(JsonOptions, ct);
        return rows is { Count: > 0 };
    }

    public async Task<InvitationRecord> CreateInvitationAsync(
        Guid familyId,
        Guid createdByUserId,
        string hashCodigo,
        DateTimeOffset expiraEn,
        CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, "invitaciones");
        req.Headers.Add("Prefer", "return=representation");
        req.Content = JsonContent.Create(new
        {
            family_id = familyId,
            creado_por = createdByUserId,
            hash_codigo = hashCodigo,
            expira_en = expiraEn,
            intentos = 0,
            usada = false
        }, options: JsonOptions);
        using var res = await _http.SendAsync(req, ct);
        res.EnsureSuccessStatusCode();
        var rows = await res.Content.ReadFromJsonAsync<List<InvitationDto>>(JsonOptions, ct);
        return rows!.Single().ToModel();
    }

    public async Task<IReadOnlyList<InvitationRecord>> ListInvitationsAsync(Guid familyId, CancellationToken ct)
    {
        var rows = await _http.GetFromJsonAsync<List<InvitationDto>>(
            $"invitaciones?family_id=eq.{familyId}&select=id,family_id,creado_por,hash_codigo,expira_en,intentos,usada,usada_por,usada_en,creado_en&order=creado_en.desc",
            JsonOptions,
            ct) ?? [];
        return rows.Select(r => r.ToModel()).ToList();
    }

    public async Task<InvitationRecord?> GetInvitationByHashAsync(string hashCodigo, CancellationToken ct)
    {
        var rows = await _http.GetFromJsonAsync<List<InvitationDto>>(
            $"invitaciones?hash_codigo=eq.{hashCodigo}&select=id,family_id,creado_por,hash_codigo,expira_en,intentos,usada,usada_por,usada_en,creado_en&limit=1",
            JsonOptions,
            ct);
        return rows?.FirstOrDefault()?.ToModel();
    }

    public async Task IncrementInvitationAttemptAsync(Guid invitationId, CancellationToken ct)
    {
        var rows = await _http.GetFromJsonAsync<List<InvitationDto>>(
            $"invitaciones?id=eq.{invitationId}&select=id,family_id,creado_por,hash_codigo,expira_en,intentos,usada,usada_por,usada_en,creado_en&limit=1",
            JsonOptions,
            ct);
        var current = rows?.FirstOrDefault();
        if (current is null || current.Intentos >= 5)
        {
            return;
        }

        using var req = new HttpRequestMessage(HttpMethod.Patch, $"invitaciones?id=eq.{invitationId}");
        req.Content = JsonContent.Create(new { intentos = current.Intentos + 1 }, options: JsonOptions);
        using var res = await _http.SendAsync(req, ct);
        res.EnsureSuccessStatusCode();
    }

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
        using var invReq = new HttpRequestMessage(HttpMethod.Patch, $"invitaciones?id=eq.{invitationId}&usada=is.false");
        invReq.Content = JsonContent.Create(new
        {
            usada = true,
            usada_por = userId,
            usada_en = now
        }, options: JsonOptions);
        using var invRes = await _http.SendAsync(invReq, ct);
        invRes.EnsureSuccessStatusCode();

        using var userReq = new HttpRequestMessage(HttpMethod.Post, "usuarios");
        userReq.Headers.Add("Prefer", "return=representation");
        userReq.Content = JsonContent.Create(new
        {
            id = userId,
            family_id = familyId,
            rol = UserRoles.Secundario,
            email
        }, options: JsonOptions);
        using var userRes = await _http.SendAsync(userReq, ct);
        userRes.EnsureSuccessStatusCode();
        var created = await userRes.Content.ReadFromJsonAsync<List<UserMembershipDto>>(JsonOptions, ct);

        if (!string.IsNullOrWhiteSpace(profileName))
        {
            var active = await ListActiveProfilesAsync(familyId, ct);
            if (active.Count < 6)
            {
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
        }

        return created!.Single().ToModel();
    }

    public async Task LogSecurityEventAsync(SecurityAuditEntry entry, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, "bitacora_seguridad");
        req.Content = JsonContent.Create(new
        {
            family_id = entry.FamilyId,
            usuario_id = entry.UsuarioId,
            dispositivo_id = entry.DispositivoId,
            evento = entry.Evento,
            ip_origen = entry.IpOrigen,
            detalle = entry.Detalle is null ? null : new { info = entry.Detalle },
            ts = entry.Ts
        }, options: JsonOptions);
        using var res = await _http.SendAsync(req, ct);
        res.EnsureSuccessStatusCode();
    }

    public async Task<IReadOnlyList<SecurityAuditEntry>> ListSecurityEventsAsync(Guid familyId, CancellationToken ct)
    {
        var rows = await _http.GetFromJsonAsync<List<SecurityAuditDto>>(
            $"bitacora_seguridad?family_id=eq.{familyId}&select=family_id,usuario_id,dispositivo_id,evento,ip_origen,ts&order=ts.desc&limit=50",
            JsonOptions,
            ct) ?? [];
        return rows.Select(r => new SecurityAuditEntry(
            r.FamilyId,
            r.UsuarioId,
            r.DispositivoId,
            r.Evento,
            r.IpOrigen,
            null,
            r.Ts)).ToList();
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
        DateTimeOffset Ts);
}
