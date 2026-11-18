import { Sun } from "lucide-react";

interface NightLightControlCardProps {
  luzEncendida: boolean;
  luzNivel: number;
  lightLevel: number;
  onSliderChange: (nextLevel: number) => void;
  onApplyLight: (encendida: boolean, nivel: number) => void;
}

const LIGHT_PRESETS = [25, 50, 80, 100] as const;

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
    <div className="rounded-xl border border-slate-200 bg-white p-4 shadow-sm space-y-3">
      <div className="flex items-center justify-between gap-2">
        <div className="flex items-center gap-2">
          <span
            className="flex h-7 w-7 items-center justify-center rounded-lg border border-amber-200/80 transition-colors"
            style={{
              backgroundColor: luzEncendida
                ? `rgba(251, 191, 36, ${Math.max(0.2, luzNivel / 100)})`
                : "#f8fafc",
            }}
          >
            <Sun
              className={`h-4 w-4 ${luzEncendida ? "text-amber-700" : "text-slate-400"}`}
              aria-hidden="true"
            />
          </span>
          <h4 className="text-xs font-semibold text-slate-900">
            Iluminación Nocturna Gradual PWM (RF09)
          </h4>
        </div>
        <span
          className={`rounded-full px-2.5 py-0.5 text-[11px] font-medium ${
            luzEncendida ? "bg-amber-50 text-amber-800" : "bg-slate-100 text-slate-600"
          }`}
        >
          {luzEncendida ? `Encendida (${luzNivel} %)` : "Apagada"}
        </span>
      </div>

      <div className="flex flex-wrap items-center gap-3">
        <input
          type="range"
          min={5}
          max={100}
          step={5}
          value={lightLevel}
          onChange={(e) => onSliderChange(Number(e.target.value))}
          aria-label="Intensidad de luz nocturna"
          className="flex-1 min-w-[140px]"
        />
        <button
          type="button"
          onClick={() => onApplyLight(true, lightLevel)}
          className="rounded-lg bg-amber-500 px-3 py-1.5 text-xs font-medium text-white hover:bg-amber-600 transition-colors"
        >
          Aplicar {lightLevel} %
        </button>
        {luzEncendida && (
          <button
            type="button"
            onClick={() => onApplyLight(false, 0)}
            className="rounded-lg border border-slate-300 bg-white px-2.5 py-1.5 text-xs text-slate-700 hover:bg-slate-100 transition-colors"
          >
            Apagar
          </button>
        )}
      </div>

      <div className="flex flex-wrap items-center justify-between gap-2">
        <div className="flex flex-wrap items-center gap-1.5" aria-label="Niveles rápidos de luz">
          {LIGHT_PRESETS.map((preset) => (
            <button
              key={preset}
              type="button"
              onClick={() => {
                onSliderChange(preset);
                onApplyLight(true, preset);
              }}
              className={`rounded-md border px-2 py-0.5 text-[11px] font-medium transition-colors ${
                luzEncendida && luzNivel === preset
                  ? "border-amber-500 bg-amber-50 text-amber-800"
                  : "border-slate-200 bg-slate-50 text-slate-600 hover:bg-slate-100"
              }`}
            >
              {preset}%
            </button>
          ))}
        </div>
        <p className="text-[11px] text-slate-500">
          Rampa suave 1.5 s (PWM ≥ 1 kHz)
        </p>
      </div>
    </div>
  );
}
