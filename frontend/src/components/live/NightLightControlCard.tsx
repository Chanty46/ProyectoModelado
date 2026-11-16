import { Sun } from "lucide-react";

interface NightLightControlCardProps {
  luzEncendida: boolean;
  luzNivel: number;
  lightLevel: number;
  onSliderChange: (nextLevel: number) => void;
  onApplyLight: (encendida: boolean, nivel: number) => void;
}

/**
 * Control de iluminación nocturna gradual PWM con deslizador de intensidad (RF09).
 */
export function NightLightControlCard({
  luzEncendida,
  luzNivel,
  lightLevel,
  onSliderChange,
  onApplyLight,
}: NightLightControlCardProps) {
  return (
    <div className="rounded-xl border border-slate-200 bg-white p-4 shadow-sm space-y-2.5">
      <div className="flex items-center justify-between">
        <div className="flex items-center gap-2">
          <Sun className="h-4 w-4 text-amber-500" aria-hidden="true" />
          <h4 className="text-xs font-semibold text-slate-900">
            Iluminación Nocturna Gradual PWM (RF09)
          </h4>
        </div>
        <span className="text-xs font-medium text-slate-600">
          {luzEncendida ? `Encendida (${luzNivel} %)` : "Apagada"}
        </span>
      </div>

      <div className="flex items-center gap-3">
        <input
          type="range"
          min={5}
          max={100}
          step={5}
          value={lightLevel}
          onChange={(e) => onSliderChange(Number(e.target.value))}
          aria-label="Intensidad de luz nocturna"
          className="flex-1"
        />
        <button
          type="button"
          onClick={() => onApplyLight(true, lightLevel)}
          className="rounded-lg bg-amber-500 px-3 py-1.5 text-xs font-medium text-white hover:bg-amber-600"
        >
          Aplicar {lightLevel} %
        </button>
        {luzEncendida && (
          <button
            type="button"
            onClick={() => onApplyLight(false, 0)}
            className="rounded-lg border border-slate-300 bg-white px-2.5 py-1.5 text-xs text-slate-700 hover:bg-slate-100"
          >
            Apagar
          </button>
        )}
      </div>
      <p className="text-[11px] text-slate-500">
        Transición suave en rampa de 1.5 s (PWM ≥ 1 kHz en Unidad Central).
      </p>
    </div>
  );
}
