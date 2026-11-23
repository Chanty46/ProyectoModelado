import { useCallback, useEffect, useState, type FormEvent } from "react";
import { Check, Copy, Link2, Plus, Trash2, Users } from "lucide-react";
import {
  accountApi,
  ApiError,
  type AccountContextResponse,
  type CreateInvitationResponse,
  type SecurityAuditEntry,
} from "../../lib/api";
import { SecurityAuditCard } from "./SecurityAuditCard";
import { RevokeMemberDialog } from "./RevokeMemberDialog";

interface FamilyAdminPanelProps {
  accessToken: string;
  account: AccountContextResponse;
  isPrincipal: boolean;
  onRefreshAccount: (token: string) => Promise<void>;
  onError: (msg: string | null) => void;
}

/**
 * Pestaña de administración familiar: gestión de perfiles de cuidador (RF23),
 * invitaciones de un solo uso y revocación de miembros (RF02 / RF22), y auditoría (RNF03).
 */
export function FamilyAdminPanel({
  accessToken,
  account,
  isPrincipal,
  onRefreshAccount,
  onError,
}: FamilyAdminPanelProps) {
  const [newProfileName, setNewProfileName] = useState("");
  const [newProfileAvatar, setNewProfileAvatar] = useState("F1");
  const [invitationResult, setInvitationResult] = useState<CreateInvitationResponse | null>(null);
  const [copiedInvite, setCopiedInvite] = useState(false);

  const [confirmRevokeMember, setConfirmRevokeMember] = useState<{
    id: string;
    email: string;
  } | null>(null);
  const [isRevokingMember, setIsRevokingMember] = useState(false);

  const [auditEntries, setAuditEntries] = useState<SecurityAuditEntry[]>([]);
  const [loadingAudit, setLoadingAudit] = useState<boolean>(false);

  const loadSecurityAudit = useCallback(async () => {
    if (!isPrincipal) {
      return;
    }
    setLoadingAudit(true);
    try {
      const logs = await accountApi.listSecurityAudit(accessToken);
      setAuditEntries(logs.slice(0, 15));
    } catch (err) {
      if (err instanceof ApiError) {
        onError(err.message);
      }
    } finally {
      setLoadingAudit(false);
    }
  }, [accessToken, isPrincipal, onError]);

  useEffect(() => {
    if (isPrincipal) {
      void loadSecurityAudit();
    }
  }, [isPrincipal, loadSecurityAudit]);

  async function handleCreateProfile(e: FormEvent) {
    e.preventDefault();
    if (!newProfileName.trim()) {
      return;
    }
    onError(null);
    try {
      await accountApi.createProfile(accessToken, {
        nombre: newProfileName.trim(),
        avatar: newProfileAvatar,
      });
      setNewProfileName("");
      await onRefreshAccount(accessToken);
    } catch (err) {
      if (err instanceof ApiError) {
        onError(err.message);
      }
    }
  }

  async function handleDeleteProfile(profileId: string) {
    onError(null);
    try {
      await accountApi.softDeleteProfile(accessToken, profileId);
      await onRefreshAccount(accessToken);
    } catch (err) {
      if (err instanceof ApiError) {
        onError(err.message);
      }
    }
  }

  async function handleCreateInvitation() {
    onError(null);
    try {
      const invite = await accountApi.createInvitation(accessToken);
      setInvitationResult(invite);
      setCopiedInvite(false);
    } catch (err) {
      if (err instanceof ApiError) {
        onError(err.message);
      }
    }
  }

  async function handleCopyInvite() {
    if (!invitationResult) {
      return;
    }
    try {
      await navigator.clipboard.writeText(invitationResult.inviteUrl);
      setCopiedInvite(true);
    } catch {
      onError("No se pudo copiar el enlace de invitación automáticamente.");
    }
  }

  async function confirmRevokeMemberAction() {
    if (!confirmRevokeMember) {
      return;
    }
    setIsRevokingMember(true);
    onError(null);
    try {
      await accountApi.revokeMember(accessToken, confirmRevokeMember.id);
      setConfirmRevokeMember(null);
      await onRefreshAccount(accessToken);
    } catch (err) {
      if (err instanceof ApiError) {
        onError(err.message);
      }
    } finally {
      setIsRevokingMember(false);
    }
  }

  return (
    <div className="space-y-6">
      {!isPrincipal && (
        <div className="rounded-xl border border-slate-200 bg-slate-100/80 px-4 py-3 text-xs text-slate-700">
          Tu rol actual es <strong>Usuario Secundario</strong>. Puedes consultar los perfiles y miembros de la familia; únicamente el Usuario Principal puede crear o dar de baja perfiles, emitir invitaciones o revocar accesos (RF03).
        </div>
      )}

      <div className="grid grid-cols-1 gap-6 md:grid-cols-2">
        {/* RF23: Gestión de Perfiles de Cuidador */}
        <section className="rounded-2xl border border-slate-200 bg-white p-6 shadow-sm space-y-4">
          <div className="flex items-center justify-between">
            <div className="flex items-center gap-2">
              <Users className="h-5 w-5 text-teal-600" aria-hidden="true" />
              <h2 className="text-base font-semibold text-slate-900">
                Perfiles de Cuidador (RF23)
              </h2>
            </div>
            <span className="rounded-full bg-slate-100 px-2.5 py-0.5 text-xs font-medium text-slate-600">
              {account.perfiles.length} / 6 activos
            </span>
          </div>

          <ul className="divide-y divide-slate-100">
            {account.perfiles.map((profile) => (
              <li key={profile.id} className="flex items-center justify-between py-2.5">
                <div className="flex items-center gap-2.5">
                  <span className="inline-flex h-7 w-7 items-center justify-center rounded-full bg-teal-50 text-xs font-bold text-teal-700">
                    {profile.avatar}
                  </span>
                  <span className="text-sm font-medium text-slate-800">
                    {profile.nombre}
                  </span>
                </div>
                {isPrincipal && (
                  <button
                    type="button"
                    onClick={() => void handleDeleteProfile(profile.id)}
                    title="Dar de baja lógica al perfil (RF23)"
                    className="inline-flex items-center gap-1 rounded-md px-2 py-1 text-xs text-red-600 hover:bg-red-50"
                  >
                    <Trash2 className="h-3.5 w-3.5" aria-hidden="true" />
                    Baja
                  </button>
                )}
              </li>
            ))}
          </ul>

          {isPrincipal && (
            <form onSubmit={(e) => void handleCreateProfile(e)} className="pt-2 space-y-3 border-t border-slate-100">
              <div className="flex gap-2">
                <input
                  type="text"
                  maxLength={30}
                  placeholder="Nuevo perfil (1–30 caracteres)"
                  value={newProfileName}
                  onChange={(e) => setNewProfileName(e.target.value)}
                  className="flex-1 rounded-lg border border-slate-300 px-3 py-1.5 text-xs"
                />
                <select
                  value={newProfileAvatar}
                  onChange={(e) => setNewProfileAvatar(e.target.value)}
                  aria-label="Avatar del perfil"
                  className="rounded-lg border border-slate-300 px-2 py-1.5 text-xs"
                >
                  <option value="F1">F1</option>
                  <option value="M1">M1</option>
                  <option value="C1">C1</option>
                  <option value="A1">A1</option>
                </select>
                <button
                  type="submit"
                  disabled={account.perfiles.length >= 6}
                  className="inline-flex items-center gap-1 rounded-lg bg-teal-600 px-3 py-1.5 text-xs font-medium text-white hover:bg-teal-700 disabled:opacity-50"
                >
                  <Plus className="h-3.5 w-3.5" aria-hidden="true" />
                  Agregar
                </button>
              </div>
            </form>
          )}
        </section>

        {/* RF02 y RF22: Invitaciones y Miembros de la Familia */}
        <section className="rounded-2xl border border-slate-200 bg-white p-6 shadow-sm space-y-4">
          <div className="flex items-center justify-between">
            <div className="flex items-center gap-2">
              <Link2 className="h-5 w-5 text-teal-600" aria-hidden="true" />
              <h2 className="text-base font-semibold text-slate-900">
                Miembros e Invitaciones (RF02 / RF22)
              </h2>
            </div>
          </div>

          {isPrincipal && (
            <div className="space-y-2">
              <button
                type="button"
                onClick={() => void handleCreateInvitation()}
                className="w-full rounded-xl border border-teal-600 bg-teal-50 px-4 py-2 text-xs font-medium text-teal-800 hover:bg-teal-100"
              >
                Generar enlace de invitación de 1 uso (72 h)
              </button>

              {invitationResult && (
                <div className="rounded-xl border border-slate-200 bg-slate-50 p-3 space-y-2">
                  <p className="text-[11px] font-medium text-slate-700">
                    Comparte este enlace manualmente (se muestra una sola vez, vence en 72 h):
                  </p>
                  <div className="flex items-center gap-2">
                    <input
                      type="text"
                      readOnly
                      value={invitationResult.inviteUrl}
                      className="flex-1 rounded border border-slate-300 bg-white px-2 py-1 text-xs font-mono text-slate-800"
                    />
                    <button
                      type="button"
                      onClick={() => void handleCopyInvite()}
                      className="inline-flex items-center gap-1 rounded bg-slate-800 px-2.5 py-1 text-xs text-white hover:bg-slate-700"
                    >
                      {copiedInvite ? (
                        <>
                          <Check className="h-3.5 w-3.5" aria-hidden="true" />
                          Copiado
                        </>
                      ) : (
                        <>
                          <Copy className="h-3.5 w-3.5" aria-hidden="true" />
                          Copiar
                        </>
                      )}
                    </button>
                  </div>
                </div>
              )}
            </div>
          )}

          <div className="pt-2 border-t border-slate-100">
            <h3 className="text-xs font-semibold uppercase tracking-wider text-slate-500 mb-2">
              Usuarios vinculados a la familia
            </h3>
            <ul className="divide-y divide-slate-100">
              {account.miembros.map((member) => (
                <li key={member.id} className="flex items-center justify-between py-2.5">
                  <div>
                    <p className="text-xs font-medium text-slate-800">{member.email}</p>
                    <p className="text-[11px] text-slate-500">
                      {member.rol === "principal" ? "Usuario Principal" : "Usuario Secundario"}
                    </p>
                  </div>
                  {isPrincipal && member.rol === "secundario" && (
                    <button
                      type="button"
                      onClick={() => setConfirmRevokeMember({ id: member.id, email: member.email })}
                      className="rounded-md px-2.5 py-1 text-xs font-medium text-red-600 hover:bg-red-50"
                    >
                      Revocar acceso (RF22)
                    </button>
                  )}
                </li>
              ))}
            </ul>
          </div>
        </section>
      </div>

      {isPrincipal && (
        <SecurityAuditCard
          entries={auditEntries}
          loading={loadingAudit}
          onRefresh={() => void loadSecurityAudit()}
        />
      )}

      <RevokeMemberDialog
        member={confirmRevokeMember}
        isRevoking={isRevokingMember}
        onCancel={() => setConfirmRevokeMember(null)}
        onConfirm={() => void confirmRevokeMemberAction()}
      />
    </div>
  );
}
