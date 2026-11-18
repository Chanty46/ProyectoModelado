import { useState } from "react";
import { Cpu, Flame, Gamepad2, Hand, RotateCcw, ShieldAlert, BatteryWarning, Waves } from "lucide-react";
import { accountApi, ApiError } from "../../lib/api";

interface DemoSimulatorCardProps {
  accessToken: string;
  cunaId: string;
  onSimulated: () => void;
  onError: (msg: string | null) => void;
}

const SIMULATION_ACTIONS = [
  {
    action: "removal_alarm",
    label: "Retiro súbito (5 s → Alarma + Telegram)",
    Icon: ShieldAlert,
    btnClass: "border-red-300 bg-red-50 text-red-800 hover:bg-red-100",
  },
  {
    action: "button_attention",
    label: "Botón físico Atención (RF05)",
    Icon: Hand,
    btnClass: "border-amber-300 bg-amber-50 text-amber-900 hover:bg-amber-100",
  },
  {
    action: "env_alert",
    label: "Clima fuera de rango (31.8 °C)",
    Icon: Flame,
    btnClass: "border-orange-300 bg-orange-50 text-orange-900 hover:bg-orange-100",
  },
  {
    action: "movement_alert",
    label: "Movimiento alto (RF29)",
    Icon: Waves,
    btnClass: "border-sky-300 bg-sky-50 text-sky-900 hover:bg-sky-100",
  },
  {
    action: "bottle_low_battery",
    label: "Batería baja biberón (8 %)",
    Icon: BatteryWarning,
    btnClass: "border-amber-300 bg-amber-50 text-amber-900 hover:bg-amber-100",
  },
  {
    action: "toy_game",
    label: "Partida juguete 5 botones",
    Icon: Gamepad2,
    btnClass: "border-indigo-300 bg-indigo-50 text-indigo-900 hover:bg-indigo-100",
  },
  {
    action: "reset_normal",
    label: "Restaurar lectura normal",
    Icon: RotateCcw,
    btnClass: "border-emerald-300 bg-emerald-50 text-emerald-800 hover:bg-emerald-100",
  },
] as const;

/**
 * Panel de simulación de eventos de hardware sin placas físicas para pruebas y
 * presentación preliminar (solo visible en modo de demostración local).
 */
export function DemoSimulatorCard({
  accessToken,
  cunaId,
  onSimulated,
  onError,
}: DemoSimulatorCardProps) {
  const [busyAction, setBusyAction] = useState<string | null>(null);
  const [statusMessage, setStatusMessage] = useState<string | null>(null);

  async function handleTrigger(action: string) {
    onError(null);
    setBusyAction(action);
    try {
      const res = await accountApi.simulateDemoEvent(accessToken, cunaId, action);
      setStatusMessage(res.message);
      onSimulated();
      if (action === "removal_alarm") {
        // Refrescar automáticamente tras los 5 s de ventana de gracia para mostrar la transición a ALARMA
        window.setTimeout(() => onSimulated(), 5400);
      }
    } catch (err) {
      if (err instanceof ApiError) {
        onError(err.message);
      }
    } finally {
      setBusyAction(null);
    }
  }

  return (
    <div className="rounded-xl border border-dashed border-teal-300 bg-teal-50/40 p-4 space-y-3">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <div className="flex items-center gap-2">
          <Cpu className="h-4 w-4 text-teal-700" aria-hidden="true" />
          <h4 className="text-xs font-semibold text-teal-950">
            Simulador de Eventos (Presentación sin Hardware Físico)
          </h4>
        </div>
        <span className="rounded-full bg-white border border-teal-200 px-2.5 py-0.5 text-[10px] font-medium text-teal-800">
          Dispara reglas reales + alertas @CunaIntBot
        </span>
      </div>

      <div className="flex flex-wrap gap-2">
        {SIMULATION_ACTIONS.map(({ action, label, Icon, btnClass }) => (
          <button
            key={action}
            type="button"
            disabled={busyAction !== null}
            onClick={() => void handleTrigger(action)}
            className={`inline-flex items-center gap-1.5 rounded-lg border px-2.5 py-1.5 text-xs font-medium transition-colors disabled:opacity-50 ${btnClass}`}
          >
            <Icon className="h-3.5 w-3.5 shrink-0" aria-hidden="true" />
            {busyAction === action ? "Ejecutando..." : label}
          </button>
        ))}
      </div>

      {statusMessage && (
        <p className="rounded-lg border border-teal-200 bg-white px-3 py-2 text-xs text-teal-900">
          {statusMessage}
        </p>
      )}
    </div>
  );
}
