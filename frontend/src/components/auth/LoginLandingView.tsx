import { Activity, MessageSquare, Milk, ShieldAlert, ShieldCheck, Sparkles } from "lucide-react";

interface LoginLandingViewProps {
  onGoogleLogin: () => void;
}

const HIGHLIGHTS = [
  {
    title: "Detección < 1 s",
    subtitle: "Retiro imprevisto y Modo Atención (15 min)",
    Icon: ShieldAlert,
  },
  {
    title: "Biberón 35–38.5 °C",
    subtitle: "Sensor IR sin contacto y cálculo ml",
    Icon: Milk,
  },
  {
    title: "Estímulos Seguros",
    subtitle: "Móvil 9 LEDs, melodías y luz ≤ 65 dBA",
    Icon: Sparkles,
  },
  {
    title: "Telegram /estado",
    subtitle: "Alertas redundantes y recordatorios",
    Icon: MessageSquare,
  },
] as const;

/**
 * Pantalla de bienvenida e inicio de sesión familiar vía Google OAuth / Supabase Auth (RF01).
 */
export function LoginLandingView({ onGoogleLogin }: LoginLandingViewProps) {
  return (
    <section className="mx-auto max-w-4xl grid grid-cols-1 gap-6 md:grid-cols-5 items-stretch pt-1 sm:pt-4">
      <div className="order-2 md:order-1 md:col-span-3 rounded-2xl border border-teal-200/70 bg-gradient-to-br from-teal-700 via-teal-800 to-slate-900 p-6 sm:p-7 text-white shadow-lg flex flex-col justify-between space-y-5 sm:space-y-6">
        <div className="space-y-3">
          <span className="inline-flex items-center gap-1.5 rounded-full bg-white/15 px-3 py-1 text-[11px] font-semibold tracking-wide text-teal-100">
            <Activity className="h-3.5 w-3.5 shrink-0 text-emerald-300" aria-hidden="true" />
            <span>Tiempo Real · 5 Nodos Pico W</span>
          </span>
          <h2 className="text-2xl sm:text-3xl font-bold leading-tight tracking-tight">
            Cuidado inteligente y tranquilo en todo momento
          </h2>
          <p className="text-xs sm:text-sm leading-relaxed text-teal-100/90">
            Supervisa la presencia en colchón, microclima ambiental, nutrición térmica por biberón y alertas instantáneas en web y Telegram desde una sola consola familiar.
          </p>
        </div>

        <div className="grid grid-cols-1 sm:grid-cols-2 gap-3 text-xs">
          {HIGHLIGHTS.map(({ title, subtitle, Icon }) => (
            <div
              key={title}
              className="rounded-xl border border-white/10 bg-white/10 p-3 backdrop-blur-sm space-y-1.5"
            >
              <div className="flex items-center gap-2">
                <span className="inline-flex h-6 w-6 shrink-0 items-center justify-center rounded-lg bg-white/15 text-teal-100">
                  <Icon className="h-3.5 w-3.5" aria-hidden="true" />
                </span>
                <p className="font-semibold text-white">{title}</p>
              </div>
              <p className="text-[11px] leading-snug text-teal-100/85">{subtitle}</p>
            </div>
          ))}
        </div>
      </div>

      <div className="order-1 md:order-2 md:col-span-2 rounded-2xl border border-slate-200 bg-white p-6 sm:p-7 shadow-md flex flex-col justify-center text-center space-y-5">
        <div className="mx-auto flex h-14 w-14 items-center justify-center rounded-2xl bg-gradient-to-br from-teal-50 to-emerald-100 text-teal-700 ring-1 ring-teal-200/70">
          <ShieldCheck className="h-7 w-7" aria-hidden="true" />
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
          className="w-full rounded-xl bg-teal-600 px-4 py-2.5 text-sm font-semibold text-white shadow-sm hover:bg-teal-700 active:scale-[0.99] focus:outline-none focus:ring-2 focus:ring-teal-500 transition-all"
        >
          Continuar con Google
        </button>
        <div className="rounded-xl border border-slate-100 bg-slate-50 px-3 py-2 text-[11px] text-slate-500">
          Autenticación cifrada mediante Supabase Auth y aislamiento por familia (RLS).
        </div>
      </div>
    </section>
  );
}
