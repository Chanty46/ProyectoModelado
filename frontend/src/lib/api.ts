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

export class ApiError extends Error {
  public readonly status: number;

  constructor(status: number, message: string) {
    super(message);
    this.status = status;
  }
}

const apiBaseUrl = (import.meta.env.VITE_API_BASE_URL ?? "http://localhost:5080").replace(/\/$/, "");

async function requestJson<T>(
  path: string,
  accessToken: string,
  init?: RequestInit
): Promise<T> {
  const response = await fetch(`${apiBaseUrl}${path}`, {
    ...init,
    headers: {
      "Content-Type": "application/json",
      Authorization: `Bearer ${accessToken}`,
      ...(init?.headers ?? {}),
    },
  });

  if (!response.ok) {
    let errorMessage = `Error HTTP ${response.status}`;
    try {
      const payload = (await response.json()) as { error?: string };
      if (payload.error) {
        errorMessage = payload.error;
      }
    } catch {
      // Ignorar si no es JSON
    }
    throw new ApiError(response.status, errorMessage);
  }

  return (await response.json()) as T;
}

export const accountApi = {
  getMe(accessToken: string): Promise<AccountContextResponse> {
    return requestJson<AccountContextResponse>("/api/account/me", accessToken);
  },

  bootstrap(
    accessToken: string,
    payload: { nombreFamilia?: string; nombrePerfilInicial?: string; avatarInicial?: string }
  ): Promise<AccountContextResponse> {
    return requestJson<AccountContextResponse>("/api/account/bootstrap", accessToken, {
      method: "POST",
      body: JSON.stringify(payload),
    });
  },

  updateFamilyName(accessToken: string, nombre: string): Promise<FamilyRecord> {
    return requestJson<FamilyRecord>("/api/family", accessToken, {
      method: "PATCH",
      body: JSON.stringify({ nombre }),
    });
  },

  createProfile(
    accessToken: string,
    payload: { nombre: string; avatar: string }
  ): Promise<CaregiverProfileRecord> {
    return requestJson<CaregiverProfileRecord>("/api/family/profiles", accessToken, {
      method: "POST",
      body: JSON.stringify(payload),
    });
  },

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

  softDeleteProfile(accessToken: string, profileId: string): Promise<boolean> {
    return requestJson<boolean>(`/api/family/profiles/${profileId}`, accessToken, {
      method: "DELETE",
    });
  },

  createInvitation(accessToken: string): Promise<CreateInvitationResponse> {
    return requestJson<CreateInvitationResponse>("/api/family/invitations", accessToken, {
      method: "POST",
    });
  },

  acceptInvitation(
    accessToken: string,
    payload: { token: string; nombrePerfil?: string; avatar?: string }
  ): Promise<AccountContextResponse> {
    return requestJson<AccountContextResponse>("/api/family/invitations/accept", accessToken, {
      method: "POST",
      body: JSON.stringify(payload),
    });
  },

  revokeMember(accessToken: string, memberUserId: string): Promise<boolean> {
    return requestJson<boolean>(`/api/family/members/${memberUserId}`, accessToken, {
      method: "DELETE",
    });
  },
};
