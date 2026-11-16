import { Activity, Baby, PauseCircle, PlayCircle, ShieldAlert } from "lucide-react";
import type { CribRecord } from "../../lib/api";
import { formatRemainingCountdown, resolveRgbLedStatus } from "../../lib/formatters";

interface CribStatusBannerProps {
  crib: CribRecord;
  babyPresent: boolean;
  enVentanaGraciaRetiro: boolean;
  countdownNowMs: number;
  onToggleMode: (targetMode: "monitoreo" | "atencion") => void;
}

/**
 * Gemelo visual superior del estado operativo de la cuna (RF04, RF05, RF09, RF20).
 * Muestra el estado del LED RGB físico, brillo de luz nocturna, cuenta regresiva de
 * Modo Atención (15 min), ventana de gracia de retiro (5 s) y botón de cambio de modo.
 */
export function CribStatusBanner({
  crib,
  babyPresent,
  enVentanaGraciaRetiro,
  countdownNowMs,
  onToggleMode,
}: CribStatusBannerProps) {
  const rgbVisual = resolveRgbLedStatus(crib.modoOperacion);
  const attentionCountdown =
    crib.modoOperacion === "atencion"
      ? formatRemainingCountdown(crib.modoAtencionExpiraEn, countdownNowMs)
      : null;
  const lampGlowOpacity = crib.luzEncendida ? Math.max(0.15, crib.luzNivel / 100) : 0;

  return (
    <div className="flex flex-wrap items-center justify-between gap-4 rounded-xl border border-slate-200 bg-white p-4 shadow-sm">
      <div className="flex items-center gap-3.5">
        {/* Indicador visual del estado de cuna + luz nocturna */}
        <div className="relative flex h-14 w-14 shrink-0 items-center justify-center rounded-2xl border border-slate-200 bg-slate-900 text-white overflow-hidden">
          {crib.luzEncendida && (
            <div
              aria-hidden="true"
              className="absolute inset-0 bg-amber-400 transition-opacity duration-500"
              style={{ opacity: lampGlowOpacity * 0.45 }}
            />
          )}
          <div className="relative z-10 flex flex-col items-center">
            {crib.modoOperacion === "alarma" ? (
              <ShieldAlert className="h-6 w-6 text-red-400 animate-soft-glow" aria-hidden="true" />
            ) : babyPresent ? (
              <Baby className="h-6 w-6 text-emerald-300" aria-hidden="true" />
            ) : (
              <Activity className="h-6 w-6 text-teal-300" aria-hidden="true" />
            )}
            <span
              className={`mt-1 h-1.5 w-6 rounded-full ${
                crib.modoOperacion === "alarma"
                  ? "bg-red-500"
                  : crib.modoOperacion === "atencion"
                  ? "bg-amber-400"
                  : "bg-emerald-400"
              }`}
            />
          </div>
        </div>

        <div>
          <div className="flex flex-wrap items-center gap-2">
            <span className="text-sm font-semibold text-slate-900">
              {crib.modoOperacion === "alarma"
                ? "ALARMA CRÍTICA DE RETIRO (RF04)"
                : crib.modoOperacion === "atencion"
                ? "Pausa de Seguridad — Modo Atención (RF05)"
                : "Supervisión Perimetral Activa (Monitoreo)"}
            </span>
            <span
              className={`inline-flex items-center gap-1.5 rounded-full border px-2 py-0.5 text-[11px] font-medium ${rgbVisual.badgeClass}`}
            >
              <span className={`h-2 w-2 rounded-full ${rgbVisual.dotClass}`} aria-hidden="true" />
              {rgbVisual.label}
            </span>
            {attentionCountdown && (
              <span className="rounded-full bg-amber-100 px-2 py-0.5 font-mono text-[11px] font-semibold text-amber-900">
                Restante: {attentionCountdown}
              </span>
            )}
            <span
              className={`rounded-full px-2 py-0.5 text-[11px] font-medium ${
                crib.estadoConexion === "en_linea"
                  ? "bg-emerald-50 text-emerald-700"
                  : "bg-red-100 text-red-800"
              }`}
            >
              {crib.estadoConexion === "en_linea"
                ? "Central en línea"
                : "Desconectada (Watchdog RF20)"}
            </span>
            {enVentanaGraciaRetiro && (
              <span className="rounded-full bg-amber-100 px-2 py-0.5 text-[11px] font-semibold text-amber-900 animate-pulse">
                Ventana de gracia de retiro (5 s)...
              </span>
            )}
          </div>
          <p className="text-xs text-slate-500 mt-1">
            {crib.modoOperacion === "atencion" && crib.modoAtencionExpiraEn
              ? `Alarma de retiro inhibida hasta las ${new Date(crib.modoAtencionExpiraEn).toLocaleTimeString()} (15 min)`
              : "Retiro imprevisto activa ventana de gracia de 5 s antes de sonar alarma en Central."}
          </p>
        </div>
      </div>

      <div className="flex flex-wrap items-center gap-2">
        {crib.modoOperacion === "atencion" ? (
          <button
            type="button"
            onClick={() => onToggleMode("monitoreo")}
            className="inline-flex items-center gap-1.5 rounded-xl bg-emerald-600 px-3.5 py-2 text-xs font-medium text-white shadow-sm hover:bg-emerald-700 transition-colors"
          >
            <PlayCircle className="h-4 w-4" aria-hidden="true" />
            Reanudar Monitoreo
          </button>
        ) : (
          <button
            type="button"
            onClick={() => onToggleMode("atencion")}
            className="inline-flex items-center gap-1.5 rounded-xl bg-amber-500 px-3.5 py-2 text-xs font-medium text-white shadow-sm hover:bg-amber-600 transition-colors"
          >
            <PauseCircle className="h-4 w-4" aria-hidden="true" />
            {crib.modoOperacion === "alarma"
              ? "Silenciar Alarma y Activar Modo Atención (15 min)"
              : "Activar Modo Atención (15 min — RF05)"}
          </button>
        )}
      </div>
    </div>
  );
}
