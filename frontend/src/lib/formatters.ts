export type CribMode = "monitoreo" | "atencion" | "alarma";
export type FamilyRole = "principal" | "secundario" | null;

export interface RgbLedVisual {
  label: string;
  dotClass: string;
  badgeClass: string;
}

/** Calcula cuenta regresiva MM:SS hasta expiraEn, o null si no aplica o ya expiró. */
export function formatRemainingCountdown(expiresAtIso: string | null, nowMs: number = Date.now()): string | null {
  if (!expiresAtIso) {
    return null;
  }
  const targetMs = Date.parse(expiresAtIso);
  if (Number.isNaN(targetMs)) {
    return null;
  }
  const remainingSec = Math.max(0, Math.floor((targetMs - nowMs) / 1000));
  if (remainingSec <= 0) {
    return null;
  }
  const mins = Math.floor(remainingSec / 60);
  const secs = remainingSec % 60;
  return `${String(mins).padStart(2, "0")}:${String(secs).padStart(2, "0")}`;
}

/** Mapea el modo operativo de la Unidad Central a su estado visual de LED RGB físico. */
export function resolveRgbLedStatus(mode: CribMode): RgbLedVisual {
  switch (mode) {
    case "alarma":
      return {
        label: "LED Rojo (Alarma)",
        dotClass: "bg-red-500 animate-ping",
        badgeClass: "bg-red-50 text-red-700 border-red-200",
      };
    case "atencion":
      return {
        label: "LED Amarillo (Atención)",
        dotClass: "bg-amber-400",
        badgeClass: "bg-amber-50 text-amber-800 border-amber-200",
      };
    default:
      return {
        label: "LED Verde (Monitoreo)",
        dotClass: "bg-emerald-500",
        badgeClass: "bg-emerald-50 text-emerald-700 border-emerald-200",
      };
  }
}

/** Verifica si el usuario actual posee permisos de Usuario Principal (RF03). */
export function canManagePrincipalActions(userRole: FamilyRole): boolean {
  return userRole === "principal";
}

/** Construye URL oficial de enlace profundo de Telegram (/start CODE). */
export function buildTelegramDeepLink(botUsername: string, code: string): string {
  const cleanHandle = botUsername.trim().replace(/^@+/, "");
  return `https://t.me/${cleanHandle}?start=${encodeURIComponent(code.trim())}`;
}

/** Convierte muestras Float32 del navegador a PCM sin signo de 8 bits remuestreado a 8 kHz (RF11). */
export function downsampleFloatToPcm8(
  input: Float32Array,
  inputSampleRate: number,
  targetSampleRate: number = 8000,
  maxSamples: number = 1200
): Uint8Array {
  if (input.length === 0 || inputSampleRate <= 0 || targetSampleRate <= 0) {
    return new Uint8Array(0);
  }

  // Promedio por ventana (boxcar) para evitar aliasing básico al reducir de 44.1/48 kHz a 8 kHz
  const ratio = Math.max(1, inputSampleRate / targetSampleRate);
  const outputLength = Math.min(maxSamples, Math.max(1, Math.floor(input.length / ratio)));
  const pcm = new Uint8Array(outputLength);

  for (let i = 0; i < outputLength; i++) {
    const startIdx = Math.floor(i * ratio);
    const endIdx = Math.min(input.length, Math.floor((i + 1) * ratio));
    let sum = 0;
    let count = 0;
    for (let j = startIdx; j < endIdx; j++) {
      sum += input[j];
      count++;
    }
    const avg = count > 0 ? sum / count : input[Math.min(startIdx, input.length - 1)];
    const clamped = Math.max(-1, Math.min(1, avg));
    // Cuantización sin signo centrada en 128 con amplitud acotada (110) para proteger el rango <= 65 dBA
    pcm[i] = Math.round(128 + clamped * 110);
  }

  return pcm;
}

export interface ComfortBandEvaluation {
  status: "ok" | "low" | "high" | "unknown";
  label: string;
  barPercent: number;
}

/** Evalúa si una lectura física está dentro de su rango seguro y calcula su posición visual (0–100 %). */
export function evaluateComfortBand(
  value: number | null | undefined,
  min: number,
  max: number
): ComfortBandEvaluation {
  if (value === null || value === undefined || Number.isNaN(value) || max <= min) {
    return { status: "unknown", label: "Sin lectura", barPercent: 0 };
  }

  const span = max - min;
  const paddedMin = min - span * 0.35;
  const paddedMax = max + span * 0.35;
  const pct = Math.max(4, Math.min(96, ((value - paddedMin) / (paddedMax - paddedMin)) * 100));

  if (value < min) {
    return { status: "low", label: "Por debajo del umbral", barPercent: Math.round(pct) };
  }
  if (value > max) {
    return { status: "high", label: "Por encima del umbral", barPercent: Math.round(pct) };
  }
  return { status: "ok", label: "En rango seguro", barPercent: Math.round(pct) };
}

/** Calcula volumen estimado en ml usando densidad 1.03 g/ml (RF17). */
export function estimateMilkMl(initialMassG: number, finalMassG: number): number {
  if (Number.isNaN(initialMassG) || Number.isNaN(finalMassG)) {
    return 0;
  }
  const diff = Math.max(0, initialMassG - finalMassG);
  return Math.round((diff / 1.03) * 10) / 10;
}

/** Parsea un campo numérico de formulario devolviendo null si está vacío, tiene espacios o no es finito. */
export function parseOptionalFiniteNumber(raw: string): number | null {
  const trimmed = raw.trim();
  if (trimmed.length === 0) {
    return null;
  }
  const parsed = Number(trimmed);
  return Number.isFinite(parsed) ? parsed : null;
}

/** Orden de recorrido en espiral sobre la cuadrícula 3×3 (índices fila*3 + columna). */
const SPIRAL_ORDER_3X3 = [0, 1, 2, 5, 8, 7, 6, 3, 4];

/**
 * Vista previa aproximada de los 9 LEDs del móvil colgante (RF13) como cuadrícula 3×3.
 * Solo es una animación orientativa en la web: el patrón real lo ejecuta el firmware.
 * Patrones: secuencia, parpadeo, columnas, filas, espiral.
 */
export function computeMobileLedFrame(pattern: string, step: number, count: number = 9): boolean[] {
  const safeCount = Math.max(1, count);
  const frame = new Array<boolean>(safeCount).fill(false);
  const safeStep = Math.max(0, Math.floor(step));
  const side = Math.max(1, Math.round(Math.sqrt(safeCount)));

  if (pattern === "parpadeo") {
    return frame.fill(safeStep % 2 === 0);
  }

  if (pattern === "columnas") {
    const col = safeStep % side;
    for (let i = 0; i < safeCount; i++) {
      frame[i] = i % side === col;
    }
    return frame;
  }

  if (pattern === "filas") {
    const row = safeStep % side;
    for (let i = 0; i < safeCount; i++) {
      frame[i] = Math.floor(i / side) === row;
    }
    return frame;
  }

  if (pattern === "espiral" && safeCount === 9) {
    const lit = safeStep % (SPIRAL_ORDER_3X3.length + 1);
    for (let i = 0; i < lit; i++) {
      frame[SPIRAL_ORDER_3X3[i]] = true;
    }
    return frame;
  }

  // Por defecto: secuencia (un LED a la vez, recorriendo los 9 en orden)
  frame[safeStep % safeCount] = true;
  return frame;
}
