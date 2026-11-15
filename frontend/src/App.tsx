import { useEffect, useState, useCallback, type FormEvent } from "react";
import { Routes, Route } from "react-router-dom";
import {
  ShieldCheck,
  Shield,
  LogOut,
  Users,
  Link2,
  Trash2,
  Plus,
  AlertTriangle,
  Copy,
  Check,
  X,
  Activity,
  MessageSquare,
} from "lucide-react";
import { supabase } from "./lib/supabase";
import {
  accountApi,
  ApiError,
  type AccountContextResponse,
  type CreateInvitationResponse,
  type HealthStatusResponse,
  type SecurityAuditEntry,
} from "./lib/api";
import { canManagePrincipalActions } from "./lib/formatters";
import { ProfileSelectorBar } from "./components/ProfileSelectorBar";
import { CribAndDeviceManager } from "./components/CribAndDeviceManager";
import { TelegramAndRemindersPanel } from "./components/TelegramAndRemindersPanel";
import { AcceptInvitationPage } from "./pages/AcceptInvitationPage";

type DashboardTab = "monitoreo" | "comunicacion" | "familia";

export default function App() {
  const [accessToken, setAccessToken] = useState<string | null>(null);
  const [account, setAccount] = useState<AccountContextResponse | null>(null);
  const [activeProfileId, setActiveProfileId] = useState<string | null>(null);
  const [loading, setLoading] = useState<boolean>(true);
  const [errorBanner, setErrorBanner] = useState<string | null>(null);
  const [activeTab, setActiveTab] = useState<DashboardTab>("monitoreo");
  const [health, setHealth] = useState<HealthStatusResponse | null>(null);
  const [healthOffline, setHealthOffline] = useState<boolean>(false);
  const [auditEntries, setAuditEntries] = useState<SecurityAuditEntry[]>([]);
  const [loadingAudit, setLoadingAudit] = useState<boolean>(false);

  // Formularios
  const [familyNameInput, setFamilyNameInput] = useState("Mi Familia");
  const [initialProfileInput, setInitialProfileInput] = useState("Mamá / Papá");
  const [newProfileName, setNewProfileName] = useState("");
  const [newProfileAvatar, setNewProfileAvatar] = useState("F1");
  const [invitationResult, setInvitationResult] = useState<CreateInvitationResponse | null>(null);
  const [copiedInvite, setCopiedInvite] = useState(false);
  const [confirmRevokeMember, setConfirmRevokeMember] = useState<{ id: string; email: string } | null>(null);
  const [isRevokingMember, setIsRevokingMember] = useState(false);

  const clearLocalSessionState = useCallback(() => {
    setAccessToken(null);
    setAccount(null);
    setActiveProfileId(null);
    setInvitationResult(null);
    setErrorBanner(null);
    try {
      localStorage.removeItem("cuna_active_profile_id");
    } catch {
      // Ignorar excepciones de almacenamiento local
    }
  }, []);

  const handleSelectProfile = useCallback((profileId: string) => {
    setActiveProfileId(profileId);
    try {
      localStorage.setItem("cuna_active_profile_id", profileId);
    } catch {
      // Ignorar excepciones de almacenamiento local
    }
  }, []);

  const refreshAccountContext = useCallback(async (token: string) => {
    try {
      const ctx = await accountApi.getMe(token);
      setAccount(ctx);
      if (ctx.perfiles.length > 0) {
        let savedProfileId: string | null = null;
        try {
          savedProfileId = localStorage.getItem("cuna_active_profile_id");
        } catch {
          // Ignorar excepciones de almacenamiento local
        }

        setActiveProfileId((prev) => {
          if (prev && ctx.perfiles.some((p) => p.id === prev)) {
            return prev;
          }
          if (savedProfileId && ctx.perfiles.some((p) => p.id === savedProfileId)) {
            return savedProfileId;
          }
          return ctx.perfiles[0].id;
        });
      } else {
        setActiveProfileId(null);
      }
    } catch (err) {
      if (err instanceof ApiError) {
        setErrorBanner(err.message);
        if (err.status === 401) {
          clearLocalSessionState();
          if (supabase) {
            void supabase.auth.signOut();
          }
        }
      }
    }
  }, [clearLocalSessionState]);

  useEffect(() => {
    if (!supabase) {
      setLoading(false);
      return;
    }

    supabase.auth.getSession().then(({ data }) => {
      const token = data.session?.access_token ?? null;
      setAccessToken(token);
      if (token) {
        refreshAccountContext(token).finally(() => setLoading(false));
      } else {
        setLoading(false);
      }
    });

    const { data: authListener } = supabase.auth.onAuthStateChange((_event, session) => {
      const token = session?.access_token ?? null;
      if (!token) {
        clearLocalSessionState();
      } else {
        setAccessToken(token);
        void refreshAccountContext(token);
      }
    });

    return () => {
      authListener.subscription.unsubscribe();
    };
  }, [clearLocalSessionState, refreshAccountContext]);

  // Suscripción Realtime a public.usuarios y public.perfiles_cuidador (RF22 < 5s / RF23 < 2s)
  const familyId = account?.familia?.id ?? null;
  useEffect(() => {
    const sbClient = supabase;
    if (!sbClient || !accessToken || !familyId) {
      return;
    }

    const channel = sbClient
      .channel(`family-sync-${familyId}`)
      .on(
        "postgres_changes",
        { event: "*", schema: "public", table: "usuarios" },
        () => {
          void refreshAccountContext(accessToken);
        }
      )
      .on(
        "postgres_changes",
        { event: "*", schema: "public", table: "perfiles_cuidador" },
        () => {
          void refreshAccountContext(accessToken);
        }
      )
      .subscribe();

    return () => {
      void sbClient.removeChannel(channel);
    };
  }, [accessToken, familyId, refreshAccountContext]);

  useEffect(() => {
    let active = true;
    let controller: AbortController | null = null;

    async function pollHealth() {
      controller?.abort();
      controller = new AbortController();
      try {
        const res = await accountApi.getHealthStatus(controller.signal);
        if (active) {
          setHealth(res);
          setHealthOffline(false);
        }
      } catch (err) {
        if (err instanceof DOMException && err.name === "AbortError") {
          return;
        }
        if (active) {
          setHealthOffline(true);
        }
      }
    }

    void pollHealth();
    const timer = window.setInterval(() => void pollHealth(), 15000);
    return () => {
      active = false;
      controller?.abort();
      window.clearInterval(timer);
    };
  }, []);

  const isPrincipal = canManagePrincipalActions(account?.rol ?? null);

  const loadSecurityAudit = useCallback(async () => {
    if (!accessToken || !isPrincipal) {
      return;
    }
    setLoadingAudit(true);
    try {
      const logs = await accountApi.listSecurityAudit(accessToken);
      setAuditEntries(logs.slice(0, 15));
    } catch (err) {
      if (err instanceof ApiError) {
        setErrorBanner(err.message);
      }
    } finally {
      setLoadingAudit(false);
    }
  }, [accessToken, isPrincipal]);

  useEffect(() => {
    if (activeTab === "familia" && isPrincipal && accessToken) {
      void loadSecurityAudit();
    }
  }, [activeTab, isPrincipal, accessToken, loadSecurityAudit]);

  async function handleGoogleLogin() {
    if (!supabase) {
      setErrorBanner("Configura VITE_SUPABASE_URL y VITE_SUPABASE_PUBLISHABLE_KEY en frontend/.env.");
      return;
    }

    setErrorBanner(null);
    const { error } = await supabase.auth.signInWithOAuth({
      provider: "google",
      options: {
        redirectTo: window.location.origin,
      },
    });

    if (error) {
      setErrorBanner(error.message);
    }
  }

  async function handleSignOut() {
    if (supabase) {
      await supabase.auth.signOut();
    }
    clearLocalSessionState();
  }

  async function handleBootstrapFamily(e: FormEvent) {
    e.preventDefault();
    if (!accessToken) {
      return;
    }
    setErrorBanner(null);
    try {
      const created = await accountApi.bootstrap(accessToken, {
        nombreFamilia: familyNameInput,
        nombrePerfilInicial: initialProfileInput,
        avatarInicial: "P1",
      });
      setAccount(created);
      if (created.perfiles.length > 0) {
        setActiveProfileId(created.perfiles[0].id);
      }
    } catch (err) {
      if (err instanceof ApiError) {
        setErrorBanner(err.message);
      }
    }
  }

  async function handleCreateProfile(e: FormEvent) {
    e.preventDefault();
    if (!accessToken || !newProfileName.trim()) {
      return;
    }
    setErrorBanner(null);
    try {
      await accountApi.createProfile(accessToken, {
        nombre: newProfileName.trim(),
        avatar: newProfileAvatar,
      });
      setNewProfileName("");
      await refreshAccountContext(accessToken);
    } catch (err) {
      if (err instanceof ApiError) {
        setErrorBanner(err.message);
      }
    }
  }

  async function handleDeleteProfile(profileId: string) {
    if (!accessToken) {
      return;
    }
    setErrorBanner(null);
    try {
      await accountApi.softDeleteProfile(accessToken, profileId);
      await refreshAccountContext(accessToken);
    } catch (err) {
      if (err instanceof ApiError) {
        setErrorBanner(err.message);
      }
    }
  }

  async function handleCreateInvitation() {
    if (!accessToken) {
      return;
    }
    setErrorBanner(null);
    try {
      const invite = await accountApi.createInvitation(accessToken);
      setInvitationResult(invite);
      setCopiedInvite(false);
    } catch (err) {
      if (err instanceof ApiError) {
        setErrorBanner(err.message);
      }
    }
  }

  function handleRevokeMember(memberUserId: string, email: string) {
    setConfirmRevokeMember({ id: memberUserId, email });
  }

  async function confirmRevokeMemberAction() {
    if (!accessToken || !confirmRevokeMember) {
      return;
    }
    setIsRevokingMember(true);
    setErrorBanner(null);
    try {
      await accountApi.revokeMember(accessToken, confirmRevokeMember.id);
      setConfirmRevokeMember(null);
      await refreshAccountContext(accessToken);
    } catch (err) {
      if (err instanceof ApiError) {
        setErrorBanner(err.message);
      }
    } finally {
      setIsRevokingMember(false);
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
      setErrorBanner("No se pudo copiar el enlace de invitación automáticamente.");
    }
  }

  const activeCaregiver = account?.perfiles.find((p) => p.id === activeProfileId) ?? null;

  const mainDashboard = (
    <div className="min-h-screen nursery-backdrop text-slate-900">
      <header className="sticky top-0 z-30 border-b border-slate-200/80 bg-white/90 backdrop-blur-md">
        <div className="mx-auto flex max-w-5xl flex-wrap items-center justify-between gap-4 px-6 py-3.5">
          <div className="flex items-center gap-3">
            <div className="flex h-10 w-10 items-center justify-center rounded-xl bg-gradient-to-br from-teal-500 to-emerald-600 text-white shadow-sm">
              <ShieldCheck className="h-5 w-5" aria-hidden="true" />
            </div>
            <div>
              <div className="flex items-center gap-2">
                <h1 className="text-base sm:text-lg font-bold tracking-tight text-slate-900">
                  Cuna Inteligente
                </h1>
                {activeCaregiver && (
                  <span className="inline-flex items-center gap-1 rounded-full bg-teal-50 border border-teal-200 px-2 py-0.5 text-[11px] font-medium text-teal-800">
                    <span className="h-1.5 w-1.5 rounded-full bg-teal-500" aria-hidden="true" />
                    <span className="sm:hidden">{activeCaregiver.nombre.split(" ")[0]}</span>
                    <span className="hidden sm:inline">Turno: {activeCaregiver.nombre}</span>
                  </span>
                )}
              </div>
              <p className="text-xs text-slate-500">
                {account?.familia
                  ? `${account.familia.nombre} · ${account.rol === "principal" ? "Usuario Principal" : "Usuario Secundario"}`
                  : "Supervisión Perimetral, Microclima y Nutrición Infantil"}
              </p>
            </div>
          </div>

          <div className="flex items-center gap-2.5">
            {health && !healthOffline ? (
              <span
                title={`TCP :${health.tcpPort} · Última verificación: ${new Date(health.timestamp).toLocaleTimeString()}`}
                className="inline-flex items-center gap-1.5 rounded-full border border-emerald-200 bg-emerald-50 px-2.5 py-1 text-[11px] font-medium text-emerald-700"
              >
                <span className="h-2 w-2 rounded-full bg-emerald-500 animate-pulse" />
                TCP :{health.tcpPort} · {health.connectedDevices} nodo(s)
              </span>
            ) : (
              <span className="inline-flex items-center gap-1.5 rounded-full border border-slate-200 bg-slate-100 px-2.5 py-1 text-[11px] font-medium text-slate-600">
                <span className="h-2 w-2 rounded-full bg-slate-400" />
                {healthOffline ? "API local sin conexión" : "Conectando API..."}
              </span>
            )}

            {accessToken && (
              <button
                type="button"
                onClick={handleSignOut}
                className="inline-flex items-center gap-1.5 rounded-lg border border-slate-300 bg-white px-3 py-1.5 text-xs font-medium text-slate-700 hover:bg-slate-100 transition-colors"
              >
                <LogOut className="h-3.5 w-3.5" aria-hidden="true" />
                Cerrar sesión
              </button>
            )}
          </div>
        </div>
      </header>

      <main className="mx-auto max-w-5xl p-6 space-y-6">
        {errorBanner && (
          <div
            role="alert"
            className="flex items-start gap-2.5 rounded-xl border border-red-200 bg-red-50 p-4 text-sm text-red-800 shadow-sm"
          >
            <AlertTriangle className="h-5 w-5 shrink-0 text-red-600 mt-0.5" aria-hidden="true" />
            <div className="flex-1">
              <p className="font-medium">Aviso de seguridad / autorización</p>
              <p className="text-xs mt-0.5">{errorBanner}</p>
            </div>
            <button
              type="button"
              onClick={() => setErrorBanner(null)}
              aria-label="Cerrar aviso"
              className="rounded-lg p-1 text-red-600 hover:bg-red-100 transition-colors"
            >
              <X className="h-4 w-4" aria-hidden="true" />
            </button>
          </div>
        )}

        {loading ? (
          <div className="rounded-2xl border border-slate-200 bg-white p-10 text-center text-sm text-slate-500 shadow-sm">
            Cargando sesión segura...
          </div>
        ) : !accessToken ? (
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
                  <p className="text-[11px] text-teal-100/80 mt-0.5">Retiro imprevisto y Modo Atención (15 min)</p>
                </div>
                <div className="rounded-xl bg-white/10 p-3 backdrop-blur-sm">
                  <p className="font-semibold text-white">Biberón 35–38.5 °C</p>
                  <p className="text-[11px] text-teal-100/80 mt-0.5">Sensor IR sin contacto y cálculo ml</p>
                </div>
                <div className="rounded-xl bg-white/10 p-3 backdrop-blur-sm">
                  <p className="font-semibold text-white">Estímulos Seguros</p>
                  <p className="text-[11px] text-teal-100/80 mt-0.5">Móvil 9 LEDs, melodías y luz ≤ 65 dBA</p>
                </div>
                <div className="rounded-xl bg-white/10 p-3 backdrop-blur-sm">
                  <p className="font-semibold text-white">Telegram /estado</p>
                  <p className="text-[11px] text-teal-100/80 mt-0.5">Alertas redundantes y recordatorios</p>
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
                onClick={handleGoogleLogin}
                className="w-full rounded-xl bg-teal-600 px-4 py-2.5 text-sm font-semibold text-white shadow-sm hover:bg-teal-700 focus:outline-none focus:ring-2 focus:ring-teal-500 transition-colors"
              >
                Continuar con Google
              </button>
              <p className="text-[11px] text-slate-500">
                Autenticación cifrada mediante Supabase Auth &amp; RLS por familia.
              </p>
            </div>
          </section>
        ) : !account?.familia ? (
          <section className="mx-auto max-w-lg rounded-2xl border border-slate-200 bg-white p-8 shadow-sm space-y-5">
            <h2 className="text-lg font-semibold text-slate-900">
              Crear Cuenta Familiar (RF01)
            </h2>
            <p className="text-xs text-slate-600">
              Aún no perteneces a ninguna familia activa. Crea una familia nueva como Usuario Principal o abre el enlace de invitación que te hayan compartido (RF02).
            </p>
            <form onSubmit={handleBootstrapFamily} className="space-y-4">
              <div>
                <label htmlFor="family-name" className="block text-xs font-medium text-slate-700 mb-1">
                  Nombre de la familia
                </label>
                <input
                  id="family-name"
                  type="text"
                  required
                  maxLength={80}
                  value={familyNameInput}
                  onChange={(e) => setFamilyNameInput(e.target.value)}
                  className="w-full rounded-lg border border-slate-300 px-3 py-2 text-sm"
                />
              </div>
              <div>
                <label htmlFor="initial-profile" className="block text-xs font-medium text-slate-700 mb-1">
                  Tu perfil de cuidador inicial (1–30 caracteres, RF23)
                </label>
                <input
                  id="initial-profile"
                  type="text"
                  required
                  maxLength={30}
                  value={initialProfileInput}
                  onChange={(e) => setInitialProfileInput(e.target.value)}
                  className="w-full rounded-lg border border-slate-300 px-3 py-2 text-sm"
                />
              </div>
              <button
                type="submit"
                className="w-full rounded-xl bg-teal-600 px-4 py-2.5 text-sm font-medium text-white hover:bg-teal-700"
              >
                Crear familia como Usuario Principal
              </button>
            </form>
          </section>
        ) : (
          <div className="space-y-6">
            {/* IU-02: Selector de perfil en <= 2 toques */}
            <ProfileSelectorBar
              profiles={account.perfiles}
              activeProfileId={activeProfileId}
              onSelectProfile={handleSelectProfile}
            />

            {/* Navegación principal por pestañas */}
            <nav
              aria-label="Secciones del panel familiar"
              role="tablist"
              className="grid grid-cols-3 gap-1.5 sm:flex sm:flex-wrap sm:gap-2 rounded-2xl border border-slate-200 bg-white p-1.5 shadow-sm"
            >
              <button
                type="button"
                role="tab"
                aria-selected={activeTab === "monitoreo"}
                onClick={() => setActiveTab("monitoreo")}
                className={`inline-flex flex-1 items-center justify-center gap-1.5 sm:gap-2 rounded-xl px-2.5 py-2 sm:px-4 text-xs font-semibold transition-colors text-center ${
                  activeTab === "monitoreo"
                    ? "bg-teal-600 text-white shadow-sm"
                    : "text-slate-600 hover:bg-slate-50 hover:text-slate-900"
                }`}
              >
                <Activity className="h-4 w-4 shrink-0" aria-hidden="true" />
                <span className="hidden sm:inline">Monitoreo y Estímulos</span>
                <span className="sm:hidden">Monitoreo</span>
              </button>
              <button
                type="button"
                role="tab"
                aria-selected={activeTab === "comunicacion"}
                onClick={() => setActiveTab("comunicacion")}
                className={`inline-flex flex-1 items-center justify-center gap-1.5 sm:gap-2 rounded-xl px-2.5 py-2 sm:px-4 text-xs font-semibold transition-colors text-center ${
                  activeTab === "comunicacion"
                    ? "bg-teal-600 text-white shadow-sm"
                    : "text-slate-600 hover:bg-slate-50 hover:text-slate-900"
                }`}
              >
                <MessageSquare className="h-4 w-4 shrink-0" aria-hidden="true" />
                <span className="hidden sm:inline">Telegram y Recordatorios</span>
                <span className="sm:hidden">Telegram</span>
              </button>
              <button
                type="button"
                role="tab"
                aria-selected={activeTab === "familia"}
                onClick={() => setActiveTab("familia")}
                className={`inline-flex flex-1 items-center justify-center gap-1.5 sm:gap-2 rounded-xl px-2.5 py-2 sm:px-4 text-xs font-semibold transition-colors text-center ${
                  activeTab === "familia"
                    ? "bg-teal-600 text-white shadow-sm"
                    : "text-slate-600 hover:bg-slate-50 hover:text-slate-900"
                }`}
              >
                <Users className="h-4 w-4 shrink-0" aria-hidden="true" />
                <span className="hidden sm:inline">Administración Familiar</span>
                <span className="sm:hidden">Familia</span>
              </button>
            </nav>

            {activeTab === "monitoreo" && (
              /* M4 + M5: Gestión de Cunas, Emparejamiento Pico W (RF24–RF27), Bebé (RF28), Cuna en Vivo (M5) e Intercomunicador (RF11) */
              <CribAndDeviceManager
                accessToken={accessToken}
                activeProfileId={activeProfileId}
                userRole={account.rol}
                onError={setErrorBanner}
              />
            )}

            {activeTab === "comunicacion" && (
              /* M6: Vinculación de Telegram por usuario (RF38 / RF39 / RF19) y Recordatorios de Tomas (RF18) */
              <TelegramAndRemindersPanel
                accessToken={accessToken}
                activeProfileId={activeProfileId}
                onError={setErrorBanner}
              />
            )}

            {activeTab === "familia" && (
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
                              onClick={() => handleDeleteProfile(profile.id)}
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
                      <form onSubmit={handleCreateProfile} className="pt-2 space-y-3 border-t border-slate-100">
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
                          onClick={handleCreateInvitation}
                          className="w-full rounded-xl border border-teal-600 bg-teal-50 px-4 py-2 text-xs font-medium text-teal-800 hover:bg-teal-100"
                        >
                          Generar enlace de invitación de 1 uso (24 h)
                        </button>

                        {invitationResult && (
                          <div className="rounded-xl border border-slate-200 bg-slate-50 p-3 space-y-2">
                            <p className="text-[11px] font-medium text-slate-700">
                              Comparte este enlace manualmente (se muestra una sola vez, vence en 24 h):
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
                                onClick={handleCopyInvite}
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
                                onClick={() => handleRevokeMember(member.id, member.email)}
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

                {/* RNF03: Auditoría de Seguridad (Solo Usuario Principal) */}
                {isPrincipal && (
                  <section className="rounded-2xl border border-slate-200 bg-white p-6 shadow-sm space-y-3">
                    <div className="flex items-center justify-between">
                      <div className="flex items-center gap-2">
                        <Shield className="h-5 w-5 text-teal-600" aria-hidden="true" />
                        <h2 className="text-base font-semibold text-slate-900">
                          Auditoría de Seguridad Familiar (RNF03)
                        </h2>
                      </div>
                      <button
                        type="button"
                        disabled={loadingAudit}
                        onClick={() => void loadSecurityAudit()}
                        className="rounded-lg border border-slate-300 bg-white px-3 py-1 text-xs font-medium text-slate-700 hover:bg-slate-50 disabled:opacity-50"
                      >
                        {loadingAudit ? "Actualizando..." : "Actualizar bitácora"}
                      </button>
                    </div>

                    {auditEntries.length === 0 ? (
                      <p className="text-xs text-slate-500">
                        Sin eventos recientes registrados en la bitácora de seguridad.
                      </p>
                    ) : (
                      <div className="overflow-x-auto">
                        <table className="w-full text-left text-xs">
                          <thead className="border-b border-slate-200 text-[11px] uppercase text-slate-500">
                            <tr>
                              <th className="py-1.5 pr-3">Fecha</th>
                              <th className="py-1.5 pr-3">Evento</th>
                              <th className="py-1.5 pr-3">IP / Dispositivo</th>
                              <th className="py-1.5">Detalle</th>
                            </tr>
                          </thead>
                          <tbody className="divide-y divide-slate-100">
                            {auditEntries.map((entry, idx) => (
                              <tr key={`${entry.ts}-${entry.evento}-${idx}`}>
                                <td className="py-1.5 pr-3 whitespace-nowrap text-slate-500">
                                  {new Date(entry.ts).toLocaleString()}
                                </td>
                                <td className="py-1.5 pr-3 font-mono font-medium text-slate-800">
                                  {entry.evento}
                                </td>
                                <td className="py-1.5 pr-3 font-mono text-slate-600">
                                  {entry.dispositivoId ?? entry.ipOrigen ?? "—"}
                                </td>
                                <td className="py-1.5 text-slate-600">
                                  {entry.detalle ?? "—"}
                                </td>
                              </tr>
                            ))}
                          </tbody>
                        </table>
                      </div>
                    )}
                  </section>
                )}
              </div>
            )}
          </div>
        )}
      </main>

      {confirmRevokeMember && (
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
                  <strong className="font-mono text-slate-800">{confirmRevokeMember.email}</strong> a esta familia? Sus credenciales perderán acceso inmediato a cunas y telemetría.
                </p>
              </div>
            </div>
            <div className="flex justify-end gap-2 pt-2">
              <button
                type="button"
                disabled={isRevokingMember}
                onClick={() => setConfirmRevokeMember(null)}
                className="rounded-lg border border-slate-300 bg-white px-3 py-1.5 text-xs font-medium text-slate-700 hover:bg-slate-50"
              >
                Cancelar
              </button>
              <button
                type="button"
                disabled={isRevokingMember}
                onClick={() => void confirmRevokeMemberAction()}
                className="rounded-lg bg-red-600 px-3.5 py-1.5 text-xs font-semibold text-white hover:bg-red-700 disabled:opacity-50"
              >
                {isRevokingMember ? "Revocando..." : "Sí, revocar acceso"}
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );

  return (
    <Routes>
      <Route path="/" element={mainDashboard} />
      <Route
        path="/invitacion/:token"
        element={
          <AcceptInvitationPage
            accessToken={accessToken}
            onAccepted={(ctx) => setAccount(ctx)}
            onRequestGoogleLogin={handleGoogleLogin}
          />
        }
      />
      <Route path="*" element={mainDashboard} />
    </Routes>
  );
}
