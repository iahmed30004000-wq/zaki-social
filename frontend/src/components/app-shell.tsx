"use client";

import Link from "next/link";
import { usePathname, useRouter } from "next/navigation";
import { useEffect, useState } from "react";
import { roleLabel, useAuth } from "@/components/auth-provider";
import { ChangePasswordForm } from "@/components/change-password-form";
import { Button } from "@/components/ui/button";
import { errorText } from "@/lib/api";
import { cn } from "@/lib/utils";

// وحدات المنصة. الجاهزة تفتح؛ الباقي بتنبني بالخطوات الجاية.
export const modules = [
  { href: "/app", label: "الرئيسية", ready: true },
  { href: "/app/accounts", label: "إدارة الحسابات", ready: true },
  { href: "/app/composer", label: "المُؤلِّف", ready: true },
  { href: "/app/calendar", label: "التقويم", ready: true },
  { href: "/app/prompts", label: "مركز البرومبتات", ready: true },
  { href: "/app/automations", label: "الأتمتة", ready: true },
  { href: "/app/analytics", label: "التحليلات", ready: true },
  { href: "/app/media", label: "مكتبة الوسائط", ready: true },
  { href: "/app/team", label: "الفريق والصلاحيات", ready: true },
  { href: "/app/settings", label: "الإعدادات", ready: true },
] as const;

export function AppShell({ children }: { children: React.ReactNode }) {
  const { status, session, logout, switchWorkspace, createWorkspace } = useAuth();
  const router = useRouter();
  const pathname = usePathname();
  const [error, setError] = useState("");

  useEffect(() => {
    if (status === "anon") router.replace("/login");
  }, [status, router]);

  if (status !== "authed" || !session) {
    return <p className="p-8 text-center text-muted-foreground">جاري التحميل...</p>;
  }

  // كلمة سر مؤقتة حطها المالك: ما بنفتح أي شي قبل ما يغيّرها.
  if (session.mustChangePassword) {
    return (
      <main className="mx-auto flex min-h-screen max-w-sm flex-col justify-center gap-4 px-4 py-12">
        <h1 className="text-2xl font-bold">غيّر كلمة السر</h1>
        <p className="text-sm text-muted-foreground">دخلت بكلمة سر مؤقتة من مالك المساحة. اختار كلمة سر خاصة فيك لتكمّل.</p>
        <ChangePasswordForm />
        <Button variant="outline" size="sm" onClick={() => logout().then(() => router.replace("/login"))}>تسجيل الخروج</Button>
      </main>
    );
  }

  async function onSwitch(id: string) {
    setError("");
    try {
      if (id === "__new") {
        const name = window.prompt("اسم المساحة الجديدة؟");
        if (name?.trim()) await createWorkspace(name.trim());
      } else if (id !== session!.workspace.id) {
        await switchWorkspace(id);
      }
    } catch (e) {
      setError(errorText(e));
    }
  }

  return (
    <div className="flex min-h-screen flex-col md:flex-row">
      <aside className="shrink-0 border-b border-border bg-card md:w-64 md:border-b-0 md:border-s">
        <div className="space-y-3 p-4">
          <p className="text-lg font-bold">صانع الترند</p>
          <label className="block space-y-1 text-xs text-muted-foreground">
            <span>المساحة</span>
            <select
              className="h-9 w-full rounded-lg border border-border bg-background px-2 text-sm text-foreground"
              value={session.workspace.id}
              onChange={(e) => onSwitch(e.target.value)}
            >
              {session.workspaces.map((w) => (
                <option key={w.id} value={w.id}>
                  {w.name} ({roleLabel[w.role] ?? w.role})
                </option>
              ))}
              <option value="__new">＋ مساحة جديدة</option>
            </select>
          </label>
          {error && <p role="alert" className="text-xs text-warn">{error}</p>}
        </div>
        <nav className="flex gap-1 overflow-x-auto px-2 pb-3 md:flex-col md:overflow-visible">
          {modules.map((m) => (
            <Link
              key={m.href}
              href={m.href}
              className={cn(
                "whitespace-nowrap rounded-lg px-3 py-2 text-sm hover:bg-muted",
                pathname === m.href && "bg-muted font-semibold text-primary",
              )}
            >
              {m.label}
            </Link>
          ))}
        </nav>
        <div className="hidden space-y-2 border-t border-border p-4 text-xs text-muted-foreground md:block">
          <p dir="ltr" className="truncate text-start">{session.user.email}</p>
          <Button variant="outline" size="sm" onClick={() => logout().then(() => router.replace("/login"))}>
            تسجيل الخروج
          </Button>
        </div>
      </aside>
      <main className="flex-1 p-4 md:p-8">{children}</main>
    </div>
  );
}
