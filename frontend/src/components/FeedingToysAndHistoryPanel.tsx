import { useState, useEffect, useCallback, type FormEvent } from "react";
import {
  Milk,
  Music,
  Sparkles,
  FileDown,
  AlertTriangle,
  BatteryWarning,
  Moon,
} from "lucide-react";
import {
  accountApi,
  ApiError,
  type FeedingSessionRecord,
  type SleepSessionRecord,
  type SongCatalogItem,
  type ToysAndPeripheralsStatusResponse,
} from "../lib/api";

interface FeedingToysAndHistoryPanelProps {
  accessToken: string;
  cunaId: string;
  bebeId: string | null;
  activeProfileId: string | null;
  onError: (msg: string | null) => void;
}

export function FeedingToysAndHistoryPanel({
  accessToken,
  cunaId,
  bebeId,
  activeProfileId,
  onError,
}: FeedingToysAndHistoryPanelProps) {
  const [songs, setSongs] = useState<SongCatalogItem[]>([]);
  const [selectedSongCode, setSelectedSongCode] = useState<string>("estrellita");
  const [toysStatus, setToysStatus] = useState<ToysAndPeripheralsStatusResponse | null>(null);

  // Control del Móvil Colgante RF13
  const [mobilePattern, setMobilePattern] = useState<string>("ola_circular");
  const [mobileSpeed, setMobileSpeed] = useState<"lenta" | "media" | "rapida">("media");
  const mobileDuration = 60;

  // Sesión de Alimentación RF15–RF17, RF37
  const [initialMassG, setInitialMassG] = useState<string>("210.0");
  const [foodTempC, setFoodTempC] = useState<string>("36.8");
  const [finalMassG, setFinalMassG] = useState<string>("95.0");
  const [activeFeedingSession, setActiveFeedingSession] = useState<FeedingSessionRecord | null>(null);

  // Historiales RF30–RF32
  const [feedingHistory, setFeedingHistory] = useState<FeedingSessionRecord[]>([]);
  const [sleepHistory, setSleepHistory] = useState<SleepSessionRecord[]>([]);
  const [filterFrom, setFilterFrom] = useState<string>("");
  const [filterTo, setFilterTo] = useState<string>("");
  const [exportStatusMsg, setExportStatusMsg] = useState<string | null>(null);

  const loadCribPeripherals = useCallback(async () => {
    try {
      const [songList, toys] = await Promise.all([
        accountApi.listSongs(accessToken, cunaId),
        accountApi.getToysStatus(accessToken, cunaId),
      ]);
      setSongs(songList);
      if (songList.length > 0) {
        setSelectedSongCode((prev) =>
          songList.some((s) => s.codigo === prev) ? prev : songList[0].codigo
        );
      }
      setToysStatus(toys);
      if (toys.ultimaMasaBiberonG !== null) {
        setInitialMassG(toys.ultimaMasaBiberonG.toString());
      }
      if (toys.ultimaTempBiberonC !== null) {
        setFoodTempC(toys.ultimaTempBiberonC.toString());
      }
    } catch (err) {
      if (err instanceof ApiError) {
        onError(err.message);
      }
    }
  }, [accessToken, cunaId, onError]);

  const loadBabyHistories = useCallback(async () => {
    if (!bebeId) {
      setFeedingHistory([]);
      setSleepHistory([]);
      return;
    }
    try {
      const [feeds, sleeps] = await Promise.all([
        accountApi.getFeedingHistory(
          accessToken,
          bebeId,
          filterFrom || undefined,
          filterTo || undefined
        ),
        accountApi.getSleepHistory(
          accessToken,
          bebeId,
          filterFrom || undefined,
          filterTo || undefined
        ),
      ]);
      setFeedingHistory(feeds);
      setSleepHistory(sleeps);
      const openSession = feeds.find((f) => f.fin === null) ?? null;
      setActiveFeedingSession(openSession);
    } catch (err) {
      if (err instanceof ApiError) {
        onError(err.message);
      }
    }
  }, [accessToken, bebeId, filterFrom, filterTo, onError]);

  useEffect(() => {
    void loadCribPeripherals();
  }, [loadCribPeripherals]);

  useEffect(() => {
    void loadBabyHistories();
  }, [loadBabyHistories]);

  async function handleStartFeeding(e: FormEvent) {
    e.preventDefault();
    if (!bebeId) {
      onError("Registra y asocia un perfil de bebé a esta cuna antes de iniciar una toma (RF37).");
      return;
    }
    onError(null);
    try {
      const created = await accountApi.startFeedingSession(accessToken, {
        bebeId,
        masaInicialG: Number(initialMassG),
        tempC: foodTempC ? Number(foodTempC) : null,
        perfilId: activeProfileId,
      });
      setActiveFeedingSession(created);
      await loadBabyHistories();
    } catch (err) {
      if (err instanceof ApiError) {
        onError(err.message);
      }
    }
  }

  async function handleFinishFeeding(e: FormEvent) {
    e.preventDefault();
    if (!activeFeedingSession) {
      return;
    }
    onError(null);
    try {
      await accountApi.finishFeedingSession(accessToken, activeFeedingSession.id, {
        masaFinalG: Number(finalMassG),
        perfilId: activeProfileId,
      });
      setActiveFeedingSession(null);
      await loadBabyHistories();
    } catch (err) {
      if (err instanceof ApiError) {
        onError(err.message);
      }
    }
  }

  async function handlePlaySong(accion: "play" | "stop") {
    onError(null);
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
    }
  }

  async function handlePlayMobile(e: FormEvent) {
    e.preventDefault();
    onError(null);
    try {
      await accountApi.playMobile(accessToken, cunaId, {
        patron: mobilePattern,
        velocidad: mobileSpeed,
        duracionSegundos: mobileDuration,
        perfilId: activeProfileId,
      });
      await loadCribPeripherals();
    } catch (err) {
      if (err instanceof ApiError) {
        onError(err.message);
      }
    }
  }

  async function handleRecordSampleSleep(tipo: "siesta" | "noche") {
    if (!bebeId) {
      onError("Asocia primero un perfil de bebé a esta cuna (RF30).");
      return;
    }
    onError(null);
    try {
      const fin = new Date();
      const inicio = new Date(fin.getTime() - (tipo === "siesta" ? 45 : 360) * 60 * 1000);
      await accountApi.createSleepSession(accessToken, bebeId, {
        cunaId,
        tipo,
        inicio: inicio.toISOString(),
        fin: fin.toISOString(),
        movimientoPromedio: 0.042,
        interrupciones: tipo === "siesta" ? 0 : 1,
        perfilId: activeProfileId,
      });
      await loadBabyHistories();
    } catch (err) {
      if (err instanceof ApiError) {
        onError(err.message);
      }
    }
  }

  async function handleExport(formato: "csv" | "pdf") {
    if (!bebeId) {
      onError("Asocia primero un perfil de bebé para exportar su historial (RF32).");
      return;
    }
    onError(null);
    setExportStatusMsg(null);
    try {
      const res = await accountApi.exportBabyHistory(accessToken, bebeId, {
        formato,
        desde: filterFrom || null,
        hasta: filterTo || null,
        perfilId: activeProfileId,
      });

      const binary = window.atob(res.contentBase64);
      const bytes = new Uint8Array(binary.length);
      for (let i = 0; i < binary.length; i++) {
        bytes[i] = binary.charCodeAt(i);
      }
      const blob = new Blob([bytes], { type: res.contentType });
      const url = URL.createObjectURL(blob);
      const a = document.createElement("a");
      a.href = url;
      a.download = res.fileName;
      a.click();
      URL.revokeObjectURL(url);

      setExportStatusMsg(
        `Exportado ${res.fileName} (${res.feedingSessionsCount} tomas, ${res.sleepSessionsCount} sueños) en ${res.elapsedMs} ms.`
      );
    } catch (err) {
      if (err instanceof ApiError) {
        onError(err.message);
      }
    }
  }

  const parsedTemp = Number(foodTempC);
  const isTempUnsafe = !Number.isNaN(parsedTemp) && (parsedTemp < 35.0 || parsedTemp > 38.5);

  return (
    <div className="space-y-4">
      <div className="grid grid-cols-1 gap-4 md:grid-cols-2">
        {/* RF15, RF16, RF17, RF37: Nutrición Infantil y Base de Biberón */}
        <div className="rounded-xl border border-slate-200 bg-white p-4 shadow-sm space-y-3">
          <div className="flex items-center justify-between">
            <div className="flex items-center gap-2">
              <Milk className="h-4 w-4 text-teal-600" aria-hidden="true" />
              <h4 className="text-xs font-semibold text-slate-900">
                Sesión de Alimentación y Biberón (RF15–RF17 / RF37)
              </h4>
            </div>
            <span className="text-[11px] text-slate-500">Densidad: 1.03 g/ml</span>
          </div>

          {isTempUnsafe && (
            <div className="flex items-center gap-2 rounded-lg border border-amber-300 bg-amber-50 p-2 text-xs text-amber-900">
              <AlertTriangle className="h-4 w-4 shrink-0 text-amber-600" aria-hidden="true" />
              <span>
                Alerta térmica (RF16): {parsedTemp.toFixed(1)} °C está fuera del rango seguro (35.0–38.5 °C).
              </span>
            </div>
          )}

          {!activeFeedingSession ? (
            <form onSubmit={handleStartFeeding} className="space-y-2.5">
              <div className="grid grid-cols-2 gap-2">
                <div>
                  <label className="block text-[11px] font-medium text-slate-700 mb-0.5">
                    Masa inicial M_i (≥ 20 g)
                  </label>
                  <input
                    type="number"
                    step="0.5"
                    min="20"
                    max="2000"
                    required
                    value={initialMassG}
                    onChange={(e) => setInitialMassG(e.target.value)}
                    className="w-full rounded-lg border border-slate-300 px-2.5 py-1.5 text-xs"
                  />
                </div>
                <div>
                  <label className="block text-[11px] font-medium text-slate-700 mb-0.5">
                    Temperatura IR (°C)
                  </label>
                  <input
                    type="number"
                    step="0.1"
                    required
                    value={foodTempC}
                    onChange={(e) => setFoodTempC(e.target.value)}
                    className="w-full rounded-lg border border-slate-300 px-2.5 py-1.5 text-xs"
                  />
                </div>
              </div>
              <button
                type="submit"
                disabled={!bebeId}
                className="w-full rounded-xl bg-teal-600 px-3 py-2 text-xs font-medium text-white hover:bg-teal-700 disabled:opacity-50"
              >
                Iniciar Sesión de Alimentación (RF37)
              </button>
            </form>
          ) : (
            <form onSubmit={handleFinishFeeding} className="space-y-2.5 rounded-lg border border-teal-200 bg-teal-50/50 p-3">
              <p className="text-xs font-medium text-teal-900">
                Toma en curso · M_inicial: <strong>{activeFeedingSession.masaInicialG} g</strong>
                {activeFeedingSession.tempC !== null ? ` · Temp: ${activeFeedingSession.tempC} °C` : ""}
              </p>
              <div className="flex items-center gap-2">
                <input
                  type="number"
                  step="0.5"
                  min="0"
                  max={activeFeedingSession.masaInicialG}
                  required
                  value={finalMassG}
                  onChange={(e) => setFinalMassG(e.target.value)}
                  placeholder="Masa final M_f (g)"
                  className="flex-1 rounded-lg border border-slate-300 bg-white px-2.5 py-1.5 text-xs"
                />
                <button
                  type="submit"
                  className="rounded-lg bg-teal-600 px-3 py-1.5 text-xs font-medium text-white hover:bg-teal-700"
                >
                  Finalizar y calcular ml (RF17)
                </button>
              </div>
            </form>
          )}
        </div>

        {/* RF12, RF13, RF33, RF36: Canciones PWM, Móvil de 9 LEDs y Juguete */}
        <div className="rounded-xl border border-slate-200 bg-white p-4 shadow-sm space-y-3">
          <div className="flex items-center justify-between">
            <div className="flex items-center gap-2">
              <Music className="h-4 w-4 text-teal-600" aria-hidden="true" />
              <h4 className="text-xs font-semibold text-slate-900">
                Melodías PWM (RF12) y Móvil 9 LEDs (RF13 / RF33)
              </h4>
            </div>
            {toysStatus?.alertaBateriaBajaJuguete && (
              <span className="inline-flex items-center gap-1 rounded-full bg-red-100 px-2 py-0.5 text-[10px] font-semibold text-red-800">
                <BatteryWarning className="h-3 w-3" aria-hidden="true" />
                Batería baja juguete ({toysStatus.bateriaJuguetePct}%)
              </span>
            )}
          </div>

          <div className="flex items-center gap-2">
            <select
              value={selectedSongCode}
              onChange={(e) => setSelectedSongCode(e.target.value)}
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
              onClick={() => handlePlaySong("play")}
              className="rounded-lg bg-teal-600 px-3 py-1.5 text-xs font-medium text-white hover:bg-teal-700"
            >
              Reproducir
            </button>
            <button
              type="button"
              onClick={() => handlePlaySong("stop")}
              className="rounded-lg border border-slate-300 bg-white px-2.5 py-1.5 text-xs text-slate-700 hover:bg-slate-100"
            >
              Detener
            </button>
          </div>

          <form onSubmit={handlePlayMobile} className="grid grid-cols-3 gap-2 pt-2 border-t border-slate-100">
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
              className="inline-flex items-center justify-center gap-1 rounded-lg bg-indigo-600 px-2.5 py-1.5 text-xs font-medium text-white hover:bg-indigo-700"
            >
              <Sparkles className="h-3.5 w-3.5" aria-hidden="true" />
              Móvil ({mobileDuration}s)
            </button>
          </form>
        </div>
      </div>

      {/* RF30, RF31, RF32: Historiales de Sueño y Alimentación (< 3 s) y Exportación PDF/CSV (< 10 s) */}
      <div className="rounded-xl border border-slate-200 bg-white p-4 shadow-sm space-y-3">
        <div className="flex flex-wrap items-center justify-between gap-2">
          <div className="flex items-center gap-2">
            <Moon className="h-4 w-4 text-teal-600" aria-hidden="true" />
            <h4 className="text-xs font-semibold text-slate-900">
              Historiales de Sueño (RF30), Alimentación (RF31) y Exportación PDF/CSV (RF32)
            </h4>
          </div>

          <div className="flex flex-wrap items-center gap-2">
            <input
              type="date"
              value={filterFrom}
              onChange={(e) => setFilterFrom(e.target.value)}
              aria-label="Filtrar desde fecha"
              className="rounded border border-slate-300 px-2 py-1 text-xs"
            />
            <input
              type="date"
              value={filterTo}
              onChange={(e) => setFilterTo(e.target.value)}
              aria-label="Filtrar hasta fecha"
              className="rounded border border-slate-300 px-2 py-1 text-xs"
            />
            <button
              type="button"
              onClick={() => handleRecordSampleSleep("siesta")}
              disabled={!bebeId}
              className="rounded-lg border border-slate-300 bg-slate-50 px-2.5 py-1 text-xs font-medium text-slate-700 hover:bg-slate-100 disabled:opacity-50"
            >
              + Siesta
            </button>
            <button
              type="button"
              onClick={() => handleExport("csv")}
              disabled={!bebeId}
              className="inline-flex items-center gap-1 rounded-lg border border-teal-600 bg-teal-50 px-2.5 py-1 text-xs font-medium text-teal-800 hover:bg-teal-100 disabled:opacity-50"
            >
              <FileDown className="h-3.5 w-3.5" aria-hidden="true" />
              CSV
            </button>
            <button
              type="button"
              onClick={() => handleExport("pdf")}
              disabled={!bebeId}
              className="inline-flex items-center gap-1 rounded-lg bg-slate-900 px-2.5 py-1 text-xs font-medium text-white hover:bg-slate-800 disabled:opacity-50"
            >
              <FileDown className="h-3.5 w-3.5" aria-hidden="true" />
              PDF
            </button>
          </div>
        </div>

        {exportStatusMsg && (
          <p className="rounded-lg bg-emerald-50 px-3 py-1.5 text-xs font-medium text-emerald-800">
            {exportStatusMsg}
          </p>
        )}

        <div className="grid grid-cols-1 gap-3 md:grid-cols-2">
          <div>
            <h5 className="text-[11px] font-semibold uppercase tracking-wider text-slate-500 mb-1.5">
              Tomas registradas ({feedingHistory.length})
            </h5>
            {feedingHistory.length === 0 ? (
              <p className="text-xs text-slate-500">Sin sesiones de alimentación en el rango.</p>
            ) : (
              <ul className="max-h-32 overflow-y-auto divide-y divide-slate-100 text-xs">
                {feedingHistory.slice(0, 8).map((f) => (
                  <li key={f.id} className="py-1.5 flex items-center justify-between">
                    <span>
                      {new Date(f.inicio).toLocaleString()} · {f.masaInicialG}g →{" "}
                      {f.masaFinalG !== null ? `${f.masaFinalG}g` : "en curso"}
                    </span>
                    <span className="font-semibold text-teal-700">
                      {f.mlConsumidos !== null ? `${f.mlConsumidos.toFixed(1)} ml` : "—"}
                    </span>
                  </li>
                ))}
              </ul>
            )}
          </div>

          <div>
            <h5 className="text-[11px] font-semibold uppercase tracking-wider text-slate-500 mb-1.5">
              Sesiones de sueño ({sleepHistory.length})
            </h5>
            {sleepHistory.length === 0 ? (
              <p className="text-xs text-slate-500">Sin sesiones de sueño en el rango.</p>
            ) : (
              <ul className="max-h-32 overflow-y-auto divide-y divide-slate-100 text-xs">
                {sleepHistory.slice(0, 8).map((s) => (
                  <li key={s.id} className="py-1.5 flex items-center justify-between">
                    <span>
                      [{s.tipo.toUpperCase()}] {new Date(s.inicio).toLocaleString()}
                    </span>
                    <span className="text-slate-600">
                      Mov: {s.movimientoPromedio ?? 0} kg/m · Int: {s.interrupciones}
                    </span>
                  </li>
                ))}
              </ul>
            )}
          </div>
        </div>
      </div>
    </div>
  );
}
