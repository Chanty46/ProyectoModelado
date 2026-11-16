/**
 * Punto de entrada unificado de la capa HTTP hacia el backend de Cuna Inteligente.
 * Re-exporta todos los tipos y agrupa los endpoints por dominio en `accountApi`
 * para mantener compatibilidad con componentes y espías de pruebas (`vi.spyOn`).
 */
import { accountEndpoints } from "./api/account";
import { cribsEndpoints } from "./api/cribs";
import { telemetryEndpoints } from "./api/telemetry";
import { feedingEndpoints } from "./api/feeding";
import { telegramEndpoints } from "./api/telegram";

export { ApiError, apiBaseUrl, requestJson } from "./api/client";

export type {
  FamilyRecord,
  UserMembership,
  CaregiverProfileRecord,
  AccountContextResponse,
  CreateInvitationResponse,
  SecurityAuditEntry,
  HealthStatusResponse,
} from "./api/account";

export type {
  CribRecord,
  BabyRecord,
  ClaimableDeviceRecord,
  CribSummaryResponse,
  OpenPairingWindowResponse,
  ClaimDeviceResponse,
} from "./api/cribs";

export type {
  IntercomChunkResponse,
  CribConfig,
  TelemetryRecord,
  EventAlertRecord,
  LiveCribStatusResponse,
} from "./api/telemetry";

export type {
  FeedingSessionRecord,
  SleepSessionRecord,
  SongCatalogItem,
  ToyUsageRecord,
  ToysAndPeripheralsStatusResponse,
  ExportHistoryResponse,
} from "./api/feeding";

export type {
  TelegramLinkStatusResponse,
  GenerateTelegramCodeResponse,
  TelegramBotStatusResponse,
  TelegramTestMessageResponse,
  FeedingReminderRecord,
} from "./api/telegram";

export {
  accountEndpoints,
  cribsEndpoints,
  telemetryEndpoints,
  feedingEndpoints,
  telegramEndpoints,
};

/** Cliente API unificado por compatibilidad con todos los módulos y suites de pruebas. */
export const accountApi = {
  ...accountEndpoints,
  ...cribsEndpoints,
  ...telemetryEndpoints,
  ...feedingEndpoints,
  ...telegramEndpoints,
};
