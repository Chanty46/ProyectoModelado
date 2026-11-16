import { useEffect, useState, type FormEvent } from "react";
import { Baby, Edit3, PlusCircle } from "lucide-react";
import {
  accountApi,
  ApiError,
  type BabyRecord,
  type CribSummaryResponse,
} from "../../lib/api";

interface BabyProfilePanelProps {
  accessToken: string;
  babies: BabyRecord[];
  cribs: CribSummaryResponse[];
  selectedCunaId: string | null;
  isPrincipal: boolean;
  onSaved: () => Promise<void>;
  onError: (msg: string | null) => void;
}

/**
 * Panel de alta y edición del perfil del bebé asociado a una cuna (RF28).
 */
export function BabyProfilePanel({
  accessToken,
  babies,
  cribs,
  selectedCunaId,
  isPrincipal,
  onSaved,
  onError,
}: BabyProfilePanelProps) {
  const [editingBabyId, setEditingBabyId] = useState<string | null>(null);
  const [babyName, setBabyName] = useState("");
  const [babyBirthDate, setBabyBirthDate] = useState("2026-05-15");
  const [babyMedicalNotes, setBabyMedicalNotes] = useState("");
  const [babyCunaId, setBabyCunaId] = useState<string>("");
  const [isSavingBaby, setIsSavingBaby] = useState(false);

  useEffect(() => {
    if (cribs.length > 0) {
      setBabyCunaId((prev) =>
        prev && cribs.some((c) => c.cuna.id === prev) ? prev : cribs[0].cuna.id
      );
    } else {
      setBabyCunaId("");
    }
  }, [cribs]);

  async function handleSaveBaby(e: FormEvent) {
    e.preventDefault();
    onError(null);
    const targetCuna = babyCunaId || selectedCunaId;
    if (!targetCuna) {
      onError("Debes registrar una cuna primero para asociar al bebé (RF28).");
      return;
    }
    setIsSavingBaby(true);
    try {
      const payload = {
        cunaId: targetCuna,
        nombre: babyName.trim(),
        fechaNacimiento: babyBirthDate,
        notas: babyMedicalNotes.trim() || null,
      };
      if (editingBabyId) {
        await accountApi.updateBaby(accessToken, editingBabyId, payload);
      } else {
        await accountApi.createBaby(accessToken, payload);
      }
      setEditingBabyId(null);
      setBabyName("");
      setBabyMedicalNotes("");
      await onSaved();
    } catch (err) {
      if (err instanceof ApiError) {
        onError(err.message);
      }
    } finally {
      setIsSavingBaby(false);
    }
  }

  function startEditBaby(baby: BabyRecord) {
    setEditingBabyId(baby.id);
    setBabyName(baby.nombre);
    setBabyBirthDate(baby.fechaNacimiento);
    setBabyMedicalNotes(baby.notas ?? "");
    setBabyCunaId(baby.cunaId ?? selectedCunaId ?? "");
  }

  return (
    <section className="rounded-2xl border border-slate-200 bg-white p-6 shadow-sm space-y-4">
      <div className="flex items-center justify-between">
        <div className="flex items-center gap-2">
          <Baby className="h-5 w-5 text-teal-600" aria-hidden="true" />
          <h2 className="text-base font-semibold text-slate-900">
            Perfil del Bebé (RF28)
          </h2>
        </div>
      </div>

      {babies.length > 0 && (
        <ul className="divide-y divide-slate-100">
          {babies.map((b) => {
            const cunaAsignada = cribs.find((c) => c.cuna.id === b.cunaId)?.cuna.nombre;
            return (
              <li key={b.id} className="flex items-center justify-between py-2.5">
                <div>
                  <p className="text-sm font-medium text-slate-800">{b.nombre}</p>
                  <p className="text-[11px] text-slate-500">
                    Nacimiento: {b.fechaNacimiento} · Cuna:{" "}
                    {cunaAsignada ?? "Desvinculada (historial conservado)"}
                  </p>
                  {b.notas && (
                    <p className="text-[11px] text-slate-600 mt-0.5">
                      Notas: {b.notas}
                    </p>
                  )}
                </div>
                {isPrincipal && (
                  <button
                    type="button"
                    onClick={() => startEditBaby(b)}
                    className="inline-flex items-center gap-1 rounded-md px-2 py-1 text-xs font-medium text-teal-700 hover:bg-teal-50"
                  >
                    <Edit3 className="h-3.5 w-3.5" aria-hidden="true" />
                    Editar
                  </button>
                )}
              </li>
            );
          })}
        </ul>
      )}

      {isPrincipal ? (
        <form onSubmit={(e) => void handleSaveBaby(e)} className="space-y-3 pt-2 border-t border-slate-100">
          <h3 className="text-xs font-semibold uppercase tracking-wider text-slate-500">
            {editingBabyId ? "Actualizar perfil del bebé" : "Registrar perfil de bebé"}
          </h3>

          <div className="grid grid-cols-1 gap-2 sm:grid-cols-2">
            <div>
              <label htmlFor="baby-name" className="block text-xs font-medium text-slate-700 mb-1">
                Nombre del bebé (1–60 caracteres)
              </label>
              <input
                id="baby-name"
                type="text"
                required
                maxLength={60}
                value={babyName}
                onChange={(e) => setBabyName(e.target.value)}
                placeholder="Ej. Bebé Sofía"
                className="w-full rounded-lg border border-slate-300 px-3 py-1.5 text-xs"
              />
            </div>

            <div>
              <label htmlFor="baby-birthdate" className="block text-xs font-medium text-slate-700 mb-1">
                Fecha de nacimiento
              </label>
              <input
                id="baby-birthdate"
                type="date"
                required
                value={babyBirthDate}
                onChange={(e) => setBabyBirthDate(e.target.value)}
                className="w-full rounded-lg border border-slate-300 px-3 py-1.5 text-xs"
              />
            </div>
          </div>

          <div>
            <label htmlFor="baby-cuna" className="block text-xs font-medium text-slate-700 mb-1">
              Asociar a cuna
            </label>
            <select
              id="baby-cuna"
              required={cribs.length > 0}
              value={babyCunaId || selectedCunaId || cribs[0]?.cuna.id || ""}
              onChange={(e) => setBabyCunaId(e.target.value)}
              className="w-full rounded-lg border border-slate-300 px-2.5 py-1.5 text-xs"
            >
              {cribs.length === 0 ? (
                <option value="">Registra primero una cuna para asociar</option>
              ) : (
                cribs.map((c) => (
                  <option key={c.cuna.id} value={c.cuna.id}>
                    {c.cuna.nombre}
                  </option>
                ))
              )}
            </select>
          </div>

          <div>
            <label htmlFor="baby-notes" className="block text-xs font-medium text-slate-700 mb-1">
              Notas médicas u observaciones (opcional)
            </label>
            <input
              id="baby-notes"
              type="text"
              maxLength={1000}
              value={babyMedicalNotes}
              onChange={(e) => setBabyMedicalNotes(e.target.value)}
              placeholder="Alergias, pediatra, observaciones..."
              className="w-full rounded-lg border border-slate-300 px-3 py-1.5 text-xs"
            />
          </div>

          <div className="flex items-center gap-2">
            <button
              type="submit"
              disabled={isSavingBaby || cribs.length === 0}
              className="inline-flex items-center gap-1.5 rounded-xl bg-teal-600 px-4 py-2 text-xs font-medium text-white hover:bg-teal-700 disabled:opacity-50 transition-colors"
            >
              <PlusCircle className="h-3.5 w-3.5" aria-hidden="true" />
              {isSavingBaby
                ? "Guardando..."
                : editingBabyId
                ? "Guardar cambios del bebé"
                : "Registrar bebé"}
            </button>
            {editingBabyId && (
              <button
                type="button"
                onClick={() => {
                  setEditingBabyId(null);
                  setBabyName("");
                  setBabyMedicalNotes("");
                }}
                className="rounded-xl border border-slate-300 bg-white px-3 py-2 text-xs text-slate-700"
              >
                Cancelar edición
              </button>
            )}
          </div>
        </form>
      ) : (
        <p className="rounded-xl border border-slate-200 bg-slate-50 px-3 py-2 text-xs text-slate-600">
          Solo el Usuario Principal puede registrar o editar perfiles del bebé (RF03 / RF28).
        </p>
      )}
    </section>
  );
}
