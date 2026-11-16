import { requestJson } from "./client";

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

/** Operaciones de emparejamiento de nodos Pico W, gestión de cunas y perfiles de bebés. */
export const cribsEndpoints = {
  /** Abre una ventana de emparejamiento de 10 minutos para reclamar dispositivos (RF24). */
  openPairingWindow(accessToken: string): Promise<OpenPairingWindowResponse> {
    return requestJson<OpenPairingWindowResponse>("/api/pairing/window", accessToken, {
      method: "POST",
    });
  },

  /** Reclama un dispositivo por su ID de fábrica y código único generando su token una vez (RF24). */
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

  /** Lista las cunas vinculadas a la familia junto con sus nodos y bebé asociado (RF25). */
  listCribs(accessToken: string): Promise<CribSummaryResponse[]> {
    return requestJson<CribSummaryResponse[]>("/api/cribs", accessToken);
  },

  /** Renombra una cuna de la familia (RF26). */
  renameCrib(accessToken: string, cunaId: string, nombre: string): Promise<CribRecord> {
    return requestJson<CribRecord>(`/api/cribs/${cunaId}`, accessToken, {
      method: "PATCH",
      body: JSON.stringify({ nombre }),
    });
  },

  /** Desvincula una cuna revocando los tokens de sus nodos con confirmación explícita (RF27). */
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

  /** Lista los perfiles de bebés registrados en la familia (RF28). */
  listBabies(accessToken: string): Promise<BabyRecord[]> {
    return requestJson<BabyRecord[]>("/api/babies", accessToken);
  },

  /** Registra un nuevo perfil de bebé asociado a una cuna (RF28). */
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

  /** Actualiza datos clínicos o asociación de cuna de un bebé (RF28). */
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
};
