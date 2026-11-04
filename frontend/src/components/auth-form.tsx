"use client";

import Link from "next/link";
import { useRouter } from "next/navigation";
import { useEffect, useState } from "react";
import { useAuth } from "@/components/auth-provider";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { errorText } from "@/lib/api";

type Props = { mode: "login" | "register"; invite?: string; inviteEmail?: string };

export function AuthForm({ mode, invite = "", inviteEmail = "" }: Props) {
  const { status, login, register } = useAuth();
  const router = useRouter();
  const [email, setEmail] = useState(inviteEmail);
  const [password, setPassword] = useState("");
  const [workspaceName, setWorkspaceName] = useState("");
  const [error, setError] = useState("");
  const [busy, setBusy] = useState(false);
  const isRegister = mode === "register";

  useEffect(() => {
    if (status === "authed") router.replace("/app");
  }, [status, router]);

  async function submit(e: React.FormEvent) {
    e.preventDefault();
    setError("");
    setBusy(true);
    try {
      if (isRegister) await register({ email, password, workspaceName: workspaceName || undefined, inviteCode: invite || undefined });
      else await login(email, password);
      router.replace("/app");
    } catch (err) {
      setError(errorText(err));
    } finally {
      setBusy(false);
    }
  }

  return (
    <main className="mx-auto flex min-h-screen max-w-sm flex-col justify-center gap-6 px-4 py-12">
      <header className="space-y-1">
        <h1 className="text-2xl font-bold">{isRegister ? (invite ? "قبول الدعوة" : "إنشاء حساب") : "تسجيل الدخول"}</h1>
        <p className="text-sm text-muted-foreground">صانع الترند</p>
      </header>

      <form onSubmit={submit} className="space-y-4 rounded-xl border border-border bg-card p-5">
        <label className="block space-y-1 text-sm">
          <span>الإيميل</span>
          <Input type="email" required autoComplete="email" dir="ltr" value={email} onChange={(e) => setEmail(e.target.value)} />
        </label>
        <label className="block space-y-1 text-sm">
          <span>كلمة السر {isRegister && <span className="text-muted-foreground">(10 أحرف على الأقل)</span>}</span>
          <Input
            type="password"
            required
            minLength={isRegister ? 10 : undefined}
            autoComplete={isRegister ? "new-password" : "current-password"}
            dir="ltr"
            value={password}
            onChange={(e) => setPassword(e.target.value)}
          />
        </label>
        {isRegister && !invite && (
          <label className="block space-y-1 text-sm">
            <span>اسم المساحة <span className="text-muted-foreground">(اختياري)</span></span>
            <Input value={workspaceName} onChange={(e) => setWorkspaceName(e.target.value)} placeholder="مثلاً: متجر زكي" />
          </label>
        )}
        {error && <p role="alert" className="text-sm text-warn">{error}</p>}
        <Button type="submit" className="w-full" disabled={busy}>
          {busy ? "لحظة..." : isRegister ? "إنشاء الحساب" : "دخول"}
        </Button>
      </form>

      <p className="text-center text-sm text-muted-foreground">
        {isRegister ? (
          <>عندك حساب؟ <Link href="/login" className="text-primary underline">سجّل الدخول</Link></>
        ) : (
          <>ما عندك حساب؟ <Link href="/register" className="text-primary underline">أنشئ حساب</Link></>
        )}
      </p>
    </main>
  );
}
