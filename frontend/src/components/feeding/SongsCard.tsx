import { useState } from "react";
import { BatteryWarning, Music } from "lucide-react";
import {
  accountApi,
  ApiError,
  type SongCatalogItem,
  type ToysAndPeripheralsStatusResponse,
} from "../../lib/api";

interface SongsCardProps {
  accessToken: string;
  cunaId: string;
  activeProfileId: string | null;
  songs: SongCatalogItem[];
  selectedSongCode: string;
  toysStatus: ToysAndPeripheralsStatusResponse | null;
  onSelectSong: (code: string) => void;
  onError: (msg: string | null) => void;
}

/**
 * Catálogo de melodías PWM (RF12) e indicadores de batería de periféricos (RF36).
 * El móvil colgante tiene su propia tarjeta: MobileControlCard.
 */
export function SongsCard({
  accessToken,
  cunaId,
  activeProfileId,
  songs,
  selectedSongCode,
  toysStatus,
  onSelectSong,
  onError,
}: SongsCardProps) {
  const [isSubmittingSong, setIsSubmittingSong] = useState<boolean>(false);

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

  return (
    <div className="rounded-xl border border-slate-200 bg-white p-4 shadow-sm space-y-3">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <div className="flex items-center gap-2">
          <Music className="h-4 w-4 text-teal-600" aria-hidden="true" />
          <h4 className="text-xs font-semibold text-slate-900">Melodías PWM (RF12)</h4>
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
    </div>
  );
}
