import { Activity, CheckCircle2, Droplets, Scale, Thermometer } from "lucide-react";
import type { CribConfig, TelemetryRecord } from "../../lib/api";
import { evaluateComfortBand } from "../../lib/formatters";

interface TelemetryMetricCardsProps {
  latest: TelemetryRecord | null;
  thresholds: CribConfig;
  babyPresent: boolean;
  presenceThresholdKg: number;
}

/**
 * Cuatro tarjetas de telemetría en tiempo real: temperatura, humedad,
 * masa del colchón (con presencia) e índice de movimiento (IU-01, RF06, RF07, RF29).
 */
export function TelemetryMetricCards({
  latest,
  thresholds,
  babyPresent,
  presenceThresholdKg,
}: TelemetryMetricCardsProps) {
  const tempBand = evaluateComfortBand(latest?.tempC, thresholds.tempMin, thresholds.tempMax);
  const humBand = evaluateComfortBand(latest?.humedad, thresholds.humMin, thresholds.humMax);
  const movPercent =
    latest?.indiceMovimiento !== null &&
    latest?.indiceMovimiento !== undefined &&
    thresholds.movimientoUmbral > 0
      ? Math.min(100, Math.round((latest.indiceMovimiento / thresholds.movimientoUmbral) * 100))
      : 0;

  return (
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
          Umbral: {thresholds.tempMin}–{thresholds.tempMax} °C
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
          Umbral: {thresholds.humMin}–{thresholds.humMax} %
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
          Máx: {thresholds.movimientoUmbral} kg/min
        </p>
      </div>
    </div>
  );
}
