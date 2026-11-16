import { useState, type FormEvent } from "react";
import { AlertTriangle, Milk } from "lucide-react";
import { accountApi, ApiError, type FeedingSessionRecord } from "../../lib/api";
import {
  estimateMilkMl,
  evaluateComfortBand,
  parseOptionalFiniteNumber,
} from "../../lib/formatters";

interface FeedingSessionCardProps {
  accessToken: string;
  bebeId: string | null;
  activeProfileId: string | null;
  initialMassG: string;
  foodTempC: string;
  activeFeedingSession: FeedingSessionRecord | null;
  onInitialMassChange: (val: string) => void;
  onFoodTempChange: (val: string) => void;
  onSessionStarted: (session: FeedingSessionRecord) => Promise<void>;
  onSessionFinished: () => Promise<void>;
  onError: (msg: string | null) => void;
}

/**
 * Tarjeta de nutrición infantil y control de base de biberón (RF15–RF17, RF37).
 * Muestra la banda térmica IR segura (35.0–38.5 °C), alertas por temperatura fuera
 * de rango y el flujo de inicio/cierre de toma con cálculo estimado en mililitros.
 */
export function FeedingSessionCard({
  accessToken,
  bebeId,
  activeProfileId,
  initialMassG,
  foodTempC,
  activeFeedingSession,
  onInitialMassChange,
  onFoodTempChange,
  onSessionStarted,
  onSessionFinished,
  onError,
}: FeedingSessionCardProps) {
  const [finalMassG, setFinalMassG] = useState<string>("95.0");
  const [isSubmittingFeeding, setIsSubmittingFeeding] = useState<boolean>(false);

  async function handleStartFeeding(e: FormEvent) {
    e.preventDefault();
    if (!bebeId) {
      onError("Registra y asocia un perfil de bebé a esta cuna antes de iniciar una toma (RF37).");
      return;
    }
    onError(null);
    // Usa parseOptionalFiniteNumber para evitar convertir entradas vacías en 0 gramos
    const parsedInitialMassG = parseOptionalFiniteNumber(initialMassG);
    if (parsedInitialMassG === null) {
      onError("Ingresa una masa inicial válida en gramos.");
      return;
    }
    const parsedFoodTempC = parseOptionalFiniteNumber(foodTempC);

    setIsSubmittingFeeding(true);
    try {
      const created = await accountApi.startFeedingSession(accessToken, {
        bebeId,
        masaInicialG: parsedInitialMassG,
        tempC: parsedFoodTempC,
        perfilId: activeProfileId,
      });
      await onSessionStarted(created);
    } catch (err) {
      if (err instanceof ApiError) {
        onError(err.message);
      }
    } finally {
      setIsSubmittingFeeding(false);
    }
  }

  async function handleFinishFeeding(e: FormEvent) {
    e.preventDefault();
    if (!activeFeedingSession) {
      return;
    }
    onError(null);
    const parsedFinalMassG = parseOptionalFiniteNumber(finalMassG);
    if (parsedFinalMassG === null) {
      onError("Ingresa una masa final válida en gramos.");
      return;
    }

    setIsSubmittingFeeding(true);
    try {
      await accountApi.finishFeedingSession(accessToken, activeFeedingSession.id, {
        masaFinalG: parsedFinalMassG,
        perfilId: activeProfileId,
      });
      await onSessionFinished();
    } catch (err) {
      if (err instanceof ApiError) {
        onError(err.message);
      }
    } finally {
      setIsSubmittingFeeding(false);
    }
  }

  const parsedTemp = parseOptionalFiniteNumber(foodTempC);
  const isTempUnsafe = parsedTemp !== null && (parsedTemp < 35.0 || parsedTemp > 38.5);
  const bottleTempBand = evaluateComfortBand(parsedTemp, 35.0, 38.5);
  const parsedFinalMass = parseOptionalFiniteNumber(finalMassG);
  const estimatedConsumedMl =
    activeFeedingSession && parsedFinalMass !== null
      ? estimateMilkMl(activeFeedingSession.masaInicialG, parsedFinalMass)
      : null;

  return (
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

      {/* Banda térmica visual segura (35.0 – 38.5 °C) */}
      <div className="rounded-lg border border-slate-100 bg-slate-50 p-2.5 space-y-1.5">
        <div className="flex items-center justify-between text-[11px]">
          <span className="font-medium text-slate-700">Banda Térmica IR (RF16)</span>
          <span
            className={`font-semibold ${
              bottleTempBand.status === "ok"
                ? "text-emerald-700"
                : bottleTempBand.status === "unknown"
                ? "text-slate-500"
                : "text-amber-700"
            }`}
          >
            {parsedTemp === null ? "—" : `${parsedTemp.toFixed(1)} °C`} · {bottleTempBand.label}
          </span>
        </div>
        <div className="relative h-2 w-full rounded-full overflow-hidden flex bg-slate-200">
          <div className="h-full w-1/4 bg-sky-300" title="Frío (< 35.0 °C)" />
          <div className="h-full w-2/4 bg-emerald-400" title="Rango seguro (35.0–38.5 °C)" />
          <div className="h-full w-1/4 bg-rose-400" title="Caliente (> 38.5 °C)" />
          {bottleTempBand.status !== "unknown" && (
            <div
              className="absolute top-0 h-2 w-1.5 -translate-x-1/2 rounded-full bg-slate-900 ring-2 ring-white transition-all duration-300"
              style={{ left: `${bottleTempBand.barPercent}%` }}
            />
          )}
        </div>
        <div className="flex justify-between text-[10px] text-slate-500">
          <span>Frío &lt;35 °C</span>
          <span className="font-medium text-emerald-700">Ideal 35.0–38.5 °C</span>
          <span>Caliente &gt;38.5 °C</span>
        </div>
      </div>

      {isTempUnsafe && parsedTemp !== null && (
        <div className="flex items-center gap-2 rounded-lg border border-amber-300 bg-amber-50 p-2 text-xs text-amber-900">
          <AlertTriangle className="h-4 w-4 shrink-0 text-amber-600" aria-hidden="true" />
          <span>
            Alerta térmica (RF16): {parsedTemp.toFixed(1)} °C está fuera del rango seguro (35.0–38.5 °C).
          </span>
        </div>
      )}

      {!activeFeedingSession ? (
        <form onSubmit={(e) => void handleStartFeeding(e)} className="space-y-2.5">
          <div className="grid grid-cols-2 gap-2">
            <div>
              <label htmlFor="feeding-initial-mass" className="block text-[11px] font-medium text-slate-700 mb-0.5">
                Masa inicial M_i (≥ 20 g)
              </label>
              <input
                id="feeding-initial-mass"
                type="number"
                step="0.5"
                min="20"
                max="2000"
                required
                value={initialMassG}
                onChange={(e) => onInitialMassChange(e.target.value)}
                className="w-full rounded-lg border border-slate-300 px-2.5 py-1.5 text-xs"
              />
            </div>
            <div>
              <label htmlFor="feeding-food-temp" className="block text-[11px] font-medium text-slate-700 mb-0.5">
                Temperatura IR (°C)
              </label>
              <input
                id="feeding-food-temp"
                type="number"
                step="0.1"
                required
                value={foodTempC}
                onChange={(e) => onFoodTempChange(e.target.value)}
                className="w-full rounded-lg border border-slate-300 px-2.5 py-1.5 text-xs"
              />
            </div>
          </div>
          <button
            type="submit"
            disabled={!bebeId || isSubmittingFeeding}
            className="w-full rounded-xl bg-teal-600 px-3 py-2 text-xs font-medium text-white hover:bg-teal-700 disabled:opacity-50 transition-colors"
          >
            {isSubmittingFeeding ? "Iniciando toma..." : "Iniciar Sesión de Alimentación (RF37)"}
          </button>
        </form>
      ) : (
        <form onSubmit={(e) => void handleFinishFeeding(e)} className="space-y-2.5 rounded-lg border border-teal-200 bg-teal-50/50 p-3">
          <div className="flex flex-wrap items-center justify-between gap-2 text-xs text-teal-900">
            <p className="font-medium">
              Toma en curso · M_inicial: <strong>{activeFeedingSession.masaInicialG} g</strong>
              {activeFeedingSession.tempC !== null ? ` · Temp: ${activeFeedingSession.tempC} °C` : ""}
            </p>
            <span className="rounded-full bg-white border border-teal-200 px-2 py-0.5 text-[11px] font-semibold text-teal-800">
              Ingesta est.: {estimatedConsumedMl !== null ? `${estimatedConsumedMl.toFixed(1)} ml` : "—"}
            </span>
          </div>
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
              disabled={isSubmittingFeeding}
              className="rounded-lg bg-teal-600 px-3 py-1.5 text-xs font-medium text-white hover:bg-teal-700 disabled:opacity-50 transition-colors"
            >
              {isSubmittingFeeding ? "Finalizando..." : "Finalizar y calcular ml (RF17)"}
            </button>
          </div>
        </form>
      )}
    </div>
  );
}
