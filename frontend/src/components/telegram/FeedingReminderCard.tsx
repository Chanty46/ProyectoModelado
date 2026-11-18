import type { FormEvent } from "react";
import { CheckCircle2, Clock } from "lucide-react";
import type { FeedingReminderRecord } from "../../lib/api";

interface FeedingReminderCardProps {
  reminder: FeedingReminderRecord | null;
  intervalHours: number;
  reminderActive: boolean;
  isSaving: boolean;
  savedNotice: boolean;
  onIntervalChange: (hours: number) => void;
  onActiveChange: (active: boolean) => void;
  onSave: (e: FormEvent) => void;
}

/**
 * Tarjeta de configuración del recordatorio dinámico de alimentación entre 1 y 8 horas (RF18).
 */
export function FeedingReminderCard({
  reminder,
  intervalHours,
  reminderActive,
  isSaving,
  savedNotice,
  onIntervalChange,
  onActiveChange,
  onSave,
}: FeedingReminderCardProps) {
  return (
    <section className="rounded-2xl border border-slate-200 bg-white p-6 shadow-sm space-y-4">
      <div className="flex items-center justify-between gap-2">
        <div className="flex items-center gap-2.5">
          <span className="flex h-8 w-8 items-center justify-center rounded-xl bg-amber-50 text-amber-600">
            <Clock className="h-4 w-4" aria-hidden="true" />
          </span>
          <h2 className="text-base font-semibold text-slate-900">
            Recordatorio Dinámico de Tomas (RF18)
          </h2>
        </div>
        {reminder && (
          <span
            className={`rounded-full px-2.5 py-0.5 text-xs font-medium ${
              reminder.activo
                ? "bg-emerald-50 text-emerald-700"
                : "bg-slate-100 text-slate-600"
            }`}
          >
            {reminder.activo ? `Activo cada ${reminder.intervaloHoras} h` : "Pausado"}
          </span>
        )}
      </div>

      <p className="text-xs leading-relaxed text-slate-600">
        El servicio en segundo plano evalúa el tiempo transcurrido desde la última toma y notifica por el panel y Telegram cuando se cumple el intervalo (1 a 8 horas).
      </p>

      <form onSubmit={onSave} className="space-y-3.5 pt-3 border-t border-slate-100">
        <div className="space-y-2">
          <div className="flex items-center justify-between gap-4">
            <label htmlFor="reminder-interval" className="text-xs font-medium text-slate-700">
              Intervalo entre tomas (1–8 horas):
            </label>
            <select
              id="reminder-interval"
              value={intervalHours}
              onChange={(e) => onIntervalChange(Number(e.target.value))}
              className="rounded-lg border border-slate-300 px-3 py-1.5 text-xs"
            >
              {[1, 2, 3, 4, 5, 6, 7, 8].map((h) => (
                <option key={h} value={h}>
                  Cada {h} {h === 1 ? "hora" : "horas"}
                </option>
              ))}
            </select>
          </div>
          <div className="flex flex-wrap gap-1.5" aria-label="Accesos rápidos de intervalo">
            {[2, 3, 4, 6, 8].map((h) => (
              <button
                key={h}
                type="button"
                onClick={() => onIntervalChange(h)}
                className={`rounded-lg px-2.5 py-1 text-[11px] font-medium border transition-colors ${
                  intervalHours === h
                    ? "border-teal-600 bg-teal-50 text-teal-800"
                    : "border-slate-200 bg-white text-slate-600 hover:bg-slate-50"
                }`}
              >
                {h}h
              </button>
            ))}
          </div>
        </div>

        <label className="inline-flex items-center gap-2 text-xs text-slate-700">
          <input
            type="checkbox"
            checked={reminderActive}
            onChange={(e) => onActiveChange(e.target.checked)}
          />
          Activar notificaciones automáticas de recordatorio de toma
        </label>

        <button
          type="submit"
          disabled={isSaving}
          className="w-full rounded-xl bg-teal-600 px-4 py-2 text-xs font-medium text-white hover:bg-teal-700 disabled:opacity-50 transition-colors"
        >
          {isSaving ? "Guardando configuración..." : "Guardar configuración de recordatorio"}
        </button>

        {savedNotice && (
          <p
            role="status"
            className="flex items-center gap-1.5 rounded-lg border border-emerald-200 bg-emerald-50 px-3 py-2 text-xs text-emerald-800"
          >
            <CheckCircle2 className="h-3.5 w-3.5 shrink-0" aria-hidden="true" />
            Configuración de recordatorio guardada.
          </p>
        )}
      </form>
    </section>
  );
}
