import { requestJson } from "./client";

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
  deepLinkUrl?: string | null;
}

export interface TelegramBotStatusResponse {
  tokenConfigured: boolean;
  mode: string;
  botReachable: boolean;
  botUsername: string;
  resolvedUsername: string | null;
  userLinked: boolean;
  userChatId: number | null;
  note: string;
}

export interface TelegramTestMessageResponse {
  sent: boolean;
  chatId: number | null;
  mode: string;
  message: string;
}

export interface FeedingReminderRecord {
  id: string;
  familyId: string;
  intervaloHoras: number;
  activo: boolean;
  ultimoEnvio: string | null;
  actualizadoEn: string;
}

/** Operaciones de vinculación con Telegram y recordatorios periódicos de alimentación. */
export const telegramEndpoints = {
  /** Consulta el estado de vinculación de Telegram para el usuario actual (RF38). */
  getTelegramLink(accessToken: string): Promise<TelegramLinkStatusResponse> {
    return requestJson<TelegramLinkStatusResponse>("/api/telegram/link", accessToken);
  },

  /** Consulta el estado operativo del bot de Telegram en el backend (RF19). */
  getTelegramStatus(
    accessToken: string,
    signal?: AbortSignal
  ): Promise<TelegramBotStatusResponse> {
    return requestJson<TelegramBotStatusResponse>("/api/telegram/status", accessToken, { signal });
  },

  /** Envía un mensaje de prueba al chat de Telegram vinculado del usuario. */
  sendTelegramTest(accessToken: string): Promise<TelegramTestMessageResponse> {
    return requestJson<TelegramTestMessageResponse>("/api/telegram/test", accessToken, {
      method: "POST",
    });
  },

  /** Genera un código temporal de un solo uso para vincular el bot vía /start (RF38). */
  generateTelegramLinkCode(accessToken: string): Promise<GenerateTelegramCodeResponse> {
    return requestJson<GenerateTelegramCodeResponse>("/api/telegram/link-code", accessToken, {
      method: "POST",
    });
  },

  /** Desvincula la cuenta de Telegram del usuario actual (RF39). */
  unlinkTelegram(accessToken: string): Promise<boolean> {
    return requestJson<boolean>("/api/telegram/link", accessToken, {
      method: "DELETE",
    });
  },

  /** Obtiene la configuración actual del recordatorio de tomas de la familia (RF18). */
  getFeedingReminder(accessToken: string): Promise<FeedingReminderRecord> {
    return requestJson<FeedingReminderRecord>("/api/reminders", accessToken);
  },

  /** Actualiza el intervalo en horas y activación del recordatorio de alimentación (RF18). */
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
