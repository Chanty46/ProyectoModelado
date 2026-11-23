import { useEffect, useState, type FormEvent, type ReactNode } from "react";
import { CheckCircle2, Loader2, Sparkles, Square, WifiOff, Wifi, XCircle, Clock } from "lucide-react";
import { computeMobileLedFrame } from "../../lib/formatters";
import {
  MOBILE_DURATION_MAX,
  MOBILE_DURATION_MIN,
  MOBILE_PATTERN_LABELS,
  MOBILE_PATTERNS,
  MOBILE_SPEED_LABELS,
  MOBILE_SPEEDS,
  buildMobileOnPayload,
  describeHeartbeatAge,
  isMobilePattern,
  isMobileSpeed,
  parseMobileDuration,
  type MobilePattern,
  type MobileSpeed,
} from "../../lib/mobileCommands";
import { useMobileCommand, type MobileCommandStatus } from "../../hooks/useMobileCommand";
import { useCribHeartbeat } from "../../hooks/useCribHeartbeat";

interface MobileControlCardProps {
  cunaId: string;
}

// Mismos intervalos que VELOCIDADES_MS en firmware/movil_supabase/main.py
const PREVIEW_INTERVAL_MS: Record<MobileSpeed, number> = { lenta: 600, media: 300, rapida: 120 };

/**
 * Control del móvil colgante de 9 LEDs (RF13).
 * Inserta comandos `movil_on` / `movil_off` en `public.comandos` y muestra la confirmación
 * de la Pico W en tiempo real, junto con el estado En línea de la cuna (`ultimo_heartbeat`).
 */
export function MobileControlCard({ cunaId }: MobileControlCardProps) {
  const [pattern, setPattern] = useState<MobilePattern>("secuencia");
  const [speed, setSpeed] = useState<MobileSpeed>("media");
  const [durationRaw, setDurationRaw] = useState<string>("5");
  const [previewStep, setPreviewStep] = useState<number>(0);

  const { status, send } = useMobileCommand(cunaId);
  const heartbeat = useCribHeartbeat(cunaId);

  const duration = parseMobileDuration(durationRaw);
  const isSending = status.kind === "sending";

  useEffect(() => {
    const timer = window.setInterval(() => setPreviewStep((prev) => (prev + 1) % 90), PREVIEW_INTERVAL_MS[speed]);
    return () => window.clearInterval(timer);
  }, [speed]);

  function handleStart(e: FormEvent) {
    e.preventDefault();
    if (!duration.ok || isSending) return;
    void send("movil_on", buildMobileOnPayload(pattern, speed, duration.value));
  }

  function handleStop() {
    if (isSending) return;
    void send("movil_off", {});
  }

  const leds = computeMobileLedFrame(pattern, previewStep, 9);

  return (
    <div className="rounded-xl border border-slate-200 bg-white p-4 shadow-sm space-y-3">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <div className="flex items-center gap-2">
          <Sparkles className="h-4 w-4 text-indigo-600" aria-hidden="true" />
          <h4 className="text-xs font-semibold text-slate-900">Móvil colgante de 9 LEDs (RF13)</h4>
        </div>
        <ConnectionBadge
          loaded={heartbeat.loaded}
          online={heartbeat.online}
          detail={describeHeartbeatAge(heartbeat.ultimoHeartbeat, heartbeat.nowMs)}
        />
      </div>

      <div className="flex items-center gap-3 rounded-lg border border-slate-100 bg-slate-900 px-3 py-2">
        <div
          className="grid grid-cols-3 gap-1.5"
          role="img"
          aria-label="Vista previa aproximada de los 9 LEDs del móvil"
        >
          {leds.map((on, idx) => (
            <span
              key={idx}
              className={`h-2.5 w-2.5 rounded-full transition-all duration-150 ${
                on ? "bg-amber-300 shadow-[0_0_8px_rgba(252,211,77,0.9)]" : "bg-slate-700"
              }`}
            />
          ))}
        </div>
        <span className="text-[11px] text-slate-300">
          Vista previa aproximada · {MOBILE_PATTERN_LABELS[pattern]}, {MOBILE_SPEED_LABELS[speed].toLowerCase()}
        </span>
      </div>

      <form onSubmit={handleStart} className="space-y-2" noValidate>
        <div className="grid grid-cols-1 gap-2 sm:grid-cols-3">
          <label className="space-y-1 text-[11px] font-medium text-slate-600">
            Patrón
            <select
              value={pattern}
              onChange={(e) => isMobilePattern(e.target.value) && setPattern(e.target.value)}
              aria-label="Patrón del móvil"
              className="block w-full rounded-lg border border-slate-300 px-2 py-1.5 text-xs font-normal text-slate-900"
            >
              {MOBILE_PATTERNS.map((p) => (
                <option key={p} value={p}>
                  {MOBILE_PATTERN_LABELS[p]}
                </option>
              ))}
            </select>
          </label>
          <label className="space-y-1 text-[11px] font-medium text-slate-600">
            Velocidad
            <select
              value={speed}
              onChange={(e) => isMobileSpeed(e.target.value) && setSpeed(e.target.value)}
              aria-label="Velocidad del móvil"
              className="block w-full rounded-lg border border-slate-300 px-2 py-1.5 text-xs font-normal text-slate-900"
            >
              {MOBILE_SPEEDS.map((s) => (
                <option key={s} value={s}>
                  {MOBILE_SPEED_LABELS[s]}
                </option>
              ))}
            </select>
          </label>
          <label className="space-y-1 text-[11px] font-medium text-slate-600">
            Duración (min)
            <input
              type="number"
              inputMode="numeric"
              min={MOBILE_DURATION_MIN}
              max={MOBILE_DURATION_MAX}
              step={1}
              value={durationRaw}
              onChange={(e) => setDurationRaw(e.target.value)}
              aria-label="Duración del móvil en minutos"
              aria-invalid={!duration.ok}
              aria-describedby={!duration.ok ? "mobile-duration-error" : undefined}
              className={`block w-full rounded-lg border px-2 py-1.5 text-xs font-normal text-slate-900 ${
                duration.ok ? "border-slate-300" : "border-red-400 bg-red-50"
              }`}
            />
          </label>
        </div>
        {!duration.ok && (
          <p id="mobile-duration-error" className="text-[11px] text-red-700">
            {duration.error}
          </p>
        )}

        <div className="flex flex-wrap items-center gap-2">
          <button
            type="submit"
            disabled={!duration.ok || isSending}
            className="inline-flex items-center gap-1.5 rounded-lg bg-indigo-600 px-3 py-1.5 text-xs font-medium text-white hover:bg-indigo-700 disabled:opacity-50 transition-colors"
          >
            <Sparkles className="h-3.5 w-3.5" aria-hidden="true" />
            Encender móvil
          </button>
          <button
            type="button"
            onClick={handleStop}
            disabled={isSending}
            className="inline-flex items-center gap-1.5 rounded-lg border border-slate-300 bg-white px-3 py-1.5 text-xs font-medium text-slate-700 hover:bg-slate-100 disabled:opacity-50 transition-colors"
          >
            <Square className="h-3.5 w-3.5" aria-hidden="true" />
            Detener móvil
          </button>
          <CommandStatusLine status={status} />
        </div>
      </form>
    </div>
  );
}

function ConnectionBadge({ loaded, online, detail }: { loaded: boolean; online: boolean; detail: string }) {
  if (!loaded) {
    return <span className="text-[10px] text-slate-500">Comprobando conexión…</span>;
  }
  return (
    <span
      title={detail}
      className={`inline-flex items-center gap-1 rounded-full px-2 py-0.5 text-[10px] font-semibold ${
        online ? "bg-emerald-50 text-emerald-700" : "bg-slate-100 text-slate-600"
      }`}
    >
      {online ? <Wifi className="h-3 w-3" aria-hidden="true" /> : <WifiOff className="h-3 w-3" aria-hidden="true" />}
      {online ? "En línea" : "Desconectada"}
      <span className="font-normal opacity-80">· {detail}</span>
    </span>
  );
}

function CommandStatusLine({ status }: { status: MobileCommandStatus }) {
  let content: ReactNode = null;
  if (status.kind === "sending") {
    content = (
      <span className="inline-flex items-center gap-1 text-slate-600">
        <Loader2 className="h-3.5 w-3.5 animate-spin" aria-hidden="true" />
        Enviando…
      </span>
    );
  } else if (status.kind === "confirmed") {
    content = (
      <span className="inline-flex items-center gap-1 text-emerald-700">
        <CheckCircle2 className="h-3.5 w-3.5" aria-hidden="true" />
        Confirmado
        <span className="text-slate-500">({status.tipo === "movil_on" ? "móvil encendido" : "móvil detenido"})</span>
      </span>
    );
  } else if (status.kind === "error") {
    content = (
      <span className="inline-flex items-center gap-1 text-red-700">
        <XCircle className="h-3.5 w-3.5" aria-hidden="true" />
        {status.message}
      </span>
    );
  } else if (status.kind === "timeout") {
    content = (
      <span className="inline-flex items-center gap-1 text-amber-700">
        <Clock className="h-3.5 w-3.5" aria-hidden="true" />
        La cuna no respondió
      </span>
    );
  }

  return (
    <span role="status" aria-live="polite" className="text-xs font-medium">
      {content}
    </span>
  );
}
