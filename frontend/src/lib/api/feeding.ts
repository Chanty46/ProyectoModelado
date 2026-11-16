import { requestJson } from "./client";

export interface FeedingSessionRecord {
  id: string;
  bebeId: string;
  perfilId: string | null;
  masaInicialG: number;
  masaFinalG: number | null;
  mlConsumidos: number | null;
  tempC: number | null;
  alertaTemperatura: boolean;
  inicio: string;
  fin: string | null;
}

export interface SleepSessionRecord {
  id: string;
  bebeId: string;
  cunaId: string;
  tipo: "siesta" | "noche";
  inicio: string;
  fin: string | null;
  movimientoPromedio: number | null;
  interrupciones: number;
}

export interface SongCatalogItem {
  id: string;
  codigo: string;
  nombre: string;
  duracionSegundos: number;
  notasJson: string;
}

export interface ToyUsageRecord {
  id: string;
  cunaId: string;
  tipo: string;
  perfilId: string | null;
  inicio: string;
  duracionSegundos: number;
  aciertos: number | null;
  patron: string | null;
  velocidad: string | null;
}

export interface ToysAndPeripheralsStatusResponse {
  historialUso: ToyUsageRecord[];
  bateriaJuguetePct: number | null;
  alertaBateriaBajaJuguete: boolean;
  bateriaBiberonPct: number | null;
  ultimaMasaBiberonG: number | null;
  ultimaTempBiberonC: number | null;
}

export interface ExportHistoryResponse {
  fileName: string;
  contentType: string;
  contentBase64: string;
  sleepSessionsCount: number;
  feedingSessionsCount: number;
  elapsedMs: number;
}

/** Operaciones de alimentación, sueño, canciones PWM, móvil LED, juguetes y exportación. */
export const feedingEndpoints = {
  /** Inicia una sesión de alimentación registrando masa inicial y temperatura IR (RF15/RF37). */
  startFeedingSession(
    accessToken: string,
    payload: {
      bebeId: string;
      masaInicialG: number;
      tempC?: number | null;
      perfilId?: string | null;
    }
  ): Promise<FeedingSessionRecord> {
    return requestJson<FeedingSessionRecord>("/api/feeding/sessions/start", accessToken, {
      method: "POST",
      body: JSON.stringify(payload),
    });
  },

  /** Finaliza la toma en curso y calcula mililitros ingeridos con densidad 1.03 g/ml (RF17). */
  finishFeedingSession(
    accessToken: string,
    sessionId: string,
    payload: { masaFinalG: number; perfilId?: string | null }
  ): Promise<FeedingSessionRecord> {
    return requestJson<FeedingSessionRecord>(
      `/api/feeding/sessions/${sessionId}/finish`,
      accessToken,
      {
        method: "POST",
        body: JSON.stringify(payload),
      }
    );
  },

  /** Consulta el historial de sesiones de alimentación del bebé filtrado por rango de fechas (RF31). */
  getFeedingHistory(
    accessToken: string,
    bebeId: string,
    desde?: string,
    hasta?: string
  ): Promise<FeedingSessionRecord[]> {
    const params = new URLSearchParams();
    if (desde) params.set("desde", desde);
    if (hasta) params.set("hasta", hasta);
    const qs = params.toString() ? `?${params.toString()}` : "";
    return requestJson<FeedingSessionRecord[]>(
      `/api/babies/${bebeId}/history/feeding${qs}`,
      accessToken
    );
  },

  /** Registra manualmente o por prueba una sesión de sueño (RF30). */
  createSleepSession(
    accessToken: string,
    bebeId: string,
    payload: {
      cunaId: string;
      tipo: "siesta" | "noche";
      inicio: string;
      fin?: string | null;
      movimientoPromedio?: number | null;
      interrupciones?: number;
      perfilId?: string | null;
    }
  ): Promise<SleepSessionRecord> {
    return requestJson<SleepSessionRecord>(
      `/api/babies/${bebeId}/history/sleep`,
      accessToken,
      {
        method: "POST",
        body: JSON.stringify(payload),
      }
    );
  },

  /** Consulta el historial de sueño del bebé filtrado por fechas y tipo (RF30). */
  getSleepHistory(
    accessToken: string,
    bebeId: string,
    desde?: string,
    hasta?: string,
    tipo?: string
  ): Promise<SleepSessionRecord[]> {
    const params = new URLSearchParams();
    if (desde) params.set("desde", desde);
    if (hasta) params.set("hasta", hasta);
    if (tipo) params.set("tipo", tipo);
    const qs = params.toString() ? `?${params.toString()}` : "";
    return requestJson<SleepSessionRecord[]>(
      `/api/babies/${bebeId}/history/sleep${qs}`,
      accessToken
    );
  },

  /** Exporta el historial consolidado de sueño y tomas en formato CSV o PDF (RF32). */
  exportBabyHistory(
    accessToken: string,
    bebeId: string,
    payload: {
      formato: "csv" | "pdf";
      desde?: string | null;
      hasta?: string | null;
      perfilId?: string | null;
    }
  ): Promise<ExportHistoryResponse> {
    return requestJson<ExportHistoryResponse>(
      `/api/babies/${bebeId}/history/export`,
      accessToken,
      {
        method: "POST",
        body: JSON.stringify(payload),
      }
    );
  },

  /** Obtiene el catálogo de melodías disponibles para el altavoz PWM de la cuna (RF12). */
  listSongs(accessToken: string, cunaId: string): Promise<SongCatalogItem[]> {
    return requestJson<SongCatalogItem[]>(`/api/cribs/${cunaId}/songs`, accessToken);
  },

  /** Ordena reproducir o detener una melodía PWM en la Unidad Central (RF12). */
  playSong(
    accessToken: string,
    cunaId: string,
    payload: { codigo: string; accion: "play" | "stop"; perfilId?: string | null }
  ): Promise<boolean> {
    return requestJson<boolean>(`/api/cribs/${cunaId}/songs/play`, accessToken, {
      method: "POST",
      body: JSON.stringify(payload),
    });
  },

  /** Activa el móvil colgante de 9 LEDs con patrón, velocidad y duración indicados (RF13). */
  playMobile(
    accessToken: string,
    cunaId: string,
    payload: {
      patron: string;
      velocidad: "lenta" | "media" | "rapida";
      duracionSegundos: number;
      perfilId?: string | null;
    }
  ): Promise<ToyUsageRecord> {
    return requestJson<ToyUsageRecord>(`/api/cribs/${cunaId}/mobile/play`, accessToken, {
      method: "POST",
      body: JSON.stringify(payload),
    });
  },

  /** Consulta estado de batería de periféricos y uso reciente de móvil y juguete (RF33–RF36). */
  getToysStatus(
    accessToken: string,
    cunaId: string
  ): Promise<ToysAndPeripheralsStatusResponse> {
    return requestJson<ToysAndPeripheralsStatusResponse>(
      `/api/cribs/${cunaId}/toys`,
      accessToken
    );
  },
};
