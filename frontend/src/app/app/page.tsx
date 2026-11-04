"use client";

import Link from "next/link";
import { roleLabel, useAuth } from "@/components/auth-provider";
import { modules } from "@/components/app-shell";

export default function OverviewPage() {
  const { session } = useAuth();
  if (!session) return null;
  return (
    <div className="max-w-3xl space-y-6">
      <header className="space-y-1">
        <h1 className="text-2xl font-bold">أهلاً بك 👋</h1>
        <p className="text-muted-foreground">
          مساحة «{session.workspace.name}» — صلاحيتك: {roleLabel[session.workspace.role] ?? session.workspace.role}
        </p>
      </header>

      <ul className="grid gap-3 sm:grid-cols-2">
        {modules
          .filter((m) => m.href !== "/app")
          .map((m) => (
            <li key={m.href}>
              <Link href={m.href} className="block rounded-xl border border-border bg-card p-4 hover:border-primary">
                <p className="font-medium">{m.label}</p>
                <p className="mt-1 text-xs text-muted-foreground">{"ready" in m && m.ready ? "جاهز" : `قريباً — الخطوة ${"step" in m ? m.step : ""}`}</p>
              </Link>
            </li>
          ))}
      </ul>
    </div>
  );
}
