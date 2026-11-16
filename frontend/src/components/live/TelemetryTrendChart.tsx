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
    <div className="rounded-xl border border-slate-200 bg-white p-4 shadow-sm">
      <h4 className="text-xs font-semibold text-slate-900 mb-2">
        Tendencia de Microclima y Movimiento (Tiempo Real)
      </h4>
      {chartData.length === 0 ? (
        <p className="text-xs text-slate-500 py-6 text-center">
          Sin muestras recientes de telemetría.
        </p>
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
