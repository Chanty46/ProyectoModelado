/**
 * Esqueleto animado de carga mostrado mientras se sincroniza la primera lectura en vivo.
 */
export function LivePanelSkeleton() {
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
