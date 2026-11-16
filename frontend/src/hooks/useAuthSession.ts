import { useCallback, useEffect, useState } from "react";
import { supabase } from "../lib/supabase";
import { accountApi, ApiError, type AccountContextResponse } from "../lib/api";

const ACTIVE_PROFILE_STORAGE_KEY = "cuna_active_profile_id";

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

interface UseAuthSessionOptions {
  onError: (msg: string | null) => void;
}

/**
 * Hook de sesión autenticada y contexto familiar en tiempo real (RF01, RF22, RF23).
 * Sincroniza el JWT de Supabase Auth, restaura el último perfil de cuidador elegido y
 * se suscribe vía Realtime a cambios en membresías (`usuarios`) y perfiles (`perfiles_cuidador`).
 */
export function useAuthSession({ onError }: UseAuthSessionOptions) {
  const [accessToken, setAccessToken] = useState<string | null>(null);
  const [account, setAccount] = useState<AccountContextResponse | null>(null);
  const [activeProfileId, setActiveProfileId] = useState<string | null>(null);
  const [loading, setLoading] = useState<boolean>(true);

  const clearLocalSessionState = useCallback(() => {
    setAccessToken(null);
    setAccount(null);
    setActiveProfileId(null);
    onError(null);
    writeSavedProfileId(null);
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
          onError(err.message);
          // Un 401 indica token revocado o sesión inválida: limpiamos el estado local inmediatamente
          if (err.status === 401) {
            clearLocalSessionState();
            if (supabase) {
              void supabase.auth.signOut();
            }
          }
        }
      }
    },
    [clearLocalSessionState, onError]
  );

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

  const handleGoogleLogin = useCallback(async () => {
    if (!supabase) {
      onError("Configura VITE_SUPABASE_URL y VITE_SUPABASE_PUBLISHABLE_KEY en frontend/.env.");
      return;
    }

    onError(null);
    const { error } = await supabase.auth.signInWithOAuth({
      provider: "google",
      options: {
        redirectTo: window.location.origin,
      },
    });

    if (error) {
      onError(error.message);
    }
  }, [onError]);

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
    handleSelectProfile,
    refreshAccountContext,
    handleGoogleLogin,
    handleSignOut,
  };
}
