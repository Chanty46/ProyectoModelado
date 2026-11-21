/**
 * Comandos del móvil colgante (RF13) que la web inserta en `public.comandos`.
 * La Pico W del móvil los lee con `dispositivo_comandos_pendientes` y los marca
 * `confirmado` o `error` con `dispositivo_confirmar_comando`.
 * Los valores deben coincidir con el firmware y con la política RLS de inserción.
 */

export const MOBILE_PATTERNS = ["secuencia", "parpadeo", "columnas", "filas", "espiral"] as const;
export type MobilePattern = (typeof MOBILE_PATTERNS)[number];

export const MOBILE_SPEEDS = ["lenta", "media", "rapida"] as const;
export type MobileSpeed = (typeof MOBILE_SPEEDS)[number];

export const MOBILE_PATTERN_LABELS: Record<MobilePattern, string> = {
  secuencia: "Secuencia",
  parpadeo: "Parpadeo",
  columnas: "Columnas",
  filas: "Filas",
  espiral: "Espiral",
};

export const MOBILE_SPEED_LABELS: Record<MobileSpeed, string> = {
  lenta: "Lenta",
  media: "Media",
  rapida: "Rápida",
};

export const MOBILE_DURATION_MIN = 1;
export const MOBILE_DURATION_MAX = 30;

/** La cuna se considera desconectada si su último heartbeat tiene más de 60 s. */
export const CRIB_OFFLINE_AFTER_MS = 60_000;

/** Tiempo máximo esperando que la cuna confirme un comando. */
export const COMMAND_TIMEOUT_MS = 10_000;

export type MobileCommandType = "movil_on" | "movil_off";

export interface MobileOnPayload {
  patron: MobilePattern;
  velocidad: MobileSpeed;
  duracion_min: number;
}

export type MobileCommandPayload = MobileOnPayload | Record<string, never>;

/**
 * Valida la duración escrita en el formulario: entero de 1 a 30.
 * Devuelve el número o un mensaje de error para mostrar.
 */
export function parseMobileDuration(raw: string): { ok: true; value: number } | { ok: false; error: string } {
  const trimmed = raw.trim();
  if (!/^\d+$/.test(trimmed)) {
    return { ok: false, error: `Escribe un número entero de ${MOBILE_DURATION_MIN} a ${MOBILE_DURATION_MAX}.` };
  }
  const value = Number(trimmed);
  if (value < MOBILE_DURATION_MIN || value > MOBILE_DURATION_MAX) {
    return { ok: false, error: `La duración debe estar entre ${MOBILE_DURATION_MIN} y ${MOBILE_DURATION_MAX} minutos.` };
  }
  return { ok: true, value };
}

export function buildMobileOnPayload(patron: MobilePattern, velocidad: MobileSpeed, duracionMin: number): MobileOnPayload {
  return { patron, velocidad, duracion_min: duracionMin };
}

export function isMobilePattern(value: string): value is MobilePattern {
  return (MOBILE_PATTERNS as readonly string[]).includes(value);
}

export function isMobileSpeed(value: string): value is MobileSpeed {
  return (MOBILE_SPEEDS as readonly string[]).includes(value);
}

/** En línea si `ultimo_heartbeat` existe y no tiene más de 60 s respecto a `nowMs`. */
export function isCribOnline(ultimoHeartbeat: string | null | undefined, nowMs: number): boolean {
  if (!ultimoHeartbeat) {
    return false;
  }
  const ts = Date.parse(ultimoHeartbeat);
  if (Number.isNaN(ts)) {
    return false;
  }
  return nowMs - ts <= CRIB_OFFLINE_AFTER_MS;
}

/** Texto corto "hace X s / min" para el último heartbeat. */
export function describeHeartbeatAge(ultimoHeartbeat: string | null | undefined, nowMs: number): string {
  if (!ultimoHeartbeat) {
    return "sin señal registrada";
  }
  const ts = Date.parse(ultimoHeartbeat);
  if (Number.isNaN(ts)) {
    return "sin señal registrada";
  }
  const seconds = Math.max(0, Math.round((nowMs - ts) / 1000));
  if (seconds < 60) {
    return `última señal hace ${seconds} s`;
  }
  const minutes = Math.floor(seconds / 60);
  if (minutes < 60) {
    return `última señal hace ${minutes} min`;
  }
  return `última señal hace ${Math.floor(minutes / 60)} h`;
}
