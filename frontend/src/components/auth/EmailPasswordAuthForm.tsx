import { useState, type FormEvent } from "react";
import { Mail } from "lucide-react";

interface EmailPasswordAuthFormProps {
  supabaseConfigured: boolean;
  isSubmitting?: boolean;
  onEmailLogin: (email: string, password: string) => Promise<void>;
  onEmailSignUp: (
    email: string,
    password: string
  ) => Promise<{ needsEmailConfirmation: boolean; message: string }>;
  compact?: boolean;
}

/** Formulario reutilizable de inicio de sesión o registro por correo en Supabase Auth (RF01). */
export function EmailPasswordAuthForm({
  supabaseConfigured,
  isSubmitting = false,
  onEmailLogin,
  onEmailSignUp,
  compact = false,
}: EmailPasswordAuthFormProps) {
  const [mode, setMode] = useState<"login" | "signup">("login");
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [statusMessage, setStatusMessage] = useState<string | null>(null);

  async function handleSubmit(e: FormEvent) {
    e.preventDefault();
    setStatusMessage(null);
    if (mode === "login") {
      await onEmailLogin(email, password);
      return;
    }

    const res = await onEmailSignUp(email, password);
    if (res.needsEmailConfirmation) {
      setStatusMessage(res.message);
    }
  }

  return (
    <form
      onSubmit={(e) => void handleSubmit(e)}
      className={`space-y-3 rounded-xl border border-slate-200 bg-slate-50/70 text-left ${
        compact ? "p-3.5" : "p-4"
      }`}
    >
      <div className="flex items-center justify-between gap-2">
        <span className="inline-flex items-center gap-1.5 text-xs font-semibold text-slate-800">
          <Mail className="h-3.5 w-3.5 text-teal-600" aria-hidden="true" />
          Cuenta Supabase (Correo)
        </span>
        <div className="inline-flex rounded-lg border border-slate-200 bg-white p-0.5 text-[10px] font-semibold">
          <button
            type="button"
            onClick={() => {
              setMode("login");
              setStatusMessage(null);
            }}
            className={`rounded-md px-2 py-0.5 transition-colors ${
              mode === "login"
                ? "bg-teal-600 text-white"
                : "text-slate-600 hover:bg-slate-100"
            }`}
          >
            Entrar
          </button>
          <button
            type="button"
            onClick={() => {
              setMode("signup");
              setStatusMessage(null);
            }}
            className={`rounded-md px-2 py-0.5 transition-colors ${
              mode === "signup"
                ? "bg-teal-600 text-white"
                : "text-slate-600 hover:bg-slate-100"
            }`}
          >
            Crear cuenta
          </button>
        </div>
      </div>

      {!supabaseConfigured && (
        <p className="rounded-lg border border-amber-200 bg-amber-50 px-2.5 py-1.5 text-[11px] leading-snug text-amber-800">
          Para usar cuentas por correo en la nube, configura{" "}
          <code>VITE_SUPABASE_PUBLISHABLE_KEY</code> en <code>frontend/.env</code>.
        </p>
      )}

      {statusMessage && (
        <p
          role="status"
          className="rounded-lg border border-emerald-200 bg-emerald-50 px-2.5 py-1.5 text-[11px] leading-snug text-emerald-800"
        >
          {statusMessage}
        </p>
      )}

      <div>
        <label
          htmlFor="supabase-auth-email"
          className="block text-[11px] font-medium text-slate-700 mb-1"
        >
          Correo electrónico
        </label>
        <input
          id="supabase-auth-email"
          type="email"
          required
          value={email}
          onChange={(e) => setEmail(e.target.value)}
          placeholder="familia@ejemplo.com"
          className="w-full rounded-lg border border-slate-300 bg-white px-3 py-1.5 text-xs text-slate-900 focus:border-teal-500 focus:outline-none focus:ring-1 focus:ring-teal-500"
        />
      </div>

      <div>
        <label
          htmlFor="supabase-auth-password"
          className="block text-[11px] font-medium text-slate-700 mb-1"
        >
          Clave de cuenta
        </label>
        <input
          id="supabase-auth-password"
          type="password"
          required
          minLength={6}
          value={password}
          onChange={(e) => setPassword(e.target.value)}
          placeholder="Mínimo 6 caracteres"
          className="w-full rounded-lg border border-slate-300 bg-white px-3 py-1.5 text-xs text-slate-900 focus:border-teal-500 focus:outline-none focus:ring-1 focus:ring-teal-500"
        />
      </div>

      <button
        type="submit"
        disabled={isSubmitting || !supabaseConfigured}
        className="w-full rounded-xl border border-teal-600 bg-white px-4 py-2 text-xs font-semibold text-teal-700 hover:bg-teal-50 disabled:cursor-not-allowed disabled:opacity-50 transition-all"
      >
        {isSubmitting
          ? "Procesando..."
          : mode === "login"
            ? "Iniciar sesión con correo"
            : "Crear cuenta en Supabase"}
      </button>
    </form>
  );
}
