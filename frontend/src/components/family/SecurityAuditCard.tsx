import { Shield } from "lucide-react";
import type { SecurityAuditEntry } from "../../lib/api";

interface SecurityAuditCardProps {
  entries: SecurityAuditEntry[];
  loading: boolean;
  onRefresh: () => void;
}

/**
 * Bitácora de auditoría de seguridad familiar visible únicamente para el Usuario Principal (RNF03).
 */
export function SecurityAuditCard({
  entries,
  loading,
  onRefresh,
}: SecurityAuditCardProps) {
  return (
    <section className="rounded-2xl border border-slate-200 bg-white p-6 shadow-sm space-y-3">
      <div className="flex items-center justify-between">
        <div className="flex items-center gap-2">
          <Shield className="h-5 w-5 text-teal-600" aria-hidden="true" />
          <h2 className="text-base font-semibold text-slate-900">
            Auditoría de Seguridad Familiar (RNF03)
          </h2>
        </div>
        <button
          type="button"
          disabled={loading}
          onClick={onRefresh}
          className="rounded-lg border border-slate-300 bg-white px-3 py-1 text-xs font-medium text-slate-700 hover:bg-slate-50 disabled:opacity-50"
        >
          {loading ? "Actualizando..." : "Actualizar bitácora"}
        </button>
      </div>

      {entries.length === 0 ? (
        <p className="text-xs text-slate-500">
          Sin eventos recientes registrados en la bitácora de seguridad.
        </p>
      ) : (
        <div className="overflow-x-auto">
          <table className="w-full text-left text-xs">
            <thead className="border-b border-slate-200 text-[11px] uppercase text-slate-500">
              <tr>
                <th className="py-1.5 pr-3">Fecha</th>
                <th className="py-1.5 pr-3">Evento</th>
                <th className="py-1.5 pr-3">IP / Dispositivo</th>
                <th className="py-1.5">Detalle</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-slate-100">
              {entries.map((entry, idx) => (
                <tr key={`${entry.ts}-${entry.evento}-${idx}`}>
                  <td className="py-1.5 pr-3 whitespace-nowrap text-slate-500">
                    {new Date(entry.ts).toLocaleString()}
                  </td>
                  <td className="py-1.5 pr-3 font-mono font-medium text-slate-800">
                    {entry.evento}
                  </td>
                  <td className="py-1.5 pr-3 font-mono text-slate-600">
                    {entry.dispositivoId ?? entry.ipOrigen ?? "—"}
                  </td>
                  <td className="py-1.5 text-slate-600">
                    {entry.detalle ?? "—"}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </section>
  );
}
