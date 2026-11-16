import { Activity, ShieldCheck } from "lucide-react";

interface LoginLandingViewProps {
  onGoogleLogin: () => void;
}

/**
 * Pantalla de bienvenida e inicio de sesión familiar vía Google OAuth / Supabase Auth (RF01).
 */
export function LoginLandingView({ onGoogleLogin }: LoginLandingViewProps) {
  return (
    <section className="mx-auto max-w-4xl grid grid-cols-1 gap-6 md:grid-cols-5 items-stretch">
      <div className="md:col-span-3 rounded-2xl border border-teal-200/70 bg-gradient-to-br from-teal-700 via-teal-800 to-slate-900 p-7 text-white shadow-md flex flex-col justify-between space-y-6">
        <div className="space-y-3">
          <span className="inline-flex items-center gap-1.5 rounded-full bg-white/15 px-3 py-1 text-[11px] font-semibold tracking-wide text-teal-100">
            <Activity className="h-3.5 w-3.5" aria-hidden="true" />
            Ecosistema Ciberfísico · 5 Nodos IoT
          </span>
          <h2 className="text-2xl font-bold leading-snug">
            Cuidado inteligente y tranquilo en todo momento
          </h2>
          <p className="text-xs leading-relaxed text-teal-100/90">
            Supervisa la presencia en colchón, microclima ambiental, nutrición térmica por biberón y alertas instantáneas en web y Telegram desde una sola consola familiar.
          </p>
        </div>
        <div className="grid grid-cols-2 gap-2.5 text-xs">
          <div className="rounded-xl bg-white/10 p-3 backdrop-blur-sm">
            <p className="font-semibold text-white">Detección &lt; 1 s</p>
            <p className="text-[11px] text-teal-100/80 mt-0.5">
              Retiro imprevisto y Modo Atención (15 min)
            </p>
          </div>
          <div className="rounded-xl bg-white/10 p-3 backdrop-blur-sm">
            <p className="font-semibold text-white">Biberón 35–38.5 °C</p>
            <p className="text-[11px] text-teal-100/80 mt-0.5">
              Sensor IR sin contacto y cálculo ml
            </p>
          </div>
          <div className="rounded-xl bg-white/10 p-3 backdrop-blur-sm">
            <p className="font-semibold text-white">Estímulos Seguros</p>
            <p className="text-[11px] text-teal-100/80 mt-0.5">
              Móvil 9 LEDs, melodías y luz ≤ 65 dBA
            </p>
          </div>
          <div className="rounded-xl bg-white/10 p-3 backdrop-blur-sm">
            <p className="font-semibold text-white">Telegram /estado</p>
            <p className="text-[11px] text-teal-100/80 mt-0.5">
              Alertas redundantes y recordatorios
            </p>
          </div>
        </div>
      </div>

      <div className="md:col-span-2 rounded-2xl border border-slate-200 bg-white p-7 shadow-sm flex flex-col justify-center text-center space-y-5">
        <div className="mx-auto flex h-12 w-12 items-center justify-center rounded-2xl bg-teal-50 text-teal-600">
          <ShieldCheck className="h-6 w-6" aria-hidden="true" />
        </div>
        <div className="space-y-2">
          <h2 className="text-xl font-semibold text-slate-900">
            Iniciar Sesión Familiar (RF01)
          </h2>
          <p className="text-xs leading-relaxed text-slate-600">
            Accede con tu cuenta de Google para gestionar tu cuna inteligente o aceptar una invitación familiar.
          </p>
        </div>
        <button
          type="button"
          onClick={onGoogleLogin}
          className="w-full rounded-xl bg-teal-600 px-4 py-2.5 text-sm font-semibold text-white shadow-sm hover:bg-teal-700 focus:outline-none focus:ring-2 focus:ring-teal-500 transition-colors"
        >
          Continuar con Google
        </button>
        <p className="text-[11px] text-slate-500">
          Autenticación cifrada mediante Supabase Auth &amp; RLS por familia.
        </p>
      </div>
    </section>
  );
}
