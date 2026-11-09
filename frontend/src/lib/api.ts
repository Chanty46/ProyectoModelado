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

export interface IntercomChunkResponse {
  delivered: boolean;
  sampleCount: number;
  sampleRateHz: number;
  maxSafeGainApplied: number;
}

export interface CribRecord {
  id: string;
  familyId: string;
  nombre: string;
  estadoConexion: string;
  modoOperacion: "monitoreo" | "atencion" | "alarma";
  modoAtencionExpiraEn: string | null;
  luzEncendida: boolean;
  luzNivel: number;
  escalaMasa: number;
  ultimoHeartbeat: string | null;
  creadoEn: string;
}

export interface CribConfig {
  cunaId: string;
  familyId: string;
  escalaMasa: number;
  tempMin: number;
  tempMax: number;
  humMin: number;
  humMax: number;
  movimientoUmbral: number;
  movimientoAlertaActiva: boolean;
  ruidoMasaPiso: number;
}

export interface TelemetryRecord {
  cunaId: string;
  dispositivoId: string;
  ts: string;
  masaKg: number | null;
  indiceMovimiento: number | null;
  tempC: number | null;
  humedad: number | null;
  masaBiberonG: number | null;
  tempBiberonC: number | null;
  bateriaPct: number | null;
  seq: number;
}

export interface EventAlertRecord {
  id: string;
  cunaId: string;
  dispositivoId: string;
  tipo: string;
  severidad: "info" | "advertencia" | "critica";
  mensaje: string | null;
  datosJson: string | null;
  ts: string;
}

export interface LiveCribStatusResponse {
  cuna: CribRecord;
  umbrales: CribConfig;
  ultimaTelemetria: TelemetryRecord | null;
  telemetriaReciente: TelemetryRecord[];
  alertasRecientes: EventAlertRecord[];
  dispositivos: ClaimableDeviceRecord[];
  enVentanaGraciaRetiro: boolean;
}

export interface TelegramLinkStatusResponse {
  usuarioId: string;
  vinculado: boolean;
  chatId: number | null;
  vinculadoEn: string | null;
  codigoPendienteExpiraEn: string | null;
}

export interface GenerateTelegramCodeResponse {
  codigo: string;
  expiraEn: string;
  botUsername: string;
  comandoEjemplo: string;
}

export interface FeedingReminderRecord {
  id: string;
  familyId: string;
  intervaloHoras: number;
  activo: boolean;
  ultimoEnvio: string | null;
  actualizadoEn: string;
}

export interface BabyRecord {
  id: string;
  familyId: string;
  cunaId: string | null;
  nombre: string;
  fechaNacimiento: string;
  notas: string | null;
  creadoEn: string;
}

export interface ClaimableDeviceRecord {
  id: string;
  deviceId: string;
  cunaId: string | null;
  familyId: string | null;
  tipo: string;
  estado: string;
  bateriaPct: number | null;
  fwVersion: string | null;
}

export interface CribSummaryResponse {
  cuna: CribRecord;
  dispositivos: ClaimableDeviceRecord[];
  bebe: BabyRecord | null;
}

export interface OpenPairingWindowResponse {
  expiraEn: string;
  ventanaMinutos: number;
}

export interface ClaimDeviceResponse {
  deviceRowId: string;
  deviceId: string;
  tipo: string;
  cunaId: string;
  nombreCuna: string;
  deviceTokenOnce: string;
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

  sendIntercomChunk(
    accessToken: string,
    cunaId: string,
    payload: { sampleRateHz: number; seq: number; pcmBase64: string }
  ): Promise<IntercomChunkResponse> {
    return requestJson<IntercomChunkResponse>(
      `/api/cribs/${cunaId}/intercom/chunk`,
      accessToken,
      {
        method: "POST",
        body: JSON.stringify(payload),
      }
    );
  },

  openPairingWindow(accessToken: string): Promise<OpenPairingWindowResponse> {
    return requestJson<OpenPairingWindowResponse>("/api/pairing/window", accessToken, {
      method: "POST",
    });
  },

  claimDevice(
    accessToken: string,
    payload: {
      deviceId: string;
      codigoReclamo: string;
      cunaId?: string | null;
      nombreCuna?: string | null;
    }
  ): Promise<ClaimDeviceResponse> {
    return requestJson<ClaimDeviceResponse>("/api/pairing/claim", accessToken, {
      method: "POST",
      body: JSON.stringify(payload),
    });
  },

  listCribs(accessToken: string): Promise<CribSummaryResponse[]> {
    return requestJson<CribSummaryResponse[]>("/api/cribs", accessToken);
  },

  renameCrib(accessToken: string, cunaId: string, nombre: string): Promise<CribRecord> {
    return requestJson<CribRecord>(`/api/cribs/${cunaId}`, accessToken, {
      method: "PATCH",
      body: JSON.stringify({ nombre }),
    });
  },

  unlinkCrib(
    accessToken: string,
    cunaId: string,
    textoConfirmacion: string
  ): Promise<boolean> {
    return requestJson<boolean>(`/api/cribs/${cunaId}/unlink`, accessToken, {
      method: "POST",
      body: JSON.stringify({
        confirmacionExplicita: true,
        textoConfirmacion,
      }),
    });
  },

  listBabies(accessToken: string): Promise<BabyRecord[]> {
    return requestJson<BabyRecord[]>("/api/babies", accessToken);
  },

  createBaby(
    accessToken: string,
    payload: {
      cunaId: string;
      nombre: string;
      fechaNacimiento: string;
      notas?: string | null;
    }
  ): Promise<BabyRecord> {
    return requestJson<BabyRecord>("/api/babies", accessToken, {
      method: "POST",
      body: JSON.stringify(payload),
    });
  },

  updateBaby(
    accessToken: string,
    babyId: string,
    payload: {
      cunaId: string;
      nombre: string;
      fechaNacimiento: string;
      notas?: string | null;
    }
  ): Promise<BabyRecord> {
    return requestJson<BabyRecord>(`/api/babies/${babyId}`, accessToken, {
      method: "PATCH",
      body: JSON.stringify(payload),
    });
  },

  getLiveCribStatus(
    accessToken: string,
    cunaId: string
  ): Promise<LiveCribStatusResponse> {
    return requestJson<LiveCribStatusResponse>(`/api/cribs/${cunaId}/live`, accessToken);
  },

  setCribMode(
    accessToken: string,
    cunaId: string,
    payload: { modo: "monitoreo" | "atencion"; perfilId?: string | null }
  ): Promise<CribRecord> {
    return requestJson<CribRecord>(`/api/cribs/${cunaId}/mode`, accessToken, {
      method: "POST",
      body: JSON.stringify(payload),
    });
  },

  setCribLight(
    accessToken: string,
    cunaId: string,
    payload: { encendida: boolean; nivel: number; perfilId?: string | null }
  ): Promise<CribRecord> {
    return requestJson<CribRecord>(`/api/cribs/${cunaId}/light`, accessToken, {
      method: "POST",
      body: JSON.stringify(payload),
    });
  },

  getCribThresholds(accessToken: string, cunaId: string): Promise<CribConfig> {
    return requestJson<CribConfig>(`/api/cribs/${cunaId}/thresholds`, accessToken);
  },

  updateCribThresholds(
    accessToken: string,
    cunaId: string,
    payload: {
      tempMin: number;
      tempMax: number;
      humMin: number;
      humMax: number;
      movimientoUmbral: number;
      movimientoAlertaActiva: boolean;
      ruidoMasaPiso: number;
      escalaMasa: number;
      perfilId?: string | null;
    }
  ): Promise<CribConfig> {
    return requestJson<CribConfig>(`/api/cribs/${cunaId}/thresholds`, accessToken, {
      method: "PUT",
      body: JSON.stringify(payload),
    });
  },

  getTelegramLink(accessToken: string): Promise<TelegramLinkStatusResponse> {
    return requestJson<TelegramLinkStatusResponse>("/api/telegram/link", accessToken);
  },

  generateTelegramLinkCode(accessToken: string): Promise<GenerateTelegramCodeResponse> {
    return requestJson<GenerateTelegramCodeResponse>("/api/telegram/link-code", accessToken, {
      method: "POST",
    });
  },

  unlinkTelegram(accessToken: string): Promise<boolean> {
    return requestJson<boolean>("/api/telegram/link", accessToken, {
      method: "DELETE",
    });
  },

  getFeedingReminder(accessToken: string): Promise<FeedingReminderRecord> {
    return requestJson<FeedingReminderRecord>("/api/reminders", accessToken);
  },

  updateFeedingReminder(
    accessToken: string,
    payload: { intervaloHoras: number; activo: boolean; perfilId?: string | null }
  ): Promise<FeedingReminderRecord> {
    return requestJson<FeedingReminderRecord>("/api/reminders", accessToken, {
      method: "PUT",
      body: JSON.stringify(payload),
    });
  },
};
