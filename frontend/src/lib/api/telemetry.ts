import { requestJson } from "./client";
import type { ClaimableDeviceRecord, CribRecord } from "./cribs";

export interface IntercomChunkResponse {
  delivered: boolean;
  sampleCount: number;
  sampleRateHz: number;
  maxSafeGainApplied: number;
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

/** Operaciones de telemetría en vivo, control de modo/luz, umbrales e intercomunicador. */
export const telemetryEndpoints = {
  /** Obtiene el estado integral en vivo de la cuna, última telemetría, serie reciente y alertas. */
  getLiveCribStatus(
    accessToken: string,
    cunaId: string,
    signal?: AbortSignal
  ): Promise<LiveCribStatusResponse> {
    return requestJson<LiveCribStatusResponse>(`/api/cribs/${cunaId}/live`, accessToken, { signal });
  },

  /** Cambia el modo operativo entre supervisión (`monitoreo`) y pausa de 15 min (`atencion`) (RF05). */
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

  /** Ajusta encendido e intensidad PWM de la luz nocturna gradual (RF09). */
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

  /** Obtiene los umbrales ambientales, de movimiento y escala de masa vigentes (RF08). */
  getCribThresholds(accessToken: string, cunaId: string): Promise<CribConfig> {
    return requestJson<CribConfig>(`/api/cribs/${cunaId}/thresholds`, accessToken);
  },

  /** Actualiza y propaga por TCP los umbrales de confort, movimiento y calibración D14 (RF08/RF29). */
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

  /** Envía un bloque de audio PCM 8-bit hacia la Unidad Central (RF11, tope <= 65 dBA). */
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
};
