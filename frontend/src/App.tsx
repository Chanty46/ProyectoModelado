import { useState } from "react";
import { Routes, Route } from "react-router-dom";
import { Activity, AlertTriangle, MessageSquare, Users, X } from "lucide-react";
import { canManagePrincipalActions } from "./lib/formatters";
import { useAuthSession } from "./hooks/useAuthSession";
import { useBackendHealth } from "./hooks/useBackendHealth";
import { ProfileSelectorBar } from "./components/ProfileSelectorBar";
import { CribAndDeviceManager } from "./components/CribAndDeviceManager";
import { TelegramAndRemindersPanel } from "./components/TelegramAndRemindersPanel";
import { DashboardHeader } from "./components/auth/DashboardHeader";
import { LoginLandingView } from "./components/auth/LoginLandingView";
import { BootstrapFamilyForm } from "./components/auth/BootstrapFamilyForm";
import { FamilyAdminPanel } from "./components/family/FamilyAdminPanel";
import { AcceptInvitationPage } from "./pages/AcceptInvitationPage";

type DashboardTab = "monitoreo" | "comunicacion" | "familia";

/** Componente raíz: rutas principales, sesión familiar y navegación por pestañas. */
export default function App() {
  const [errorBanner, setErrorBanner] = useState<string | null>(null);
  const [activeTab, setActiveTab] = useState<DashboardTab>("monitoreo");

  const {
    accessToken,
    account,
    setAccount,
    activeProfileId,
    setActiveProfileId,
    loading,
    localLoginEnabled,
    supabaseConfigured,
    isLoggingInLocal,
    isAuthenticatingEmail,
    handleSelectProfile,
    refreshAccountContext,
    handleLocalLogin,
    handleEmailLogin,
    handleEmailSignUp,
    handleSignOut,
  } = useAuthSession({ onError: setErrorBanner });

  const { health, healthOffline } = useBackendHealth();

  const isPrincipal = canManagePrincipalActions(account?.rol ?? null);
  const activeCaregiver = account?.perfiles.find((p) => p.id === activeProfileId) ?? null;

  const mainDashboard = (
    <div className="min-h-screen flex flex-col nursery-backdrop text-slate-900">
      <DashboardHeader
        account={account}
        activeCaregiver={activeCaregiver}
        accessToken={accessToken}
        health={health}
        healthOffline={healthOffline}
        onSignOut={() => void handleSignOut()}
      />

      <main className="flex-1 w-full mx-auto max-w-5xl p-6 space-y-6">
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
          <LoginLandingView
            supabaseConfigured={supabaseConfigured}
            isAuthenticatingEmail={isAuthenticatingEmail}
            onEmailLogin={handleEmailLogin}
            onEmailSignUp={handleEmailSignUp}
            localLoginEnabled={localLoginEnabled}
            isLoggingInLocal={isLoggingInLocal}
            onLocalLogin={handleLocalLogin}
          />
        ) : !account?.familia ? (
          <BootstrapFamilyForm
            accessToken={accessToken}
            onCreated={(created) => {
              setAccount(created);
              if (created.perfiles.length > 0) {
                setActiveProfileId(created.perfiles[0].id);
              }
            }}
            onError={setErrorBanner}
          />
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
                localLoginEnabled={localLoginEnabled}
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
              <FamilyAdminPanel
                accessToken={accessToken}
                account={account}
                isPrincipal={isPrincipal}
                onRefreshAccount={refreshAccountContext}
                onError={setErrorBanner}
              />
            )}
          </div>
        )}
      </main>

      <footer className="border-t border-slate-200/70 bg-white/60 py-3 px-6 text-center text-[11px] text-slate-500">
        Cuna Inteligente · Panel familiar local (Web · API .NET 8 · Pico W TCP · Supabase RLS)
      </footer>
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
            supabaseConfigured={supabaseConfigured}
            isAuthenticatingEmail={isAuthenticatingEmail}
            onAccepted={(ctx) => setAccount(ctx)}
            onEmailLogin={handleEmailLogin}
            onEmailSignUp={handleEmailSignUp}
          />
        }
      />
      <Route path="*" element={mainDashboard} />
    </Routes>
  );
}
