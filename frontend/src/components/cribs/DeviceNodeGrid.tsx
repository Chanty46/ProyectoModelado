import { Battery, BedDouble, Cpu, Gamepad2, Milk, Sparkles, Wifi, WifiOff } from "lucide-react";
import type { ClaimableDeviceRecord } from "../../lib/api";

interface DeviceNodeGridProps {
  devices: ClaimableDeviceRecord[];
}

const NODE_META: Record<string, { label: string; badgeBg: string; Icon: typeof Cpu }> = {
  central: { label: "Unidad Central", badgeBg: "bg-teal-50 text-teal-700", Icon: Cpu },
  colchon: { label: "Colchón Sensor", badgeBg: "bg-indigo-50 text-indigo-700", Icon: BedDouble },
  movil: { label: "Móvil Colgante", badgeBg: "bg-violet-50 text-violet-700", Icon: Sparkles },
  biberon: { label: "Base Biberón", badgeBg: "bg-sky-50 text-sky-700", Icon: Milk },
  juguete: { label: "Juguete Cognitivo", badgeBg: "bg-amber-50 text-amber-700", Icon: Gamepad2 },
};

/**
 * Cuadrícula visual de los nodos Raspberry Pi Pico W asociados a una cuna (RF25).
 */
export function DeviceNodeGrid({ devices }: DeviceNodeGridProps) {
  return (
    <div>
      <h4 className="text-xs font-semibold uppercase tracking-wider text-slate-500 mb-2">
        Dispositivos de la cuna ({devices.length})
      </h4>
      <div className="grid grid-cols-1 gap-2.5 sm:grid-cols-2 md:grid-cols-3">
        {devices.map((dev) => {
          const enLinea = dev.estado === "en_linea";
          const meta = NODE_META[dev.tipo] ?? {
            label: dev.tipo,
            badgeBg: "bg-slate-100 text-slate-700",
            Icon: Cpu,
          };
          const NodeIcon = meta.Icon;

          return (
            <div
              key={dev.id}
              className="flex items-center justify-between gap-2 rounded-xl border border-slate-200 bg-white p-3 shadow-sm hover:border-slate-300 transition-colors"
            >
              <div className="flex items-center gap-2.5 min-w-0">
                <div className={`flex h-9 w-9 shrink-0 items-center justify-center rounded-lg ${meta.badgeBg}`}>
                  <NodeIcon className="h-4 w-4" aria-hidden="true" />
                </div>
                <div className="min-w-0">
                  <p className="text-xs font-semibold text-slate-800 truncate">{meta.label}</p>
                  <p className="text-[11px] font-mono text-slate-500 truncate">
                    {dev.deviceId}
                    {dev.fwVersion ? ` · v${dev.fwVersion}` : ""}
                  </p>
                </div>
              </div>
              <div className="flex flex-col items-end gap-1 shrink-0">
                <span
                  className={`inline-flex items-center gap-1 rounded-full px-2 py-0.5 text-[10px] font-semibold ${
                    enLinea
                      ? "bg-emerald-50 text-emerald-700"
                      : "bg-slate-100 text-slate-600"
                  }`}
                >
                  {enLinea ? (
                    <>
                      <Wifi className="h-3 w-3" aria-hidden="true" />
                      En línea
                    </>
                  ) : (
                    <>
                      <WifiOff className="h-3 w-3" aria-hidden="true" />
                      Offline
                    </>
                  )}
                </span>
                {dev.bateriaPct !== null && dev.bateriaPct !== undefined && (
                  <span
                    className={`inline-flex items-center gap-1 text-[10px] font-medium ${
                      dev.bateriaPct < 20 ? "text-red-600" : "text-slate-500"
                    }`}
                  >
                    <Battery className="h-3 w-3" aria-hidden="true" />
                    {dev.bateriaPct}%
                  </span>
                )}
              </div>
            </div>
          );
        })}
      </div>
    </div>
  );
}
