import { useState } from "react";
import { FileDown, Moon } from "lucide-react";
import {
  accountApi,
  ApiError,
  type FeedingSessionRecord,
  type SleepSessionRecord,
  type ToyUsageRecord,
} from "../../lib/api";

interface HistoryAndExportCardProps {
  accessToken: string;
  cunaId: string;
  bebeId: string | null;
  activeProfileId: string | null;
  filterFrom: string;
  filterTo: string;
  feedingHistory: FeedingSessionRecord[];
  sleepHistory: SleepSessionRecord[];
  toyUsageHistory: ToyUsageRecord[];
  onFilterFromChange: (value: string) => void;
  onFilterToChange: (value: string) => void;
  onHistoryChanged: () => Promise<void>;
  onError: (msg: string | null) => void;
}

/**
 * Decodifica una carga Base64 del backend y dispara la descarga en navegador como Blob binario.
 */
function triggerBase64Download(base64: string, contentType: string, fileName: string): void {
  const binary = window.atob(base64);
  const bytes = new Uint8Array(binary.length);
  for (let i = 0; i < binary.length; i++) {
    bytes[i] = binary.charCodeAt(i);
  }
  const blob = new Blob([bytes], { type: contentType });
  const url = URL.createObjectURL(blob);
  const anchor = document.createElement("a");
  anchor.href = url;
  anchor.download = fileName;
  anchor.click();
  URL.revokeObjectURL(url);
}

/**
 * Panel unificado de historiales de sueño (RF30), alimentación (RF31),
 * uso de juguete/móvil (RF36) y exportación filtrada a CSV/PDF (RF32).
 */
export function HistoryAndExportCard({
  accessToken,
  cunaId,
  bebeId,
  activeProfileId,
  filterFrom,
  filterTo,
  feedingHistory,
  sleepHistory,
  toyUsageHistory,
  onFilterFromChange,
  onFilterToChange,
  onHistoryChanged,
  onError,
}: HistoryAndExportCardProps) {
  const [exportStatusMsg, setExportStatusMsg] = useState<string | null>(null);
  const [isExporting, setIsExporting] = useState<boolean>(false);

  async function handleRecordSampleSleep(tipo: "siesta" | "noche") {
    if (!bebeId) {
      onError("Asocia primero un perfil de bebé a esta cuna (RF30).");
      return;
    }
    onError(null);
    try {
      const fin = new Date();
      const inicio = new Date(fin.getTime() - (tipo === "siesta" ? 45 : 360) * 60 * 1000);
      await accountApi.createSleepSession(accessToken, bebeId, {
        cunaId,
        tipo,
        inicio: inicio.toISOString(),
        fin: fin.toISOString(),
        movimientoPromedio: 0.042,
        interrupciones: tipo === "siesta" ? 0 : 1,
        perfilId: activeProfileId,
      });
      await onHistoryChanged();
    } catch (err) {
      if (err instanceof ApiError) {
        onError(err.message);
      }
    }
  }

  async function handleExport(formato: "csv" | "pdf") {
    if (!bebeId) {
      onError("Asocia primero un perfil de bebé para exportar su historial (RF32).");
      return;
    }
    onError(null);
    setExportStatusMsg(null);
    setIsExporting(true);
    try {
      const res = await accountApi.exportBabyHistory(accessToken, bebeId, {
        formato,
        desde: filterFrom || null,
        hasta: filterTo || null,
        perfilId: activeProfileId,
      });

      triggerBase64Download(res.contentBase64, res.contentType, res.fileName);

      setExportStatusMsg(
        `Exportado ${res.fileName} (${res.feedingSessionsCount} tomas, ${res.sleepSessionsCount} sueños) en ${res.elapsedMs} ms.`
      );
    } catch (err) {
      if (err instanceof ApiError) {
        onError(err.message);
      }
    } finally {
      setIsExporting(false);
    }
  }

  return (
    <div className="rounded-xl border border-slate-200 bg-white p-4 shadow-sm space-y-3">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <div className="flex items-center gap-2">
          <Moon className="h-4 w-4 text-teal-600" aria-hidden="true" />
          <h4 className="text-xs font-semibold text-slate-900">
            Historiales de Sueño (RF30), Alimentación (RF31) y Exportación PDF/CSV (RF32)
          </h4>
        </div>

        <div className="flex flex-wrap items-center gap-2">
          <input
            type="date"
            value={filterFrom}
            onChange={(e) => onFilterFromChange(e.target.value)}
            aria-label="Filtrar desde fecha"
            className="rounded border border-slate-300 px-2 py-1 text-xs"
          />
          <input
            type="date"
            value={filterTo}
            onChange={(e) => onFilterToChange(e.target.value)}
            aria-label="Filtrar hasta fecha"
            className="rounded border border-slate-300 px-2 py-1 text-xs"
          />
          <button
            type="button"
            onClick={() => void handleRecordSampleSleep("siesta")}
            disabled={!bebeId}
            className="rounded-lg border border-slate-300 bg-slate-50 px-2.5 py-1 text-xs font-medium text-slate-700 hover:bg-slate-100 disabled:opacity-50"
          >
            + Siesta
          </button>
          <button
            type="button"
            onClick={() => void handleRecordSampleSleep("noche")}
            disabled={!bebeId}
            className="rounded-lg border border-slate-300 bg-slate-50 px-2.5 py-1 text-xs font-medium text-slate-700 hover:bg-slate-100 disabled:opacity-50"
          >
            + Noche
          </button>
          <button
            type="button"
            onClick={() => void handleExport("csv")}
            disabled={!bebeId || isExporting}
            className="inline-flex items-center gap-1 rounded-lg border border-teal-600 bg-teal-50 px-2.5 py-1 text-xs font-medium text-teal-800 hover:bg-teal-100 disabled:opacity-50 transition-colors"
          >
            <FileDown className="h-3.5 w-3.5" aria-hidden="true" />
            {isExporting ? "Exportando..." : "CSV"}
          </button>
          <button
            type="button"
            onClick={() => void handleExport("pdf")}
            disabled={!bebeId || isExporting}
            className="inline-flex items-center gap-1 rounded-lg bg-slate-900 px-2.5 py-1 text-xs font-medium text-white hover:bg-slate-800 disabled:opacity-50 transition-colors"
          >
            <FileDown className="h-3.5 w-3.5" aria-hidden="true" />
            {isExporting ? "Exportando..." : "PDF"}
          </button>
        </div>
      </div>

      {exportStatusMsg && (
        <p className="rounded-lg bg-emerald-50 px-3 py-1.5 text-xs font-medium text-emerald-800">
          {exportStatusMsg}
        </p>
      )}

      <div className="grid grid-cols-1 gap-3 md:grid-cols-2">
        <div>
          <h5 className="text-[11px] font-semibold uppercase tracking-wider text-slate-500 mb-1.5">
            Tomas registradas ({feedingHistory.length})
          </h5>
          {feedingHistory.length === 0 ? (
            <p className="text-xs text-slate-500">Sin sesiones de alimentación en el rango.</p>
          ) : (
            <ul className="max-h-32 overflow-y-auto divide-y divide-slate-100 text-xs">
              {feedingHistory.slice(0, 8).map((f) => (
                <li key={f.id} className="py-1.5 flex items-center justify-between">
                  <span>
                    {new Date(f.inicio).toLocaleString()} · {f.masaInicialG}g →{" "}
                    {f.masaFinalG !== null ? `${f.masaFinalG}g` : "en curso"}
                  </span>
                  <span className="font-semibold text-teal-700">
                    {f.mlConsumidos !== null ? `${f.mlConsumidos.toFixed(1)} ml` : "—"}
                  </span>
                </li>
              ))}
            </ul>
          )}
        </div>

        <div>
          <h5 className="text-[11px] font-semibold uppercase tracking-wider text-slate-500 mb-1.5">
            Sesiones de sueño ({sleepHistory.length})
          </h5>
          {sleepHistory.length === 0 ? (
            <p className="text-xs text-slate-500">Sin sesiones de sueño en el rango.</p>
          ) : (
            <ul className="max-h-32 overflow-y-auto divide-y divide-slate-100 text-xs">
              {sleepHistory.slice(0, 8).map((s) => (
                <li key={s.id} className="py-1.5 flex items-center justify-between">
                  <span>
                    [{s.tipo.toUpperCase()}] {new Date(s.inicio).toLocaleString()}
                  </span>
                  <span className="text-slate-600">
                    Mov: {s.movimientoPromedio ?? 0} kg/m · Int: {s.interrupciones}
                  </span>
                </li>
              ))}
            </ul>
          )}
        </div>
      </div>

      {/* RF36: Uso reciente de Juguete Didáctico y Móvil de LEDs */}
      <div className="pt-2 border-t border-slate-100">
        <h5 className="text-[11px] font-semibold uppercase tracking-wider text-slate-500 mb-1.5">
          Actividad de Juguete y Móvil (RF36) ({toyUsageHistory.length})
        </h5>
        {toyUsageHistory.length === 0 ? (
          <p className="text-xs text-slate-500">Sin sesiones recientes de juguete didáctico o móvil.</p>
        ) : (
          <ul className="max-h-28 overflow-y-auto divide-y divide-slate-100 text-xs">
            {toyUsageHistory.slice(0, 6).map((item) => (
              <li key={item.id} className="py-1.5 flex flex-wrap items-center justify-between gap-2">
                <span>
                  <strong className="uppercase">{item.tipo}</strong> ·{" "}
                  {new Date(item.inicio).toLocaleString()}
                  {item.patron ? ` · Patrón: ${item.patron}` : ""}
                  {item.velocidad ? ` (${item.velocidad})` : ""}
                </span>
                <span className="text-slate-600 font-medium">
                  {item.duracionSegundos} s
                  {item.aciertos !== null ? ` · Aciertos: ${item.aciertos}` : ""}
                </span>
              </li>
            ))}
          </ul>
        )}
      </div>
    </div>
  );
}
