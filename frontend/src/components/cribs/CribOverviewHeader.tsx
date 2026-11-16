import { useState, type FormEvent } from "react";
import { Cpu, Edit3, Unlink } from "lucide-react";
import { accountApi, ApiError, type CribSummaryResponse } from "../../lib/api";

interface CribOverviewHeaderProps {
  accessToken: string;
  cribs: CribSummaryResponse[];
  selectedCunaId: string | null;
  activeCribSummary: CribSummaryResponse | null;
  isPrincipal: boolean;
  onSelectCrib: (cunaId: string) => void;
  onDataChanged: () => Promise<void>;
  onError: (msg: string | null) => void;
}

/**
 * Encabezado y resumen de la cuna seleccionada con selector por pestañas,
 * renombrado (RF26) y desvinculación con confirmación explícita (RF27).
 */
export function CribOverviewHeader({
  accessToken,
  cribs,
  selectedCunaId,
  activeCribSummary,
  isPrincipal,
  onSelectCrib,
  onDataChanged,
  onError,
}: CribOverviewHeaderProps) {
  const [editingCunaId, setEditingCunaId] = useState<string | null>(null);
  const [renameValue, setRenameValue] = useState("");
  const [unlinkingCunaId, setUnlinkingCunaId] = useState<string | null>(null);
  const [unlinkConfirmText, setUnlinkConfirmText] = useState("");
  const [isRenaming, setIsRenaming] = useState(false);
  const [isUnlinking, setIsUnlinking] = useState(false);

  async function handleRenameCrib(e: FormEvent, cunaId: string) {
    e.preventDefault();
    onError(null);
    setIsRenaming(true);
    try {
      await accountApi.renameCrib(accessToken, cunaId, renameValue.trim());
      setEditingCunaId(null);
      setRenameValue("");
      await onDataChanged();
    } catch (err) {
      if (err instanceof ApiError) {
        onError(err.message);
      }
    } finally {
      setIsRenaming(false);
    }
  }

  async function handleUnlinkCrib(e: FormEvent, cunaId: string) {
    e.preventDefault();
    onError(null);
    setIsUnlinking(true);
    try {
      await accountApi.unlinkCrib(accessToken, cunaId, unlinkConfirmText.trim());
      setUnlinkingCunaId(null);
      setUnlinkConfirmText("");
      await onDataChanged();
    } catch (err) {
      if (err instanceof ApiError) {
        onError(err.message);
      }
    } finally {
      setIsUnlinking(false);
    }
  }

  return (
    <>
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div className="flex items-center gap-2">
          <Cpu className="h-5 w-5 text-teal-600" aria-hidden="true" />
          <div>
            <h2 className="text-base font-semibold text-slate-900">
              Cunas y Dispositivos Vinculados (RF25 / RF26 / RF27)
            </h2>
            <p className="text-xs text-slate-500">
              Selecciona una cuna para supervisar sus nodos Pico W, renombrarla o usar el intercomunicador.
            </p>
          </div>
        </div>

        {cribs.length > 0 && (
          <div className="flex flex-wrap gap-2" role="tablist" aria-label="Selector de cuna">
            {cribs.map((item) => {
              const isSelected = item.cuna.id === selectedCunaId;
              return (
                <button
                  key={item.cuna.id}
                  type="button"
                  role="tab"
                  aria-selected={isSelected}
                  onClick={() => onSelectCrib(item.cuna.id)}
                  className={`rounded-xl px-3.5 py-1.5 text-xs font-medium transition-colors ${
                    isSelected
                      ? "bg-teal-600 text-white shadow-sm"
                      : "border border-slate-200 bg-slate-50 text-slate-700 hover:bg-slate-100"
                  }`}
                >
                  {item.cuna.nombre}
                </button>
              );
            })}
          </div>
        )}
      </div>

      {cribs.length === 0 ? (
        <div className="rounded-xl border border-dashed border-slate-300 bg-slate-50 p-6 text-center text-xs text-slate-600">
          No hay cunas registradas aún. Usa el panel de Emparejamiento (RF24) abajo para vincular una Unidad Central y crear tu primera cuna.
        </div>
      ) : (
        activeCribSummary && (
          <>
            <div className="flex flex-wrap items-center justify-between gap-3 rounded-xl border border-slate-200 bg-slate-50 p-4">
              <div>
                <div className="flex items-center gap-2">
                  <h3 className="text-sm font-semibold text-slate-900">
                    {activeCribSummary.cuna.nombre}
                  </h3>
                  <span
                    className={`rounded-full px-2.5 py-0.5 text-[11px] font-medium ${
                      activeCribSummary.cuna.modoOperacion === "alarma"
                        ? "bg-red-100 text-red-800"
                        : activeCribSummary.cuna.modoOperacion === "atencion"
                          ? "bg-amber-100 text-amber-800"
                          : "bg-emerald-100 text-emerald-800"
                    }`}
                  >
                    Estado: {activeCribSummary.cuna.modoOperacion.toUpperCase()}
                  </span>
                </div>
                <p className="text-xs text-slate-600 mt-1">
                  Bebé asociado:{" "}
                  <span className="font-medium text-slate-800">
                    {activeCribSummary.bebe ? activeCribSummary.bebe.nombre : "Sin bebé asignado"}
                  </span>
                </p>
              </div>

              {isPrincipal && (
                <div className="flex items-center gap-2">
                  <button
                    type="button"
                    onClick={() => {
                      setEditingCunaId(activeCribSummary.cuna.id);
                      setRenameValue(activeCribSummary.cuna.nombre);
                      setUnlinkingCunaId(null);
                    }}
                    className="inline-flex items-center gap-1 rounded-lg border border-slate-300 bg-white px-2.5 py-1.5 text-xs font-medium text-slate-700 hover:bg-slate-100"
                  >
                    <Edit3 className="h-3.5 w-3.5" aria-hidden="true" />
                    Renombrar (RF26)
                  </button>
                  <button
                    type="button"
                    onClick={() => {
                      setUnlinkingCunaId(activeCribSummary.cuna.id);
                      setUnlinkConfirmText("");
                      setEditingCunaId(null);
                    }}
                    className="inline-flex items-center gap-1 rounded-lg border border-red-200 bg-red-50 px-2.5 py-1.5 text-xs font-medium text-red-700 hover:bg-red-100"
                  >
                    <Unlink className="h-3.5 w-3.5" aria-hidden="true" />
                    Desvincular (RF27)
                  </button>
                </div>
              )}
            </div>

            {editingCunaId === activeCribSummary.cuna.id && (
              <form
                onSubmit={(e) => void handleRenameCrib(e, activeCribSummary.cuna.id)}
                className="flex flex-wrap items-center gap-2 rounded-xl border border-teal-200 bg-teal-50/50 p-3"
              >
                <input
                  type="text"
                  required
                  minLength={1}
                  maxLength={30}
                  value={renameValue}
                  onChange={(e) => setRenameValue(e.target.value)}
                  placeholder="Nuevo nombre de la cuna (1–30 caracteres)"
                  className="flex-1 rounded-lg border border-slate-300 bg-white px-3 py-1.5 text-xs"
                />
                <button
                  type="submit"
                  disabled={isRenaming}
                  className="rounded-lg bg-teal-600 px-3 py-1.5 text-xs font-medium text-white hover:bg-teal-700 disabled:opacity-50 transition-colors"
                >
                  {isRenaming ? "Guardando..." : "Guardar nombre"}
                </button>
                <button
                  type="button"
                  onClick={() => setEditingCunaId(null)}
                  className="rounded-lg border border-slate-300 bg-white px-2.5 py-1.5 text-xs text-slate-600"
                >
                  Cancelar
                </button>
              </form>
            )}

            {unlinkingCunaId === activeCribSummary.cuna.id && (
              <form
                onSubmit={(e) => void handleUnlinkCrib(e, activeCribSummary.cuna.id)}
                className="space-y-2 rounded-xl border border-red-300 bg-red-50 p-4"
              >
                <p className="text-xs font-semibold text-red-900">
                  Confirmación explícita de desvinculación (RF27)
                </p>
                <p className="text-[11px] text-red-800">
                  Esta acción revoca los tokens de todos los dispositivos de la cuna, cierra sus sesiones TCP activas y conserva el perfil e historial del bebé. Escribe{" "}
                  <code className="font-mono font-bold">DESVINCULAR</code> para confirmar:
                </p>
                <div className="flex flex-wrap items-center gap-2">
                  <input
                    type="text"
                    required
                    value={unlinkConfirmText}
                    onChange={(e) => setUnlinkConfirmText(e.target.value)}
                    placeholder="Escribe DESVINCULAR"
                    className="rounded-lg border border-red-300 bg-white px-3 py-1.5 text-xs font-mono"
                  />
                  <button
                    type="submit"
                    disabled={unlinkConfirmText.trim().toUpperCase() !== "DESVINCULAR" || isUnlinking}
                    className="rounded-lg bg-red-600 px-3 py-1.5 text-xs font-medium text-white hover:bg-red-700 disabled:opacity-50 transition-colors"
                  >
                    {isUnlinking ? "Desvinculando..." : "Confirmar desvinculación"}
                  </button>
                  <button
                    type="button"
                    onClick={() => setUnlinkingCunaId(null)}
                    className="rounded-lg border border-slate-300 bg-white px-2.5 py-1.5 text-xs text-slate-700"
                  >
                    Cancelar
                  </button>
                </div>
              </form>
            )}
          </>
        )
      )}
    </>
  );
}
