"use client";

import { createContext, useCallback, useContext, useEffect, useMemo, useState } from "react";
import { api, authCall, refreshSession, setAccessToken, type Session } from "@/lib/api";

type Status = "loading" | "anon" | "authed";

type AuthState = {
  status: Status;
  session: Session | null;
  login: (email: string, password: string) => Promise<void>;
  register: (input: { email: string; password: string; workspaceName?: string; inviteCode?: string }) => Promise<void>;
  logout: () => Promise<void>;
  switchWorkspace: (workspaceId: string) => Promise<void>;
  createWorkspace: (name: string) => Promise<void>;
  changePassword: (current: string, next: string) => Promise<void>;
};

const AuthContext = createContext<AuthState | null>(null);

export function AuthProvider({ children }: { children: React.ReactNode }) {
  const [status, setStatus] = useState<Status>("loading");
  const [session, setSession] = useState<Session | null>(null);

  const apply = useCallback((next: Session | null) => {
    setSession(next);
    setStatus(next ? "authed" : "anon");
  }, []);

  useEffect(() => {
    refreshSession().then(apply);
  }, [apply]);

  const value = useMemo<AuthState>(
    () => ({
      status,
      session,
      login: async (email, password) => apply(await authCall("/api/auth/login", { email, password })),
      register: async (input) => apply(await authCall("/api/auth/register", input)),
      logout: async () => {
        await api("/api/auth/logout", { method: "POST" }).catch(() => undefined);
        setAccessToken(null);
        apply(null);
      },
      switchWorkspace: async (workspaceId) => apply(await authCall("/api/auth/switch-workspace", { workspaceId })),
      createWorkspace: async (name) => apply(await authCall("/api/workspaces", { name })),
      changePassword: async (current, next) => apply(await authCall("/api/auth/change-password", { current, next })),
    }),
    [status, session, apply],
  );

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

export function useAuth(): AuthState {
  const ctx = useContext(AuthContext);
  if (!ctx) throw new Error("useAuth must be used inside AuthProvider");
  return ctx;
}

export const roleLabel: Record<string, string> = { owner: "مالك", editor: "محرّر", viewer: "مشاهد" };
