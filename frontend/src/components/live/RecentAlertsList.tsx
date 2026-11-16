import { Bell } from "lucide-react";
import type { EventAlertRecord } from "../../lib/api";

interface RecentAlertsListProps {
  alerts: EventAlertRecord[];
}

/**
 * Bitácora resumida de los últimos 6 eventos y alertas registrados en la cuna.
 */
export function RecentAlertsList({ alerts }: RecentAlertsListProps) {
  return (
    <div className="rounded-xl border border-slate-200 bg-white p-4 shadow-sm space-y-2">
      <div className="flex items-center gap-1.5">
        <Bell className="h-4 w-4 text-teal-600" aria-hidden="true" />
        <h4 className="text-xs font-semibold text-slate-900">
          Eventos y Alertas Recientes ({alerts.length})
        </h4>
      </div>
      {alerts.length === 0 ? (
        <p className="text-xs text-slate-500 py-4 text-center">
          No se han registrado alertas recientes en esta cuna.
        </p>
      ) : (
        <ul className="max-h-36 overflow-y-auto divide-y divide-slate-100">
          {alerts.slice(0, 6).map((alert) => (
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
  );
}
