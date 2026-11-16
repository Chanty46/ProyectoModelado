import { useEffect, useState, type FormEvent } from "react";
import { BatteryWarning, Music, Sparkles } from "lucide-react";
import {
  accountApi,
  ApiError,
  type SongCatalogItem,
  type ToysAndPeripheralsStatusResponse,
} from "../../lib/api";
import { computeMobileLedFrame } from "../../lib/formatters";

interface SongsAndMobileCardProps {
  accessToken: string;
  cunaId: string;
  activeProfileId: string | null;
  songs: SongCatalogItem[];
  selectedSongCode: string;
  toysStatus: ToysAndPeripheralsStatusResponse | null;
  onSelectSong: (code: string) => void;
  onMobileActivated: () => Promise<void>;
  onError: (msg: string | null) => void;
}

/**
 * Tarjeta de estímulos sensoriales: catálogo de melodías PWM (RF12),
 * vista previa animada y control del móvil colgante de 9 LEDs (RF13/RF33)
 * junto con indicadores de batería de periféricos.
 */
export function SongsAndMobileCard({
  accessToken,
  cunaId,
  activeProfileId,
  songs,
  selectedSongCode,
  toysStatus,
  onSelectSong,
  onMobileActivated,
  onError,
}: SongsAndMobileCardProps) {
  const [mobilePattern, setMobilePattern] = useState<string>("ola_circular");
  const [mobileSpeed, setMobileSpeed] = useState<"lenta" | "media" | "rapida">("media");
  const [mobileStep, setMobileStep] = useState<number>(0);
  const [isSubmittingMobile, setIsSubmittingMobile] = useState<boolean>(false);
  const [isSubmittingSong, setIsSubmittingSong] = useState<boolean>(false);
  const mobileDuration = 60;

  useEffect(() => {
    const intervalMs = mobileSpeed === "rapida" ? 160 : mobileSpeed === "media" ? 320 : 600;
    const timer = window.setInterval(() => {
      setMobileStep((prev) => (prev + 1) % 90);
    }, intervalMs);
    return () => window.clearInterval(timer);
  }, [mobileSpeed]);

  async function handlePlaySong(accion: "play" | "stop") {
    onError(null);
    setIsSubmittingSong(true);
    try {
      await accountApi.playSong(accessToken, cunaId, {
        codigo: selectedSongCode,
        accion,
        perfilId: activeProfileId,
      });
    } catch (err) {
      if (err instanceof ApiError) {
        onError(err.message);
      }
    } finally {
      setIsSubmittingSong(false);
    }
  }

  async function handlePlayMobile(e: FormEvent) {
    e.preventDefault();
    onError(null);
    setIsSubmittingMobile(true);
    try {
      await accountApi.playMobile(accessToken, cunaId, {
        patron: mobilePattern,
        velocidad: mobileSpeed,
        duracionSegundos: mobileDuration,
        perfilId: activeProfileId,
      });
      await onMobileActivated();
    } catch (err) {
      if (err instanceof ApiError) {
        onError(err.message);
      }
    } finally {
      setIsSubmittingMobile(false);
    }
  }

  const mobileLeds = computeMobileLedFrame(mobilePattern, mobileStep, 9);

  return (
    <div className="rounded-xl border border-slate-200 bg-white p-4 shadow-sm space-y-3">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <div className="flex items-center gap-2">
          <Music className="h-4 w-4 text-teal-600" aria-hidden="true" />
          <h4 className="text-xs font-semibold text-slate-900">
            Melodías PWM (RF12) y Móvil 9 LEDs (RF13 / RF33)
          </h4>
        </div>
        <div className="flex flex-wrap items-center gap-1.5">
          {toysStatus?.bateriaJuguetePct !== null && toysStatus?.bateriaJuguetePct !== undefined && (
            <span
              className={`inline-flex items-center gap-1 rounded-full px-2 py-0.5 text-[10px] font-semibold ${
                toysStatus.alertaBateriaBajaJuguete
                  ? "bg-red-100 text-red-800"
                  : "bg-slate-100 text-slate-700"
              }`}
            >
              {toysStatus.alertaBateriaBajaJuguete && (
                <BatteryWarning className="h-3 w-3" aria-hidden="true" />
              )}
              Juguete: {toysStatus.bateriaJuguetePct}%
            </span>
          )}
          {toysStatus?.bateriaBiberonPct !== null && toysStatus?.bateriaBiberonPct !== undefined && (
            <span className="inline-flex items-center gap-1 rounded-full bg-slate-100 px-2 py-0.5 text-[10px] font-medium text-slate-700">
              Biberón: {toysStatus.bateriaBiberonPct}%
              {toysStatus.ultimaMasaBiberonG !== null ? ` · ${toysStatus.ultimaMasaBiberonG.toFixed(0)}g` : ""}
              {toysStatus.ultimaTempBiberonC !== null ? ` · ${toysStatus.ultimaTempBiberonC.toFixed(1)}°C` : ""}
            </span>
          )}
        </div>
      </div>

      <div className="flex items-center gap-2">
        <select
          value={selectedSongCode}
          onChange={(e) => onSelectSong(e.target.value)}
          aria-label="Seleccionar melodía de cuna"
          className="flex-1 rounded-lg border border-slate-300 px-2.5 py-1.5 text-xs"
        >
          {songs.map((s) => (
            <option key={s.id} value={s.codigo}>
              {s.nombre} ({s.duracionSegundos} s)
            </option>
          ))}
        </select>
        <button
          type="button"
          disabled={isSubmittingSong}
          onClick={() => void handlePlaySong("play")}
          className="rounded-lg bg-teal-600 px-3 py-1.5 text-xs font-medium text-white hover:bg-teal-700 disabled:opacity-50 transition-colors"
        >
          {isSubmittingSong ? "Enviando..." : "Reproducir"}
        </button>
        <button
          type="button"
          disabled={isSubmittingSong}
          onClick={() => void handlePlaySong("stop")}
          className="rounded-lg border border-slate-300 bg-white px-2.5 py-1.5 text-xs text-slate-700 hover:bg-slate-100 disabled:opacity-50 transition-colors"
        >
          Detener
        </button>
      </div>

      {/* Vista previa animada del aro de 9 LEDs (RF13) */}
      <div className="flex items-center justify-between rounded-lg border border-slate-100 bg-slate-900 px-3 py-2">
        <span className="text-[11px] font-medium text-slate-300">
          Vista previa 9 LEDs ({mobilePattern.replace("_", " ")})
        </span>
        <div className="flex items-center gap-1.5" aria-label="Simulación de los 9 LEDs del móvil">
          {mobileLeds.map((on, idx) => (
            <span
              key={idx}
              className={`h-2.5 w-2.5 rounded-full transition-all duration-150 ${
                on
                  ? "bg-amber-300 shadow-[0_0_8px_rgba(252,211,77,0.9)] scale-110"
                  : "bg-slate-700"
              }`}
            />
          ))}
        </div>
      </div>

      <form onSubmit={(e) => void handlePlayMobile(e)} className="grid grid-cols-3 gap-2 pt-1">
        <select
          value={mobilePattern}
          onChange={(e) => setMobilePattern(e.target.value)}
          aria-label="Patrón del móvil de LEDs"
          className="rounded-lg border border-slate-300 px-2 py-1.5 text-xs"
        >
          <option value="ola_circular">Ola circular</option>
          <option value="cascada_suave">Cascada suave</option>
          <option value="destello_alterno">Destello alterno</option>
        </select>
        <select
          value={mobileSpeed}
          onChange={(e) => setMobileSpeed(e.target.value as "lenta" | "media" | "rapida")}
          aria-label="Velocidad del móvil"
          className="rounded-lg border border-slate-300 px-2 py-1.5 text-xs"
        >
          <option value="lenta">Velocidad lenta</option>
          <option value="media">Velocidad media</option>
          <option value="rapida">Velocidad rápida</option>
        </select>
        <button
          type="submit"
          disabled={isSubmittingMobile}
          className="inline-flex items-center justify-center gap-1 rounded-lg bg-indigo-600 px-2.5 py-1.5 text-xs font-medium text-white hover:bg-indigo-700 disabled:opacity-50 transition-colors"
        >
          <Sparkles className="h-3.5 w-3.5" aria-hidden="true" />
          {isSubmittingMobile ? "Enviando..." : `Móvil (${mobileDuration}s)`}
        </button>
      </form>
    </div>
  );
}
