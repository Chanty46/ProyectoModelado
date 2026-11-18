import { useCallback, useEffect, useState } from "react";
import { supabase } from "../lib/supabase";
import { accountApi, ApiError, type AccountContextResponse } from "../lib/api";

const ACTIVE_PROFILE_STORAGE_KEY = "cuna_active_profile_id";
const LOCAL_TOKEN_STORAGE_KEY = "cuna_local_demo_access_token";

function readSavedProfileId(): string | null {
  try {
    return localStorage.getItem(ACTIVE_PROFILE_STORAGE_KEY);
  } catch {
    return null;
  }
}

function writeSavedProfileId(profileId: string | null): void {
  try {
    if (profileId === null) {
      localStorage.removeItem(ACTIVE_PROFILE_STORAGE_KEY);
    } else {
      localStorage.setItem(ACTIVE_PROFILE_STORAGE_KEY, profileId);
    }
  } catch {
    // Ignorar excepciones de almacenamiento local (modo privado o bloqueado)
  }
}

function readSavedLocalToken(): string | null {
  try {
    return sessionStorage.getItem(LOCAL_TOKEN_STORAGE_KEY);
  } catch {
    return null;
  }
}

function writeSavedLocalToken(token: string | null): void {
  try {
    if (token === null) {
      sessionStorage.removeItem(LOCAL_TOKEN_STORAGE_KEY);
    } else {
      sessionStorage.setItem(LOCAL_TOKEN_STORAGE_KEY, token);
    }
  } catch {
    // Ignorar excepciones de almacenamiento de sesión
  }
}

interface UseAuthSessionOptions {
  onError: (msg: string | null) => void;
}

/**
 * Hook de sesión autenticada y contexto familiar en tiempo real (RF01, RF22, RF23).
 * Soporta tanto Supabase Auth (correo y contraseña + Realtime) como el modo local
 * temporal de pruebas y presentación preliminar.
 */
function formatSupabaseAuthError(message: string): string {
  const normalized = message.toLowerCase();
  if (normalized.includes("invalid login credentials")) {
    return "Correo o contraseña incorrectos en Supabase Auth.";
  }
  if (normalized.includes("email not confirmed")) {
    return "Tu correo aún no ha sido confirmado en Supabase. Revisa tu bandeja o desactiva 'Confirm email' en Supabase Auth → Providers → Email.";
  }
  if (normalized.includes("user already registered")) {
    return "Este correo ya tiene una cuenta registrada. Cambia a 'Iniciar sesión'.";
  }
  if (normalized.includes("password should be at least")) {
    return "La contraseña de Supabase debe tener al menos 6 caracteres.";
  }
  return message;
}

export function useAuthSession({ onError }: UseAuthSessionOptions) {
  const [accessToken, setAccessToken] = useState<string | null>(null);
  const [account, setAccount] = useState<AccountContextResponse | null>(null);
  const [activeProfileId, setActiveProfileId] = useState<string | null>(null);
  const [loading, setLoading] = useState<boolean>(true);
  const [localLoginEnabled, setLocalLoginEnabled] = useState<boolean>(!supabase);
  const [supabaseConfigured, setSupabaseConfigured] = useState<boolean>(Boolean(supabase));
  const [isLoggingInLocal, setIsLoggingInLocal] = useState<boolean>(false);
  const [isAuthenticatingEmail, setIsAuthenticatingEmail] = useState<boolean>(false);

  const clearLocalSessionState = useCallback(() => {
    setAccessToken(null);
    setAccount(null);
    setActiveProfileId(null);
    onError(null);
    writeSavedProfileId(null);
    writeSavedLocalToken(null);
  }, [onError]);

  const handleSelectProfile = useCallback((profileId: string) => {
    setActiveProfileId(profileId);
    writeSavedProfileId(profileId);
  }, []);

  const refreshAccountContext = useCallback(
    async (token: string) => {
      try {
        const ctx = await accountApi.getMe(token);
        setAccount(ctx);
        if (ctx.perfiles.length > 0) {
          const savedProfileId = readSavedProfileId();
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
          // Un 401 indica token revocado o expirado (ej. reinicio del backend local)
          if (err.status === 401) {
            clearLocalSessionState();
            if (supabase) {
              void supabase.auth.signOut();
            }
          } else {
            onError(err.message);
          }
        }
      }
    },
    [clearLocalSessionState, onError]
  );

  useEffect(() => {
    const controller = new AbortController();
    accountApi
      .getAuthMode(controller.signal)
      .then((mode) => {
        setLocalLoginEnabled(mode.localLoginEnabled);
        setSupabaseConfigured(mode.supabaseConfigured && Boolean(supabase));
      })
      .catch(() => {
        // Si el backend aún está arrancando, mantenemos respaldo local cuando no hay Supabase
        setLocalLoginEnabled(!supabase);
        setSupabaseConfigured(Boolean(supabase));
      });
    return () => controller.abort();
  }, []);

  useEffect(() => {
    const savedLocalToken = readSavedLocalToken();

    if (!supabase) {
      if (savedLocalToken) {
        setAccessToken(savedLocalToken);
        refreshAccountContext(savedLocalToken).finally(() => setLoading(false));
      } else {
        setLoading(false);
      }
      return;
    }

    supabase.auth.getSession().then(({ data }) => {
      const token = data.session?.access_token ?? savedLocalToken ?? null;
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
        if (!readSavedLocalToken()) {
          clearLocalSessionState();
        }
      } else {
        writeSavedLocalToken(null);
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
    if (!sbClient || !accessToken || !familyId || readSavedLocalToken()) {
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

  const handleLocalLogin = useCallback(
    async (username: string, password: string) => {
      onError(null);
      setIsLoggingInLocal(true);
      try {
        const res = await accountApi.localLogin({ username, password });
        writeSavedLocalToken(res.accessToken);
        setAccessToken(res.accessToken);
        await refreshAccountContext(res.accessToken);
      } catch (err) {
        if (err instanceof ApiError) {
          onError(err.message);
        } else {
          onError("No se pudo conectar con el servidor local (verifica que el backend esté corriendo en :5080).");
        }
      } finally {
        setIsLoggingInLocal(false);
      }
    },
    [onError, refreshAccountContext]
  );

  const handleEmailLogin = useCallback(
    async (email: string, password: string) => {
      if (!supabase) {
        onError(
          "Supabase Auth requiere configurar VITE_SUPABASE_URL y VITE_SUPABASE_PUBLISHABLE_KEY en frontend/.env. Puedes usar el acceso local Padre / 123."
        );
        return;
      }

      onError(null);
      setIsAuthenticatingEmail(true);
      try {
        const { data, error } = await supabase.auth.signInWithPassword({
          email: email.trim(),
          password,
        });
        if (error) {
          onError(formatSupabaseAuthError(error.message));
          return;
        }

        const token = data.session?.access_token ?? null;
        if (token) {
          writeSavedLocalToken(null);
          setAccessToken(token);
          await refreshAccountContext(token);
        }
      } finally {
        setIsAuthenticatingEmail(false);
      }
    },
    [onError, refreshAccountContext]
  );

  const handleEmailSignUp = useCallback(
    async (
      email: string,
      password: string
    ): Promise<{ needsEmailConfirmation: boolean; message: string }> => {
      if (!supabase) {
        const msg =
          "Para crear una cuenta en la nube configura VITE_SUPABASE_URL y VITE_SUPABASE_PUBLISHABLE_KEY en frontend/.env.";
        onError(msg);
        return { needsEmailConfirmation: false, message: msg };
      }

      onError(null);
      setIsAuthenticatingEmail(true);
      try {
        const { data, error } = await supabase.auth.signUp({
          email: email.trim(),
          password,
        });
        if (error) {
          const friendly = formatSupabaseAuthError(error.message);
          onError(friendly);
          return { needsEmailConfirmation: false, message: friendly };
        }

        const token = data.session?.access_token ?? null;
        if (token) {
          writeSavedLocalToken(null);
          setAccessToken(token);
          await refreshAccountContext(token);
          return {
            needsEmailConfirmation: false,
            message: "Cuenta creada e iniciada correctamente.",
          };
        }

        return {
          needsEmailConfirmation: true,
          message:
            "Cuenta registrada. Revisa tu correo para confirmar la dirección o desactiva 'Confirm email' en Supabase Auth → Providers → Email.",
        };
      } finally {
        setIsAuthenticatingEmail(false);
      }
    },
    [onError, refreshAccountContext]
  );

  const handleSignOut = useCallback(async () => {
    if (supabase) {
      await supabase.auth.signOut();
    }
    clearLocalSessionState();
  }, [clearLocalSessionState]);

  return {
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
  };
}
