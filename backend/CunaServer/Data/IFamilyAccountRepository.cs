namespace CunaServer.Data;

/// <summary>
/// Contrato de acceso a datos para cuentas familiares, membresías, perfiles de cuidador,
/// invitaciones, bitácora de seguridad, vínculos de Telegram y recordatorios de alimentación.
/// </summary>
public interface IFamilyAccountRepository
{
    #region Familias y membresías (RF21, RF22)

    /// <summary>
    /// Obtiene la membresía de un usuario autenticado para deducir su <c>family_id</c> y rol desde la base de datos.
    /// </summary>
    Task<UserMembership?> GetUserMembershipAsync(Guid userId, CancellationToken ct);

    /// <summary>
    /// Obtiene el registro de una familia por su identificador.
    /// </summary>
    Task<FamilyRecord?> GetFamilyAsync(Guid familyId, CancellationToken ct);

    /// <summary>
    /// Crea una familia nueva junto con su usuario principal y perfil inicial de cuidador (operación idempotente).
    /// </summary>
    Task<FamilyRecord> CreateFamilyWithPrincipalAsync(
        Guid userId,
        string email,
        string familyName,
        string initialProfileName,
        string initialAvatar,
        CancellationToken ct);

    /// <summary>
    /// Actualiza el nombre visible de la familia indicada.
    /// </summary>
    Task<FamilyRecord?> UpdateFamilyNameAsync(Guid familyId, string newName, CancellationToken ct);

    /// <summary>
    /// Lista todos los miembros de la familia ordenados cronológicamente.
    /// </summary>
    Task<IReadOnlyList<UserMembership>> ListFamilyMembersAsync(Guid familyId, CancellationToken ct);

    /// <summary>
    /// Revoca a un miembro secundario de la familia; nunca permite revocar al usuario principal.
    /// </summary>
    Task<bool> RevokeSecondaryMemberAsync(Guid familyId, Guid memberUserId, CancellationToken ct);

    #endregion

    #region Perfiles de cuidador (RF23)

    /// <summary>
    /// Lista los perfiles de cuidador activos de una familia.
    /// </summary>
    Task<IReadOnlyList<CaregiverProfileRecord>> ListActiveProfilesAsync(Guid familyId, CancellationToken ct);

    /// <summary>
    /// Obtiene un perfil de cuidador por identificador.
    /// </summary>
    Task<CaregiverProfileRecord?> GetProfileAsync(Guid profileId, CancellationToken ct);

    /// <summary>
    /// Crea un perfil de cuidador respetando el límite máximo de 6 perfiles activos por familia (RF23).
    /// </summary>
    Task<CaregiverProfileRecord> CreateProfileAsync(Guid familyId, string nombre, string avatar, CancellationToken ct);

    /// <summary>
    /// Actualiza nombre y avatar de un perfil de cuidador activo perteneciente a la familia.
    /// </summary>
    Task<CaregiverProfileRecord?> UpdateProfileAsync(Guid familyId, Guid profileId, string nombre, string avatar, CancellationToken ct);

    /// <summary>
    /// Realiza el borrado lógico (soft delete) de un perfil de cuidador activo.
    /// </summary>
    Task<bool> SoftDeleteProfileAsync(Guid familyId, Guid profileId, CancellationToken ct);

    #endregion

    #region Invitaciones y auditoría de seguridad (RF22)

    /// <summary>
    /// Registra una invitación almacenando únicamente el hash SHA-256 del token de un solo uso.
    /// </summary>
    Task<InvitationRecord> CreateInvitationAsync(Guid familyId, Guid createdByUserId, string hashCodigo, DateTimeOffset expiraEn, CancellationToken ct);

    /// <summary>
    /// Lista las invitaciones de una familia ordenadas de más reciente a más antigua.
    /// </summary>
    Task<IReadOnlyList<InvitationRecord>> ListInvitationsAsync(Guid familyId, CancellationToken ct);

    /// <summary>
    /// Busca una invitación comparando el hash SHA-256 del token recibido.
    /// </summary>
    Task<InvitationRecord?> GetInvitationByHashAsync(string hashCodigo, CancellationToken ct);

    /// <summary>
    /// Incrementa el contador de intentos fallidos de una invitación hasta el tope de seguridad de 5.
    /// </summary>
    Task IncrementInvitationAttemptAsync(Guid invitationId, CancellationToken ct);

    /// <summary>
    /// Marca la invitación como usada, crea la membresía secundaria y opcionalmente el perfil inicial.
    /// </summary>
    Task<UserMembership> AcceptInvitationAsync(
        Guid invitationId,
        Guid familyId,
        Guid userId,
        string email,
        string? profileName,
        string? avatar,
        CancellationToken ct);

    /// <summary>
    /// Registra un evento en la bitácora de seguridad sin exponer tokens ni datos sensibles del bebé.
    /// </summary>
    Task LogSecurityEventAsync(SecurityAuditEntry entry, CancellationToken ct);

    /// <summary>
    /// Lista los eventos recientes de la bitácora de seguridad de una familia.
    /// </summary>
    Task<IReadOnlyList<SecurityAuditEntry>> ListSecurityEventsAsync(Guid familyId, CancellationToken ct);

    #endregion

    #region Telegram y recordatorios de alimentación (RF18, RF19, RF38, RF39)

    /// <summary>
    /// Obtiene el vínculo de Telegram asociado a un usuario.
    /// </summary>
    Task<TelegramLinkRecord?> GetTelegramLinkByUserAsync(Guid userId, CancellationToken ct);

    /// <summary>
    /// Obtiene el vínculo de Telegram activo para un <c>chat_id</c> verificando membresía vigente.
    /// </summary>
    Task<TelegramLinkRecord?> GetTelegramLinkByChatIdAsync(long chatId, CancellationToken ct);

    /// <summary>
    /// Guarda o renueva el hash SHA-256 de un código efímero de vinculación de Telegram.
    /// </summary>
    Task<TelegramLinkRecord> SaveTelegramLinkCodeAsync(Guid userId, string hashCodigo, DateTimeOffset expiraEn, CancellationToken ct);

    /// <summary>
    /// Completa la vinculación de Telegram validando en tiempo constante el hash del código activo.
    /// </summary>
    Task<TelegramLinkRecord?> CompleteTelegramLinkByCodeAsync(string candidateHash, long chatId, DateTimeOffset now, CancellationToken ct);

    /// <summary>
    /// Elimina el vínculo de Telegram de un usuario.
    /// </summary>
    Task<bool> DeleteTelegramLinkByUserAsync(Guid userId, CancellationToken ct);

    /// <summary>
    /// Lista los <c>chat_id</c> únicos vinculados a miembros activos de una familia.
    /// </summary>
    Task<IReadOnlyList<long>> ListLinkedTelegramChatsByFamilyAsync(Guid familyId, CancellationToken ct);

    /// <summary>
    /// Obtiene la configuración de recordatorio de alimentación de la familia o la crea con valores predeterminados (3 h).
    /// </summary>
    Task<FeedingReminderRecord> GetOrCreateFeedingReminderAsync(Guid familyId, CancellationToken ct);

    /// <summary>
    /// Actualiza intervalo, estado activo y marca de último envío del recordatorio de alimentación.
    /// </summary>
    Task<FeedingReminderRecord> UpdateFeedingReminderAsync(Guid familyId, int intervaloHoras, bool activo, DateTimeOffset? ultimoEnvio, CancellationToken ct);

    /// <summary>
    /// Lista todos los recordatorios de alimentación activos para evaluación periódica del servicio de fondo.
    /// </summary>
    Task<IReadOnlyList<FeedingReminderRecord>> ListActiveFeedingRemindersAsync(CancellationToken ct);

    #endregion
}
