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
    pcm[i] = Math.round(128 + clamped * 110);
  }

  return pcm;
}
