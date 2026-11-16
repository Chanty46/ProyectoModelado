import type { FormEvent } from "react";
import { Sliders } from "lucide-react";
import type { CribConfig } from "../../lib/api";
import type { ThresholdFormFields } from "../../hooks/useLiveCribTelemetry";

interface ThresholdsConfigCardProps {
  currentThresholds: CribConfig;
  isPrincipal: boolean;
  showForm: boolean;
  formValues: ThresholdFormFields;
  onToggleForm: () => void;
  onChangeField: (patch: Partial<ThresholdFormFields>) => void;
  onSubmit: (e: FormEvent) => void;
}

/**
 * Tarjeta de resumen y edición de umbrales ambientales, movimiento y escala de masa D14 (RF08 / RF29).
 * Solo el Usuario Principal puede abrir el formulario y enviar cambios por TCP.
 */
export function ThresholdsConfigCard({
  currentThresholds,
  isPrincipal,
  showForm,
  formValues,
  onToggleForm,
  onChangeField,
  onSubmit,
}: ThresholdsConfigCardProps) {
  return (
    <div className="rounded-xl border border-slate-200 bg-white p-4 shadow-sm space-y-2.5">
      <div className="flex items-center justify-between">
        <div className="flex items-center gap-2">
          <Sliders className="h-4 w-4 text-teal-600" aria-hidden="true" />
          <h4 className="text-xs font-semibold text-slate-900">
            Umbrales Ambientales y Movimiento (RF08 / RF29)
          </h4>
        </div>
        {isPrincipal && (
          <button
            type="button"
            onClick={onToggleForm}
            className="rounded-lg border border-teal-600 bg-teal-50 px-2.5 py-1 text-xs font-medium text-teal-800 hover:bg-teal-100"
          >
            {showForm ? "Cerrar" : "Configurar"}
          </button>
        )}
      </div>
      <p className="text-xs text-slate-600">
        T: {currentThresholds.tempMin}–{currentThresholds.tempMax} °C · H: {currentThresholds.humMin}–
        {currentThresholds.humMax} % · Mov: {currentThresholds.movimientoUmbral} kg/min (
        {currentThresholds.movimientoAlertaActiva ? "alerta activa" : "alerta silenciada"}) · Escala D14:{" "}
        {currentThresholds.escalaMasa}
      </p>

      {showForm && isPrincipal && (
        <form onSubmit={onSubmit} className="grid grid-cols-2 gap-2 pt-2 border-t border-slate-100">
          <div>
            <label className="block text-[11px] text-slate-600">Temp. Mín (°C)</label>
            <input
              type="number"
              step="0.5"
              value={formValues.tempMin}
              onChange={(e) => onChangeField({ tempMin: e.target.value })}
              className="w-full rounded border border-slate-300 px-2 py-1 text-xs"
            />
          </div>
          <div>
            <label className="block text-[11px] text-slate-600">Temp. Máx (°C)</label>
            <input
              type="number"
              step="0.5"
              value={formValues.tempMax}
              onChange={(e) => onChangeField({ tempMax: e.target.value })}
              className="w-full rounded border border-slate-300 px-2 py-1 text-xs"
            />
          </div>
          <div>
            <label className="block text-[11px] text-slate-600">Hum. Mín (%)</label>
            <input
              type="number"
              step="1"
              value={formValues.humMin}
              onChange={(e) => onChangeField({ humMin: e.target.value })}
              className="w-full rounded border border-slate-300 px-2 py-1 text-xs"
            />
          </div>
          <div>
            <label className="block text-[11px] text-slate-600">Hum. Máx (%)</label>
            <input
              type="number"
              step="1"
              value={formValues.humMax}
              onChange={(e) => onChangeField({ humMax: e.target.value })}
              className="w-full rounded border border-slate-300 px-2 py-1 text-xs"
            />
          </div>
          <div>
            <label className="block text-[11px] text-slate-600">Umbral Mov. (kg/min)</label>
            <input
              type="number"
              step="0.01"
              value={formValues.movUmbral}
              onChange={(e) => onChangeField({ movUmbral: e.target.value })}
              className="w-full rounded border border-slate-300 px-2 py-1 text-xs"
            />
          </div>
          <div>
            <label className="block text-[11px] text-slate-600">Escala Masa (D14)</label>
            <input
              type="number"
              step="0.05"
              value={formValues.escalaMasa}
              onChange={(e) => onChangeField({ escalaMasa: e.target.value })}
              className="w-full rounded border border-slate-300 px-2 py-1 text-xs"
            />
          </div>
          <div className="col-span-2 flex items-center justify-between pt-1">
            <label className="inline-flex items-center gap-1.5 text-xs text-slate-700">
              <input
                type="checkbox"
                checked={formValues.movAlertaActiva}
                onChange={(e) => onChangeField({ movAlertaActiva: e.target.checked })}
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
  );
}
