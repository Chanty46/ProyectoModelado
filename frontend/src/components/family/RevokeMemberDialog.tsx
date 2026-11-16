import { AlertTriangle } from "lucide-react";

interface RevokeMemberDialogProps {
  member: { id: string; email: string } | null;
  isRevoking: boolean;
  onCancel: () => void;
  onConfirm: () => void;
}

/**
 * Diálogo modal de confirmación para revocar el acceso de un miembro secundario (RF22).
 */
export function RevokeMemberDialog({
  member,
  isRevoking,
  onCancel,
  onConfirm,
}: RevokeMemberDialogProps) {
  if (!member) {
    return null;
  }

  return (
    <div
      role="dialog"
      aria-modal="true"
      aria-labelledby="revoke-dialog-title"
      className="fixed inset-0 z-50 flex items-center justify-center bg-slate-900/50 p-4 backdrop-blur-sm"
    >
      <div className="w-full max-w-md rounded-2xl border border-slate-200 bg-white p-6 shadow-xl space-y-4">
        <div className="flex items-start gap-3">
          <div className="flex h-10 w-10 shrink-0 items-center justify-center rounded-full bg-red-100 text-red-600">
            <AlertTriangle className="h-5 w-5" aria-hidden="true" />
          </div>
          <div>
            <h3 id="revoke-dialog-title" className="text-sm font-semibold text-slate-900">
              Confirmar revocación de acceso (RF22)
            </h3>
            <p className="mt-1 text-xs text-slate-600">
              ¿Confirmas revocar el acceso de{" "}
              <strong className="font-mono text-slate-800">{member.email}</strong> a esta familia? Sus credenciales perderán acceso inmediato a cunas y telemetría.
            </p>
          </div>
        </div>
        <div className="flex justify-end gap-2 pt-2">
          <button
            type="button"
            disabled={isRevoking}
            onClick={onCancel}
            className="rounded-lg border border-slate-300 bg-white px-3 py-1.5 text-xs font-medium text-slate-700 hover:bg-slate-50"
          >
            Cancelar
          </button>
          <button
            type="button"
            disabled={isRevoking}
            onClick={onConfirm}
            className="rounded-lg bg-red-600 px-3.5 py-1.5 text-xs font-semibold text-white hover:bg-red-700 disabled:opacity-50"
          >
            {isRevoking ? "Revocando..." : "Sí, revocar acceso"}
          </button>
        </div>
      </div>
    </div>
  );
}
