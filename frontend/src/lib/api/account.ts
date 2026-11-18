import { ApiError, apiBaseUrl, requestJson } from "./client";

export interface FamilyRecord {
  id: string;
  nombre: string;
  creadoEn: string;
}

export interface UserMembership {
  id: string;
  familyId: string;
  rol: "principal" | "secundario";
  email: string;
  creadoEn: string;
}

export interface CaregiverProfileRecord {
  id: string;
  familyId: string;
  nombre: string;
  avatar: string;
  activo: boolean;
  eliminadoEn: string | null;
  creadoEn: string;
}

export interface AccountContextResponse {
  userId: string;
  email: string;
  familia: FamilyRecord | null;
  rol: "principal" | "secundario" | null;
  perfiles: CaregiverProfileRecord[];
  miembros: UserMembership[];
}

export interface CreateInvitationResponse {
  invitationId: string;
  token: string;
  inviteUrl: string;
  expiraEn: string;
}

export interface SecurityAuditEntry {
  familyId: string | null;
  usuarioId: string | null;
  dispositivoId: string | null;
  evento: string;
  ipOrigen: string | null;
  detalle: string | null;
  ts: string;
}

export interface HealthStatusResponse {
  status: string;
  service: string;
  tcpPort: number;
  connectedDevices: number;
  timestamp: string;
}

export interface AuthModeResponse {
  localLoginEnabled: boolean;
  supabaseConfigured: boolean;
  telegramBotUsername: string;
}

export interface LocalLoginResponse {
  accessToken: string;
  userId: string;
  email: string;
  username: string;
  expiresAt: string;
}

export interface DemoSimulateResponse {
  action: string;
  message: string;
  executedAt: string;
}

/** Operaciones de cuenta, familia, perfiles de cuidador, invitaciones, auditoría y salud. */
export const accountEndpoints = {
  /** Consulta qué modos de acceso están activos en el backend. */
  async getAuthMode(signal?: AbortSignal): Promise<AuthModeResponse> {
    const response = await fetch(`${apiBaseUrl}/api/auth/mode`, { signal });
    if (!response.ok) {
      throw new ApiError(response.status, `Error HTTP ${response.status}`);
    }
    return (await response.json()) as AuthModeResponse;
  },

  /** Inicia sesión con el usuario local temporal de pruebas y presentación preliminar. */
  async localLogin(payload: { username: string; password: string }): Promise<LocalLoginResponse> {
    const response = await fetch(`${apiBaseUrl}/api/auth/local-login`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(payload),
    });
    if (!response.ok) {
      let msg = `Error HTTP ${response.status}`;
      try {
        const body = (await response.json()) as { error?: string };
        if (body.error) {
          msg = body.error;
        }
      } catch {
        // Ignorar si no es JSON
      }
      throw new ApiError(response.status, msg);
    }
    return (await response.json()) as LocalLoginResponse;
  },

  /** Dispara un evento simulado sin hardware físico en modo demostración local. */
  simulateDemoEvent(
    accessToken: string,
    cunaId: string,
    action: string
  ): Promise<DemoSimulateResponse> {
    return requestJson<DemoSimulateResponse>(`/api/demo/cribs/${cunaId}/simulate`, accessToken, {
      method: "POST",
      body: JSON.stringify({ action }),
    });
  },
  /** Obtiene el contexto de cuenta, familia, rol y perfiles del usuario autenticado. */
  getMe(accessToken: string): Promise<AccountContextResponse> {
    return requestJson<AccountContextResponse>("/api/account/me", accessToken);
  },

  /** Crea una nueva familia con el usuario actual como Usuario Principal (RF01). */
  bootstrap(
    accessToken: string,
    payload: { nombreFamilia?: string; nombrePerfilInicial?: string; avatarInicial?: string }
  ): Promise<AccountContextResponse> {
    return requestJson<AccountContextResponse>("/api/account/bootstrap", accessToken, {
      method: "POST",
      body: JSON.stringify(payload),
    });
  },

  /** Actualiza el nombre visible de la familia activa. */
  updateFamilyName(accessToken: string, nombre: string): Promise<FamilyRecord> {
    return requestJson<FamilyRecord>("/api/family", accessToken, {
      method: "PATCH",
      body: JSON.stringify({ nombre }),
    });
  },

  /** Crea un perfil de cuidador dentro de la familia (RF23, máx. 6 activos). */
  createProfile(
    accessToken: string,
    payload: { nombre: string; avatar: string }
  ): Promise<CaregiverProfileRecord> {
    return requestJson<CaregiverProfileRecord>("/api/family/profiles", accessToken, {
      method: "POST",
      body: JSON.stringify(payload),
    });
  },

  /** Edita nombre o avatar de un perfil de cuidador existente. */
  updateProfile(
    accessToken: string,
    profileId: string,
    payload: { nombre: string; avatar: string }
  ): Promise<CaregiverProfileRecord> {
    return requestJson<CaregiverProfileRecord>(`/api/family/profiles/${profileId}`, accessToken, {
      method: "PATCH",
      body: JSON.stringify(payload),
    });
  },

  /** Realiza baja lógica de un perfil de cuidador (RF23). */
  softDeleteProfile(accessToken: string, profileId: string): Promise<boolean> {
    return requestJson<boolean>(`/api/family/profiles/${profileId}`, accessToken, {
      method: "DELETE",
    });
  },

  /** Genera enlace de invitación de un solo uso válido por 24 horas (RF02). */
  createInvitation(accessToken: string): Promise<CreateInvitationResponse> {
    return requestJson<CreateInvitationResponse>("/api/family/invitations", accessToken, {
      method: "POST",
    });
  },

  /** Acepta una invitación familiar usando el token recibido en URL (RF02). */
  acceptInvitation(
    accessToken: string,
    payload: { token: string; nombrePerfil?: string; avatar?: string }
  ): Promise<AccountContextResponse> {
    return requestJson<AccountContextResponse>("/api/family/invitations/accept", accessToken, {
      method: "POST",
      body: JSON.stringify(payload),
    });
  },

  /** Revoca inmediatamente el acceso de un miembro secundario (RF22). */
  revokeMember(accessToken: string, memberUserId: string): Promise<boolean> {
    return requestJson<boolean>(`/api/family/members/${memberUserId}`, accessToken, {
      method: "DELETE",
    });
  },

  /** Lista los eventos recientes de auditoría de seguridad familiar (RNF03). */
  listSecurityAudit(accessToken: string): Promise<SecurityAuditEntry[]> {
    return requestJson<SecurityAuditEntry[]>("/api/family/security-audit", accessToken);
  },

  /** Consulta el estado de salud del servidor HTTP/TCP local sin requerir token. */
  async getHealthStatus(signal?: AbortSignal): Promise<HealthStatusResponse> {
    const response = await fetch(`${apiBaseUrl}/health`, { signal });
    if (!response.ok) {
      throw new ApiError(response.status, `Error HTTP ${response.status}`);
    }
    return (await response.json()) as HealthStatusResponse;
  },
};
