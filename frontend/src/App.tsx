import { useEffect, useState, useCallback, type FormEvent } from "react";
import { Routes, Route } from "react-router-dom";
import {
  ShieldCheck,
  LogOut,
  Users,
  Link2,
  Trash2,
  Plus,
  AlertTriangle,
  Copy,
  Check,
} from "lucide-react";
import { supabase } from "./lib/supabase";
import {
  accountApi,
  ApiError,
  type AccountContextResponse,
  type CreateInvitationResponse,
} from "./lib/api";
import { ProfileSelectorBar } from "./components/ProfileSelectorBar";
import { CribAndDeviceManager } from "./components/CribAndDeviceManager";
import { TelegramAndRemindersPanel } from "./components/TelegramAndRemindersPanel";
import { AcceptInvitationPage } from "./pages/AcceptInvitationPage";

export default function App() {
  const [accessToken, setAccessToken] = useState<string | null>(null);
  const [account, setAccount] = useState<AccountContextResponse | null>(null);
  const [activeProfileId, setActiveProfileId] = useState<string | null>(null);
  const [loading, setLoading] = useState<boolean>(true);
  const [errorBanner, setErrorBanner] = useState<string | null>(null);

  // Formularios
  const [familyNameInput, setFamilyNameInput] = useState("Mi Familia");
  const [initialProfileInput, setInitialProfileInput] = useState("Mamá / Papá");
  const [newProfileName, setNewProfileName] = useState("");
  const [newProfileAvatar, setNewProfileAvatar] = useState("F1");
  const [invitationResult, setInvitationResult] = useState<CreateInvitationResponse | null>(null);
  const [copiedInvite, setCopiedInvite] = useState(false);

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
  useEffect(() => {
    const sbClient = supabase;
    if (!sbClient || !accessToken || !account?.familia) {
      return;
    }

    const channel = sbClient
      .channel(`family-sync-${account.familia.id}`)
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
  }, [accessToken, account?.familia, refreshAccountContext]);

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

  async function handleRevokeMember(memberUserId: string, email: string) {
    if (!accessToken) {
      return;
    }
    if (!window.confirm(`¿Confirmas revocar el acceso de ${email} a la familia (RF22)?`)) {
      return;
    }
    setErrorBanner(null);
    try {
      await accountApi.revokeMember(accessToken, memberUserId);
      await refreshAccountContext(accessToken);
    } catch (err) {
      if (err instanceof ApiError) {
        setErrorBanner(err.message);
      }
    }
  }

  async function handleCopyInvite() {
    if (!invitationResult) {
      return;
    }
    await navigator.clipboard.writeText(invitationResult.inviteUrl);
    setCopiedInvite(true);
  }

  const mainDashboard = (
    <div className="min-h-screen bg-slate-50 text-slate-900">
      <header className="border-b border-slate-200 bg-white">
        <div className="mx-auto flex max-w-5xl flex-wrap items-center justify-between gap-4 px-6 py-4">
          <div className="flex items-center gap-3">
            <div className="flex h-10 w-10 items-center justify-center rounded-full bg-teal-50 text-teal-600">
              <ShieldCheck className="h-5 w-5" aria-hidden="true" />
            </div>
            <div>
              <h1 className="text-lg font-semibold text-slate-900">
                Cuna Inteligente
              </h1>
              <p className="text-xs text-slate-500">
                {account?.familia
                  ? `${account.familia.nombre} · Rol: ${account.rol === "principal" ? "Usuario Principal" : "Usuario Secundario"}`
                  : "Supervisión Perimetral, Microclima y Nutrición Infantil"}
              </p>
            </div>
          </div>

          {accessToken && (
            <button
              type="button"
              onClick={handleSignOut}
              className="inline-flex items-center gap-1.5 rounded-lg border border-slate-300 bg-white px-3 py-1.5 text-xs font-medium text-slate-700 hover:bg-slate-100"
            >
              <LogOut className="h-3.5 w-3.5" aria-hidden="true" />
              Cerrar sesión
            </button>
          )}
        </div>
      </header>

      <main className="mx-auto max-w-5xl p-6 space-y-6">
        {errorBanner && (
          <div
            role="alert"
            className="flex items-start gap-2.5 rounded-xl border border-red-200 bg-red-50 p-4 text-sm text-red-800"
          >
            <AlertTriangle className="h-5 w-5 shrink-0 text-red-600 mt-0.5" aria-hidden="true" />
            <div className="flex-1">
              <p className="font-medium">Aviso de seguridad / autorización</p>
              <p className="text-xs mt-0.5">{errorBanner}</p>
            </div>
          </div>
        )}

        {loading ? (
          <div className="rounded-2xl border border-slate-200 bg-white p-8 text-center text-sm text-slate-500">
            Cargando sesión...
          </div>
        ) : !accessToken ? (
          <section className="mx-auto max-w-md rounded-2xl border border-slate-200 bg-white p-8 shadow-sm text-center space-y-5">
            <div className="mx-auto flex h-12 w-12 items-center justify-center rounded-full bg-teal-50 text-teal-600">
              <ShieldCheck className="h-6 w-6" aria-hidden="true" />
            </div>
            <div className="space-y-2">
              <h2 className="text-xl font-semibold text-slate-900">
                Iniciar Sesión Familiar (RF01)
              </h2>
              <p className="text-sm text-slate-600">
                Accede con tu cuenta de Google para gestionar la cuna inteligente o unirte a una familia existente.
              </p>
            </div>
            <button
              type="button"
              onClick={handleGoogleLogin}
              className="w-full rounded-xl bg-teal-600 px-4 py-2.5 text-sm font-medium text-white shadow-sm hover:bg-teal-700 focus:outline-none focus:ring-2 focus:ring-teal-500"
            >
              Continuar con Google
            </button>
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

            {/* M4 + M5: Gestión de Cunas, Emparejamiento Pico W (RF24–RF27), Bebé (RF28), Cuna en Vivo (M5) e Intercomunicador (RF11) */}
            <CribAndDeviceManager
              accessToken={accessToken}
              activeProfileId={activeProfileId}
              userRole={account.rol}
              onError={setErrorBanner}
            />

            {/* M6: Vinculación de Telegram por usuario (RF38 / RF39 / RF19) y Recordatorios de Tomas (RF18) */}
            <TelegramAndRemindersPanel
              accessToken={accessToken}
              activeProfileId={activeProfileId}
              onError={setErrorBanner}
            />

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
                      <button
                        type="button"
                        onClick={() => handleDeleteProfile(profile.id)}
                        title="Dar de baja lógica al perfil (RF23)"
                        className="inline-flex items-center gap-1 rounded-md px-2 py-1 text-xs text-red-600 hover:bg-red-50"
                      >
                        <Trash2 className="h-3.5 w-3.5" aria-hidden="true" />
                        Baja
                      </button>
                    </li>
                  ))}
                </ul>

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
                        {member.rol === "secundario" && (
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
          </div>
        )}
      </main>
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
    </Routes>
  );
}
