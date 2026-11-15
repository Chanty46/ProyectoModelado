import { useState, useEffect, useCallback, useRef, type FormEvent } from "react";
import {
  Activity,
  Thermometer,
  Droplets,
  Scale,
  Sun,
  ShieldAlert,
  PauseCircle,
  PlayCircle,
  Sliders,
  Bell,
  Baby,
  CheckCircle2,
} from "lucide-react";
import {
  ResponsiveContainer,
  LineChart,
  Line,
  XAxis,
  YAxis,
  Tooltip,
  CartesianGrid,
  Legend,
} from "recharts";
import { supabase } from "../lib/supabase";
import {
  accountApi,
  ApiError,
  type LiveCribStatusResponse,
} from "../lib/api";
import {
  evaluateComfortBand,
  formatRemainingCountdown,
  resolveRgbLedStatus,
} from "../lib/formatters";

interface LiveCribPanelProps {
  accessToken: string;
  cunaId: string;
  activeProfileId: string | null;
  userRole: "principal" | "secundario" | null;
  onError: (msg: string | null) => void;
  onCribUpdated: () => void;
}

export function LiveCribPanel({
  accessToken,
  cunaId,
  activeProfileId,
  userRole,
  onError,
  onCribUpdated,
}: LiveCribPanelProps) {
  const [live, setLive] = useState<LiveCribStatusResponse | null>(null);
  const [lightLevel, setLightLevel] = useState<number>(50);
  const [showThresholdsForm, setShowThresholdsForm] = useState<boolean>(false);
  const [countdownNowMs, setCountdownNowMs] = useState<number>(() => Date.now());
  const isDirtyLightRef = useRef<boolean>(false);
  const showThresholdsFormRef = useRef<boolean>(false);

  // Estado del formulario de umbrales RF08
  const [tempMin, setTempMin] = useState("18.0");
  const [tempMax, setTempMax] = useState("26.0");
  const [humMin, setHumMin] = useState("40.0");
  const [humMax, setHumMax] = useState("60.0");
  const [movUmbral, setMovUmbral] = useState("0.150");
  const [movAlertaActiva, setMovAlertaActiva] = useState(true);
  const [ruidoPiso, setRuidoPiso] = useState("0.010");
  const [escalaMasa, setEscalaMasa] = useState("0.20");

  const syncThresholdInputsFromData = useCallback((data: LiveCribStatusResponse) => {
    setTempMin(data.umbrales.tempMin.toString());
    setTempMax(data.umbrales.tempMax.toString());
    setHumMin(data.umbrales.humMin.toString());
    setHumMax(data.umbrales.humMax.toString());
    setMovUmbral(data.umbrales.movimientoUmbral.toString());
    setMovAlertaActiva(data.umbrales.movimientoAlertaActiva);
    setRuidoPiso(data.umbrales.ruidoMasaPiso.toString());
    setEscalaMasa(data.umbrales.escalaMasa.toString());
  }, []);

  useEffect(() => {
    showThresholdsFormRef.current = showThresholdsForm;
  }, [showThresholdsForm]);

  const fetchLiveStatus = useCallback(
    async (signal?: AbortSignal) => {
      try {
        const data = await accountApi.getLiveCribStatus(accessToken, cunaId, signal);
        setLive(data);
        if (!isDirtyLightRef.current) {
          setLightLevel(data.cuna.luzEncendida ? data.cuna.luzNivel : 50);
        }
        if (!showThresholdsFormRef.current) {
          syncThresholdInputsFromData(data);
        }
      } catch (err) {
        if ((err as Error)?.name === "AbortError") {
          return;
        }
        if (err instanceof ApiError) {
          onError(err.message);
        }
      }
    },
    [accessToken, cunaId, onError, syncThresholdInputsFromData]
  );

  useEffect(() => {
    isDirtyLightRef.current = false;
    showThresholdsFormRef.current = false;
    setShowThresholdsForm(false);
  }, [cunaId]);

  useEffect(() => {
    const controller = new AbortController();
    void fetchLiveStatus(controller.signal);
    return () => controller.abort();
  }, [cunaId, fetchLiveStatus]);

  // Cuenta regresiva cada segundo mientras esté activo el Modo Atención (RF05)
  useEffect(() => {
    if (live?.cuna.modoOperacion !== "atencion") {
      return;
    }
    const timer = window.setInterval(() => setCountdownNowMs(Date.now()), 1000);
    return () => window.clearInterval(timer);
  }, [live?.cuna.modoOperacion]);

  // IU-01: Suscripción Supabase Realtime (< 1 s) + fallback por polling cada 2 s si no hay Supabase
  useEffect(() => {
    const sbClient = supabase;
    const pollIntervalMs = sbClient ? 10_000 : 2_000;
    const pollTimer = window.setInterval(() => {
      void fetchLiveStatus();
    }, pollIntervalMs);

    if (!sbClient) {
      return () => window.clearInterval(pollTimer);
    }

    const channel = sbClient
      .channel(`crib-live-${cunaId}`)
      .on(
        "postgres_changes",
        { event: "*", schema: "public", table: "cunas", filter: `id=eq.${cunaId}` },
        () => {
          void fetchLiveStatus();
          onCribUpdated();
        }
      )
      .on(
        "postgres_changes",
        { event: "*", schema: "public", table: "telemetria", filter: `cuna_id=eq.${cunaId}` },
        () => {
          void fetchLiveStatus();
        }
      )
      .on(
        "postgres_changes",
        { event: "*", schema: "public", table: "eventos_alertas", filter: `cuna_id=eq.${cunaId}` },
        () => {
          void fetchLiveStatus();
        }
      )
      .on(
        "postgres_changes",
        { event: "*", schema: "public", table: "umbrales", filter: `cuna_id=eq.${cunaId}` },
        () => {
          void fetchLiveStatus();
        }
      )
      .subscribe();

    return () => {
      window.clearInterval(pollTimer);
      void sbClient.removeChannel(channel);
    };
  }, [cunaId, fetchLiveStatus, onCribUpdated]);

  async function handleToggleMode(targetMode: "monitoreo" | "atencion") {
    onError(null);
    try {
      await accountApi.setCribMode(accessToken, cunaId, {
        modo: targetMode,
        perfilId: activeProfileId,
      });
      await fetchLiveStatus();
      onCribUpdated();
    } catch (err) {
      if (err instanceof ApiError) {
        onError(err.message);
      }
    }
  }

  async function handleApplyLight(encendida: boolean, nivel: number) {
    onError(null);
    try {
      await accountApi.setCribLight(accessToken, cunaId, {
        encendida,
        nivel,
        perfilId: activeProfileId,
      });
      await fetchLiveStatus();
      onCribUpdated();
    } catch (err) {
      if (err instanceof ApiError) {
        onError(err.message);
      }
    }
  }

  async function handleSaveThresholds(e: FormEvent) {
    e.preventDefault();
    onError(null);
    try {
      await accountApi.updateCribThresholds(accessToken, cunaId, {
        tempMin: Number(tempMin),
        tempMax: Number(tempMax),
        humMin: Number(humMin),
        humMax: Number(humMax),
        movimientoUmbral: Number(movUmbral),
        movimientoAlertaActiva: movAlertaActiva,
        ruidoMasaPiso: Number(ruidoPiso),
        escalaMasa: Number(escalaMasa),
        perfilId: activeProfileId,
      });
      showThresholdsFormRef.current = false;
      setShowThresholdsForm(false);
      await fetchLiveStatus();
    } catch (err) {
      if (err instanceof ApiError) {
        onError(err.message);
      }
    }
  }

  if (!live) {
    return (
      <div
        role="status"
        aria-label="Cargando telemetría en tiempo real"
        className="space-y-4 rounded-xl border border-slate-200 bg-slate-50/70 p-4 animate-pulse"
      >
        <div className="flex items-center justify-between gap-4 rounded-xl border border-slate-200 bg-white p-4 shadow-sm">
          <div className="flex items-center gap-3.5">
            <div className="h-14 w-14 rounded-2xl bg-slate-200" />
            <div className="space-y-2">
              <div className="h-4 w-48 rounded bg-slate-200" />
              <div className="h-3 w-32 rounded bg-slate-100" />
            </div>
          </div>
          <div className="h-8 w-36 rounded-xl bg-slate-200" />
        </div>
        <div className="grid grid-cols-2 gap-3 sm:grid-cols-4">
          {[1, 2, 3, 4].map((i) => (
            <div key={i} className="rounded-xl border border-slate-200 bg-white p-3.5 shadow-sm space-y-2">
              <div className="h-3 w-20 rounded bg-slate-200" />
              <div className="h-6 w-16 rounded bg-slate-200" />
              <div className="h-1.5 w-full rounded bg-slate-100" />
              <div className="h-2 w-24 rounded bg-slate-100" />
            </div>
          ))}
        </div>
        <p className="sr-only">Sincronizando telemetría en tiempo real...</p>
      </div>
    );
  }

  const latest = live.ultimaTelemetria;
  const presenceThresholdKg = Number((1.0 * live.umbrales.escalaMasa).toFixed(2));
  const babyPresent =
    latest?.masaKg !== null &&
    latest?.masaKg !== undefined &&
    latest.masaKg > presenceThresholdKg;
  const rgbVisual = resolveRgbLedStatus(live.cuna.modoOperacion);
  const attentionCountdown =
    live.cuna.modoOperacion === "atencion"
      ? formatRemainingCountdown(live.cuna.modoAtencionExpiraEn, countdownNowMs)
      : null;

  const chartData = live.telemetriaReciente.map((t) => ({
    hora: new Date(t.ts).toLocaleTimeString([], { hour: "2-digit", minute: "2-digit", second: "2-digit" }),
    tempC: t.tempC,
    humedad: t.humedad,
    movimiento: t.indiceMovimiento,
  }));

  const tempBand = evaluateComfortBand(latest?.tempC, live.umbrales.tempMin, live.umbrales.tempMax);
  const humBand = evaluateComfortBand(latest?.humedad, live.umbrales.humMin, live.umbrales.humMax);
  const movPercent =
    latest?.indiceMovimiento !== null &&
    latest?.indiceMovimiento !== undefined &&
    live.umbrales.movimientoUmbral > 0
      ? Math.min(100, Math.round((latest.indiceMovimiento / live.umbrales.movimientoUmbral) * 100))
      : 0;
  const lampGlowOpacity = live.cuna.luzEncendida ? Math.max(0.15, live.cuna.luzNivel / 100) : 0;

  return (
    <div className="space-y-4 rounded-xl border border-slate-200 bg-slate-50/70 p-4">
      {/* Gemelo Visual y Estado Operativo (RF04, RF05, RF09) */}
      <div className="flex flex-wrap items-center justify-between gap-4 rounded-xl border border-slate-200 bg-white p-4 shadow-sm">
        <div className="flex items-center gap-3.5">
          {/* Indicador visual del estado de cuna + luz nocturna */}
          <div className="relative flex h-14 w-14 shrink-0 items-center justify-center rounded-2xl border border-slate-200 bg-slate-900 text-white overflow-hidden">
            {live.cuna.luzEncendida && (
              <div
                aria-hidden="true"
                className="absolute inset-0 bg-amber-400 transition-opacity duration-500"
                style={{ opacity: lampGlowOpacity * 0.45 }}
              />
            )}
            <div className="relative z-10 flex flex-col items-center">
              {live.cuna.modoOperacion === "alarma" ? (
                <ShieldAlert className="h-6 w-6 text-red-400 animate-soft-glow" aria-hidden="true" />
              ) : babyPresent ? (
                <Baby className="h-6 w-6 text-emerald-300" aria-hidden="true" />
              ) : (
                <Activity className="h-6 w-6 text-teal-300" aria-hidden="true" />
              )}
              <span
                className={`mt-1 h-1.5 w-6 rounded-full ${
                  live.cuna.modoOperacion === "alarma"
                    ? "bg-red-500"
                    : live.cuna.modoOperacion === "atencion"
                    ? "bg-amber-400"
                    : "bg-emerald-400"
                }`}
              />
            </div>
          </div>

          <div>
            <div className="flex flex-wrap items-center gap-2">
              <span className="text-sm font-semibold text-slate-900">
                {live.cuna.modoOperacion === "alarma"
                  ? "ALARMA CRÍTICA DE RETIRO (RF04)"
                  : live.cuna.modoOperacion === "atencion"
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
                  live.cuna.estadoConexion === "en_linea"
                    ? "bg-emerald-50 text-emerald-700"
                    : "bg-red-100 text-red-800"
                }`}
              >
                {live.cuna.estadoConexion === "en_linea"
                  ? "Central en línea"
                  : "Desconectada (Watchdog RF20)"}
              </span>
              {live.enVentanaGraciaRetiro && (
                <span className="rounded-full bg-amber-100 px-2 py-0.5 text-[11px] font-semibold text-amber-900 animate-pulse">
                  Ventana de gracia de retiro (5 s)...
                </span>
              )}
            </div>
            <p className="text-xs text-slate-500 mt-1">
              {live.cuna.modoOperacion === "atencion" && live.cuna.modoAtencionExpiraEn
                ? `Alarma de retiro inhibida hasta las ${new Date(live.cuna.modoAtencionExpiraEn).toLocaleTimeString()} (15 min)`
                : "Retiro imprevisto activa ventana de gracia de 5 s antes de sonar alarma en Central."}
            </p>
          </div>
        </div>

        <div className="flex flex-wrap items-center gap-2">
          {live.cuna.modoOperacion === "atencion" ? (
            <button
              type="button"
              onClick={() => handleToggleMode("monitoreo")}
              className="inline-flex items-center gap-1.5 rounded-xl bg-emerald-600 px-3.5 py-2 text-xs font-medium text-white shadow-sm hover:bg-emerald-700 transition-colors"
            >
              <PlayCircle className="h-4 w-4" aria-hidden="true" />
              Reanudar Monitoreo
            </button>
          ) : (
            <button
              type="button"
              onClick={() => handleToggleMode("atencion")}
              className="inline-flex items-center gap-1.5 rounded-xl bg-amber-500 px-3.5 py-2 text-xs font-medium text-white shadow-sm hover:bg-amber-600 transition-colors"
            >
              <PauseCircle className="h-4 w-4" aria-hidden="true" />
              {live.cuna.modoOperacion === "alarma"
                ? "Silenciar Alarma y Activar Modo Atención (15 min)"
                : "Activar Modo Atención (15 min — RF05)"}
            </button>
          )}
        </div>
      </div>

      {/* Tarjetas de Telemetría en Tiempo Real (< 1 s, IU-01) */}
      <div className="grid grid-cols-2 gap-3 sm:grid-cols-4">
        <div className="rounded-xl border border-slate-200 bg-white p-3.5 shadow-sm space-y-1.5">
          <div className="flex items-center justify-between text-xs text-slate-500">
            <span>Temperatura (RF07)</span>
            <Thermometer className="h-4 w-4 text-teal-600" aria-hidden="true" />
          </div>
          <p className="text-lg font-bold text-slate-900">
            {latest?.tempC !== null && latest?.tempC !== undefined ? `${latest.tempC.toFixed(1)} °C` : "—"}
          </p>
          <div className="h-1.5 w-full rounded-full bg-slate-100 overflow-hidden">
            <div
              className={`h-full rounded-full transition-all duration-300 ${
                tempBand.status === "ok"
                  ? "bg-emerald-500"
                  : tempBand.status === "unknown"
                  ? "bg-slate-300"
                  : "bg-amber-500"
              }`}
              style={{ width: `${tempBand.barPercent}%` }}
            />
          </div>
          <p className="text-[11px] text-slate-500">
            Umbral: {live.umbrales.tempMin}–{live.umbrales.tempMax} °C
          </p>
        </div>

        <div className="rounded-xl border border-slate-200 bg-white p-3.5 shadow-sm space-y-1.5">
          <div className="flex items-center justify-between text-xs text-slate-500">
            <span>Humedad (RF07)</span>
            <Droplets className="h-4 w-4 text-sky-600" aria-hidden="true" />
          </div>
          <p className="text-lg font-bold text-slate-900">
            {latest?.humedad !== null && latest?.humedad !== undefined ? `${latest.humedad.toFixed(1)} %` : "—"}
          </p>
          <div className="h-1.5 w-full rounded-full bg-slate-100 overflow-hidden">
            <div
              className={`h-full rounded-full transition-all duration-300 ${
                humBand.status === "ok"
                  ? "bg-sky-500"
                  : humBand.status === "unknown"
                  ? "bg-slate-300"
                  : "bg-amber-500"
              }`}
              style={{ width: `${humBand.barPercent}%` }}
            />
          </div>
          <p className="text-[11px] text-slate-500">
            Umbral: {live.umbrales.humMin}–{live.umbrales.humMax} %
          </p>
        </div>

        <div className="rounded-xl border border-slate-200 bg-white p-3.5 shadow-sm space-y-1.5">
          <div className="flex items-center justify-between text-xs text-slate-500">
            <span>Masa Colchón (D14)</span>
            <Scale className="h-4 w-4 text-indigo-600" aria-hidden="true" />
          </div>
          <p className="text-lg font-bold text-slate-900">
            {latest?.masaKg !== null && latest?.masaKg !== undefined ? `${latest.masaKg.toFixed(3)} kg` : "—"}
          </p>
          <span
            className={`inline-flex items-center gap-1 rounded-full px-2 py-0.5 text-[10px] font-semibold ${
              babyPresent
                ? "bg-emerald-50 text-emerald-700"
                : "bg-slate-100 text-slate-600"
            }`}
          >
            {babyPresent && <CheckCircle2 className="h-3 w-3" aria-hidden="true" />}
            {babyPresent ? "Presencia detectada" : `Sin presencia (≤ ${presenceThresholdKg} kg)`}
          </span>
        </div>

        <div className="rounded-xl border border-slate-200 bg-white p-3.5 shadow-sm space-y-1.5">
          <div className="flex items-center justify-between text-xs text-slate-500">
            <span>Movimiento (RF06/29)</span>
            <Activity className="h-4 w-4 text-amber-600" aria-hidden="true" />
          </div>
          <p className="text-lg font-bold text-slate-900">
            {latest?.indiceMovimiento !== null && latest?.indiceMovimiento !== undefined
              ? `${latest.indiceMovimiento.toFixed(3)} kg/m`
              : "—"}
          </p>
          <div className="h-1.5 w-full rounded-full bg-slate-100 overflow-hidden">
            <div
              className={`h-full rounded-full transition-all duration-300 ${
                movPercent >= 90 ? "bg-red-500" : movPercent >= 65 ? "bg-amber-500" : "bg-teal-500"
              }`}
              style={{ width: `${movPercent}%` }}
            />
          </div>
          <p className="text-[11px] text-slate-500">
            Máx: {live.umbrales.movimientoUmbral} kg/min
          </p>
        </div>
      </div>

      {/* Control de Iluminación Nocturna Gradual PWM (RF09) y Botón de Umbrales (RF08) */}
      <div className="grid grid-cols-1 gap-3 md:grid-cols-2">
        <div className="rounded-xl border border-slate-200 bg-white p-4 shadow-sm space-y-2.5">
          <div className="flex items-center justify-between">
            <div className="flex items-center gap-2">
              <Sun className="h-4 w-4 text-amber-500" aria-hidden="true" />
              <h4 className="text-xs font-semibold text-slate-900">
                Iluminación Nocturna Gradual PWM (RF09)
              </h4>
            </div>
            <span className="text-xs font-medium text-slate-600">
              {live.cuna.luzEncendida ? `Encendida (${live.cuna.luzNivel} %)` : "Apagada"}
            </span>
          </div>

          <div className="flex items-center gap-3">
            <input
              type="range"
              min={5}
              max={100}
              step={5}
              value={lightLevel}
              onChange={(e) => {
                isDirtyLightRef.current = true;
                setLightLevel(Number(e.target.value));
              }}
              aria-label="Intensidad de luz nocturna"
              className="flex-1"
            />
            <button
              type="button"
              onClick={() => {
                isDirtyLightRef.current = false;
                void handleApplyLight(true, lightLevel);
              }}
              className="rounded-lg bg-amber-500 px-3 py-1.5 text-xs font-medium text-white hover:bg-amber-600"
            >
              Aplicar {lightLevel} %
            </button>
            {live.cuna.luzEncendida && (
              <button
                type="button"
                onClick={() => {
                  isDirtyLightRef.current = false;
                  void handleApplyLight(false, 0);
                }}
                className="rounded-lg border border-slate-300 bg-white px-2.5 py-1.5 text-xs text-slate-700 hover:bg-slate-100"
              >
                Apagar
              </button>
            )}
          </div>
          <p className="text-[11px] text-slate-500">
            Transición suave en rampa de 1.5 s (PWM ≥ 1 kHz en Unidad Central).
          </p>
        </div>

        <div className="rounded-xl border border-slate-200 bg-white p-4 shadow-sm space-y-2.5">
          <div className="flex items-center justify-between">
            <div className="flex items-center gap-2">
              <Sliders className="h-4 w-4 text-teal-600" aria-hidden="true" />
              <h4 className="text-xs font-semibold text-slate-900">
                Umbrales Ambientales y Movimiento (RF08 / RF29)
              </h4>
            </div>
            {userRole === "principal" && (
              <button
                type="button"
                onClick={() => {
                  const opening = !showThresholdsForm;
                  showThresholdsFormRef.current = opening;
                  setShowThresholdsForm(opening);
                  if (opening && live) {
                    syncThresholdInputsFromData(live);
                  }
                }}
                className="rounded-lg border border-teal-600 bg-teal-50 px-2.5 py-1 text-xs font-medium text-teal-800 hover:bg-teal-100"
              >
                {showThresholdsForm ? "Cerrar" : "Configurar"}
              </button>
            )}
          </div>
          <p className="text-xs text-slate-600">
            T: {live.umbrales.tempMin}–{live.umbrales.tempMax} °C · H: {live.umbrales.humMin}–
            {live.umbrales.humMax} % · Mov: {live.umbrales.movimientoUmbral} kg/min (
            {live.umbrales.movimientoAlertaActiva ? "alerta activa" : "alerta silenciada"}) · Escala D14:{" "}
            {live.umbrales.escalaMasa}
          </p>

          {showThresholdsForm && userRole === "principal" && (
            <form onSubmit={handleSaveThresholds} className="grid grid-cols-2 gap-2 pt-2 border-t border-slate-100">
              <div>
                <label className="block text-[11px] text-slate-600">Temp. Mín (°C)</label>
                <input
                  type="number"
                  step="0.5"
                  value={tempMin}
                  onChange={(e) => setTempMin(e.target.value)}
                  className="w-full rounded border border-slate-300 px-2 py-1 text-xs"
                />
              </div>
              <div>
                <label className="block text-[11px] text-slate-600">Temp. Máx (°C)</label>
                <input
                  type="number"
                  step="0.5"
                  value={tempMax}
                  onChange={(e) => setTempMax(e.target.value)}
                  className="w-full rounded border border-slate-300 px-2 py-1 text-xs"
                />
              </div>
              <div>
                <label className="block text-[11px] text-slate-600">Hum. Mín (%)</label>
                <input
                  type="number"
                  step="1"
                  value={humMin}
                  onChange={(e) => setHumMin(e.target.value)}
                  className="w-full rounded border border-slate-300 px-2 py-1 text-xs"
                />
              </div>
              <div>
                <label className="block text-[11px] text-slate-600">Hum. Máx (%)</label>
                <input
                  type="number"
                  step="1"
                  value={humMax}
                  onChange={(e) => setHumMax(e.target.value)}
                  className="w-full rounded border border-slate-300 px-2 py-1 text-xs"
                />
              </div>
              <div>
                <label className="block text-[11px] text-slate-600">Umbral Mov. (kg/min)</label>
                <input
                  type="number"
                  step="0.01"
                  value={movUmbral}
                  onChange={(e) => setMovUmbral(e.target.value)}
                  className="w-full rounded border border-slate-300 px-2 py-1 text-xs"
                />
              </div>
              <div>
                <label className="block text-[11px] text-slate-600">Escala Masa (D14)</label>
                <input
                  type="number"
                  step="0.05"
                  value={escalaMasa}
                  onChange={(e) => setEscalaMasa(e.target.value)}
                  className="w-full rounded border border-slate-300 px-2 py-1 text-xs"
                />
              </div>
              <div className="col-span-2 flex items-center justify-between pt-1">
                <label className="inline-flex items-center gap-1.5 text-xs text-slate-700">
                  <input
                    type="checkbox"
                    checked={movAlertaActiva}
                    onChange={(e) => setMovAlertaActiva(e.target.checked)}
                  />
                  Alerta por movimiento anómalo (30 s — RF29)
                </label>
                <button
                  type="submit"
                  className="rounded-lg bg-teal-600 px-3 py-1 text-xs font-medium text-white hover:bg-teal-700"
                >
                  Guardar y enviar por TCP
                </button>
              </div>
            </form>
          )}
        </div>
      </div>

      {/* Gráfico de Tendencia Reciente (Recharts) y Alertas en Vivo */}
      <div className="grid grid-cols-1 gap-3 md:grid-cols-2">
        <div className="rounded-xl border border-slate-200 bg-white p-4 shadow-sm">
          <h4 className="text-xs font-semibold text-slate-900 mb-2">
            Tendencia de Microclima y Movimiento (Tiempo Real)
          </h4>
          {chartData.length === 0 ? (
            <p className="text-xs text-slate-500 py-6 text-center">
              Sin muestras recientes de telemetría.
            </p>
          ) : (
            <div className="h-44 w-full">
              <ResponsiveContainer width="100%" height="100%">
                <LineChart data={chartData}>
                  <CartesianGrid strokeDasharray="3 3" stroke="#e2e8f0" />
                  <XAxis dataKey="hora" tick={{ fontSize: 10, fill: "#64748b" }} />
                  <YAxis yAxisId="env" tick={{ fontSize: 10, fill: "#64748b" }} />
                  <YAxis yAxisId="mov" orientation="right" tick={{ fontSize: 10, fill: "#64748b" }} domain={[0, "auto"]} />
                  <Tooltip contentStyle={{ borderRadius: "0.75rem", fontSize: "11px", borderColor: "#cbd5e1" }} />
                  <Legend wrapperStyle={{ fontSize: "11px" }} />
                  <Line yAxisId="env" type="monotone" dataKey="tempC" stroke="#0d9488" strokeWidth={2} connectNulls name="Temp (°C)" dot={false} />
                  <Line yAxisId="env" type="monotone" dataKey="humedad" stroke="#0284c7" strokeWidth={2} connectNulls name="Humedad (%)" dot={false} />
                  <Line yAxisId="mov" type="monotone" dataKey="movimiento" stroke="#d97706" strokeWidth={2} connectNulls name="Mov (kg/m)" dot={false} />
                </LineChart>
              </ResponsiveContainer>
            </div>
          )}
        </div>

        <div className="rounded-xl border border-slate-200 bg-white p-4 shadow-sm space-y-2">
          <div className="flex items-center gap-1.5">
            <Bell className="h-4 w-4 text-teal-600" aria-hidden="true" />
            <h4 className="text-xs font-semibold text-slate-900">
              Eventos y Alertas Recientes ({live.alertasRecientes.length})
            </h4>
          </div>
          {live.alertasRecientes.length === 0 ? (
            <p className="text-xs text-slate-500 py-4 text-center">
              No se han registrado alertas recientes en esta cuna.
            </p>
          ) : (
            <ul className="max-h-36 overflow-y-auto divide-y divide-slate-100">
              {live.alertasRecientes.slice(0, 6).map((alert) => (
                <li key={alert.id} className="py-1.5 flex items-start justify-between gap-2">
                  <div>
                    <p className="text-xs font-medium text-slate-800">
                      {alert.mensaje ?? alert.tipo}
                    </p>
                    <p className="text-[10px] text-slate-500">
                      {new Date(alert.ts).toLocaleTimeString()} · Tipo: {alert.tipo}
                    </p>
                  </div>
                  <span
                    className={`shrink-0 rounded-full px-2 py-0.5 text-[10px] font-semibold ${
                      alert.severidad === "critica"
                        ? "bg-red-100 text-red-800"
                        : alert.severidad === "advertencia"
                        ? "bg-amber-100 text-amber-800"
                        : "bg-slate-100 text-slate-700"
                    }`}
                  >
                    {alert.severidad.toUpperCase()}
                  </span>
                </li>
              ))}
            </ul>
          )}
        </div>
      </div>
    </div>
  );
}
