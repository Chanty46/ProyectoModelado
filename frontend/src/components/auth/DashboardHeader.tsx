import { LogOut, ShieldCheck } from "lucide-react";
import type {
  AccountContextResponse,
  CaregiverProfileRecord,
  HealthStatusResponse,
} from "../../lib/api";

interface DashboardHeaderProps {
  account: AccountContextResponse | null;
  activeCaregiver: CaregiverProfileRecord | null;
  accessToken: string | null;
  health: HealthStatusResponse | null;
  healthOffline: boolean;
  onSignOut: () => void;
}

/**
 * Barra superior fija de la consola familiar con turno de cuidador activo,
 * estado del servidor TCP/API local y cierre de sesión.
 */
export function DashboardHeader({
  account,
  activeCaregiver,
  accessToken,
  health,
  healthOffline,
  onSignOut,
}: DashboardHeaderProps) {
  return (
    <header className="sticky top-0 z-30 border-b border-slate-200/80 bg-white/90 backdrop-blur-md">
      <div className="mx-auto flex max-w-5xl flex-wrap items-center justify-between gap-4 px-6 py-3.5">
        <div className="flex items-center gap-3">
          <div className="flex h-10 w-10 items-center justify-center rounded-xl bg-gradient-to-br from-teal-500 to-emerald-600 text-white shadow-sm">
            <ShieldCheck className="h-5 w-5" aria-hidden="true" />
          </div>
          <div>
            <div className="flex items-center gap-2">
              <h1 className="text-base sm:text-lg font-bold tracking-tight text-slate-900">
                Cuna Inteligente
              </h1>
              {activeCaregiver && (
                <span className="inline-flex items-center gap-1.5 rounded-full bg-teal-50 border border-teal-200 pl-1 pr-2.5 py-0.5 text-[11px] font-medium text-teal-800">
                  <span
                    aria-hidden="true"
                    className="inline-flex h-4 w-4 items-center justify-center rounded-full bg-teal-600 text-[9px] font-bold text-white"
                  >
                    {activeCaregiver.avatar.slice(0, 2).toUpperCase()}
                  </span>
                  <span className="sm:hidden">{activeCaregiver.nombre.split(" ")[0]}</span>
                  <span className="hidden sm:inline">Turno: {activeCaregiver.nombre}</span>
                </span>
              )}
            </div>
            <p className="text-xs text-slate-500">
              {account?.familia
                ? `${account.familia.nombre} · ${
                    account.rol === "principal" ? "Usuario Principal" : "Usuario Secundario"
                  }`
                : "Supervisión Perimetral, Microclima y Nutrición Infantil"}
            </p>
          </div>
        </div>

        <div className="flex items-center gap-2.5">
          {health && !healthOffline ? (
            <span
              title={`TCP :${health.tcpPort} · Última verificación: ${new Date(
                health.timestamp
              ).toLocaleTimeString()}`}
              className="inline-flex items-center gap-1.5 rounded-full border border-emerald-200 bg-emerald-50 px-2.5 py-1 text-[11px] font-medium text-emerald-700"
            >
              <span className="h-2 w-2 rounded-full bg-emerald-500 animate-pulse" />
              TCP :{health.tcpPort} · {health.connectedDevices} nodo(s)
            </span>
          ) : (
            <span className="inline-flex items-center gap-1.5 rounded-full border border-slate-200 bg-slate-100 px-2.5 py-1 text-[11px] font-medium text-slate-600">
              <span className="h-2 w-2 rounded-full bg-slate-400" />
              {healthOffline ? "API local sin conexión" : "Conectando API..."}
            </span>
          )}

          {accessToken && (
            <button
              type="button"
              onClick={onSignOut}
              className="inline-flex items-center gap-1.5 rounded-lg border border-slate-300 bg-white px-3 py-1.5 text-xs font-medium text-slate-700 hover:bg-slate-100 transition-colors"
            >
              <LogOut className="h-3.5 w-3.5" aria-hidden="true" />
              Cerrar sesión
            </button>
          )}
        </div>
      </div>
    </header>
  );
}
