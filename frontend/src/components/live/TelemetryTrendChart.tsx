import { Activity } from "lucide-react";
import {
  CartesianGrid,
  Legend,
  Line,
  LineChart,
  ResponsiveContainer,
  Tooltip,
  XAxis,
  YAxis,
} from "recharts";
import type { TelemetryRecord } from "../../lib/api";

interface TelemetryTrendChartProps {
  samples: TelemetryRecord[];
}

/**
 * Gráfica lineal en tiempo real de temperatura, humedad e índice de movimiento (Recharts).
 */
export function TelemetryTrendChart({ samples }: TelemetryTrendChartProps) {
  const chartData = samples.map((t) => ({
    hora: new Date(t.ts).toLocaleTimeString([], {
      hour: "2-digit",
      minute: "2-digit",
      second: "2-digit",
    }),
    tempC: t.tempC,
    humedad: t.humedad,
    movimiento: t.indiceMovimiento,
  }));

  return (
    <div className="rounded-xl border border-slate-200 bg-white p-4 shadow-sm space-y-2.5">
      <div className="flex items-center justify-between gap-2">
        <h4 className="text-xs font-semibold text-slate-900">
          Tendencia de Microclima y Movimiento (Tiempo Real)
        </h4>
        <span className="rounded-full bg-slate-100 px-2 py-0.5 text-[10px] font-medium text-slate-600 tabular-nums">
          {chartData.length} muestras
        </span>
      </div>
      {chartData.length === 0 ? (
        <div className="flex flex-col items-center justify-center rounded-lg border border-dashed border-slate-200 bg-slate-50/70 py-6 text-center">
          <Activity className="h-5 w-5 text-slate-400 mb-1" aria-hidden="true" />
          <p className="text-xs text-slate-500">
            Sin muestras recientes de telemetría.
          </p>
        </div>
      ) : (
        <div className="h-44 w-full">
          <ResponsiveContainer width="100%" height="100%">
            <LineChart data={chartData}>
              <CartesianGrid strokeDasharray="3 3" stroke="#e2e8f0" />
              <XAxis dataKey="hora" tick={{ fontSize: 10, fill: "#64748b" }} />
              <YAxis yAxisId="env" tick={{ fontSize: 10, fill: "#64748b" }} />
              <YAxis
                yAxisId="mov"
                orientation="right"
                tick={{ fontSize: 10, fill: "#64748b" }}
                domain={[0, "auto"]}
              />
              <Tooltip
                contentStyle={{
                  borderRadius: "0.75rem",
                  fontSize: "11px",
                  borderColor: "#cbd5e1",
                }}
              />
              <Legend wrapperStyle={{ fontSize: "11px" }} />
              <Line
                yAxisId="env"
                type="monotone"
                dataKey="tempC"
                stroke="#0d9488"
                strokeWidth={2}
                connectNulls
                name="Temp (°C)"
                dot={false}
              />
              <Line
                yAxisId="env"
                type="monotone"
                dataKey="humedad"
                stroke="#0284c7"
                strokeWidth={2}
                connectNulls
                name="Humedad (%)"
                dot={false}
              />
              <Line
                yAxisId="mov"
                type="monotone"
                dataKey="movimiento"
                stroke="#d97706"
                strokeWidth={2}
                connectNulls
                name="Mov (kg/m)"
                dot={false}
              />
            </LineChart>
          </ResponsiveContainer>
        </div>
      )}
    </div>
  );
}
