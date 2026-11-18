import { useState, type FormEvent } from "react";
import { useParams, useNavigate } from "react-router-dom";
import { UserPlus, AlertCircle, CheckCircle2 } from "lucide-react";
import { accountApi, ApiError, type AccountContextResponse } from "../lib/api";
import { EmailPasswordAuthForm } from "../components/auth/EmailPasswordAuthForm";

interface AcceptInvitationPageProps {
  accessToken: string | null;
  supabaseConfigured?: boolean;
  isAuthenticatingEmail?: boolean;
  onAccepted: (context: AccountContextResponse) => void;
  onEmailLogin: (email: string, password: string) => Promise<void>;
  onEmailSignUp: (
    email: string,
    password: string
  ) => Promise<{ needsEmailConfirmation: boolean; message: string }>;
}

export function AcceptInvitationPage({
  accessToken,
  supabaseConfigured = false,
  isAuthenticatingEmail = false,
  onAccepted,
  onEmailLogin,
  onEmailSignUp,
}: AcceptInvitationPageProps) {
  const { token } = useParams<{ token: string }>();
  const navigate = useNavigate();
  const [nombrePerfil, setNombrePerfil] = useState("");
  const [avatar, setAvatar] = useState("C1");
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [accepted, setAccepted] = useState(false);

  async function handleAccept(e: FormEvent) {
    e.preventDefault();
    if (!accessToken || !token) {
      return;
    }

    setLoading(true);
    setError(null);
    try {
      const context = await accountApi.acceptInvitation(accessToken, {
        token,
        nombrePerfil: nombrePerfil.trim() || undefined,
        avatar,
      });
      setAccepted(true);
      onAccepted(context);
      setTimeout(() => navigate("/"), 1000);
    } catch (err) {
      if (err instanceof ApiError) {
        setError(err.message);
      } else {
        setError("No se pudo procesar la invitación.");
      }
    } finally {
      setLoading(false);
    }
  }

  return (
    <main className="min-h-screen flex items-center justify-center p-6 nursery-backdrop text-slate-900">
      <section className="max-w-md w-full rounded-2xl border border-slate-200 bg-white p-8 shadow-sm space-y-5">
        <div className="flex items-center gap-3">
          <div className="flex h-10 w-10 items-center justify-center rounded-full bg-teal-50 text-teal-600">
            <UserPlus className="h-5 w-5" aria-hidden="true" />
          </div>
          <div>
            <h1 className="text-xl font-semibold text-slate-900">
              Invitación Familiar (RF02)
            </h1>
            <p className="text-xs text-slate-500">
              Vinculación como Usuario Secundario
            </p>
          </div>
        </div>

        {error && (
          <div
            role="alert"
            className="flex items-start gap-2 rounded-lg border border-red-200 bg-red-50 p-3 text-xs text-red-700"
          >
            <AlertCircle className="h-4 w-4 shrink-0 mt-0.5" aria-hidden="true" />
            <span>{error}</span>
          </div>
        )}

        {accepted ? (
          <div className="flex items-center gap-2 rounded-lg border border-emerald-200 bg-emerald-50 p-4 text-sm text-emerald-800">
            <CheckCircle2 className="h-5 w-5 shrink-0" aria-hidden="true" />
            <span>Cuenta vinculada correctamente. Redirigiendo al panel...</span>
          </div>
        ) : !accessToken ? (
          <div className="space-y-3">
            <p className="text-sm text-slate-600">
              Inicia sesión o crea tu cuenta con correo en Supabase para aceptar la invitación familiar.
            </p>
            <EmailPasswordAuthForm
              supabaseConfigured={supabaseConfigured}
              isSubmitting={isAuthenticatingEmail}
              onEmailLogin={onEmailLogin}
              onEmailSignUp={onEmailSignUp}
            />
          </div>
        ) : (
          <form onSubmit={handleAccept} className="space-y-4">
            <div>
              <label
                htmlFor="invite-profile-name"
                className="block text-xs font-medium text-slate-700 mb-1"
              >
                Nombre de tu perfil de cuidador (opcional, 1–30 caracteres)
              </label>
              <input
                id="invite-profile-name"
                type="text"
                maxLength={30}
                value={nombrePerfil}
                onChange={(e) => setNombrePerfil(e.target.value)}
                placeholder="Ej. Abuela Rosa"
                className="w-full rounded-lg border border-slate-300 px-3 py-2 text-sm focus:border-teal-500 focus:outline-none focus:ring-1 focus:ring-teal-500"
              />
            </div>

            <div>
              <label
                htmlFor="invite-avatar"
                className="block text-xs font-medium text-slate-700 mb-1"
              >
                Iniciales / Avatar
              </label>
              <select
                id="invite-avatar"
                value={avatar}
                onChange={(e) => setAvatar(e.target.value)}
                className="w-full rounded-lg border border-slate-300 px-3 py-2 text-sm focus:border-teal-500 focus:outline-none focus:ring-1 focus:ring-teal-500"
              >
                <option value="C1">C1 — Cuidador</option>
                <option value="F1">F1 — Familiar 1</option>
                <option value="F2">F2 — Familiar 2</option>
                <option value="M1">M1 — Miembro 1</option>
              </select>
            </div>

            <button
              type="submit"
              disabled={loading}
              className="w-full rounded-xl bg-teal-600 px-4 py-2.5 text-sm font-medium text-white hover:bg-teal-700 disabled:opacity-50"
            >
              {loading ? "Vinculando cuenta..." : "Aceptar invitación y unirme"}
            </button>
          </form>
        )}
      </section>
    </main>
  );
}
