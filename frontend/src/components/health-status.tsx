"use client";

import { useEffect, useState } from "react";
import { Button } from "@/components/ui/button";

type Health = { status: string; database: string; redis: string };
type State = { kind: "loading" } | { kind: "error" } | { kind: "done"; health: Health };

const label = (v: string) => (v === "ok" ? "شغّال" : "متوقف");

export function HealthStatus() {
  const [state, setState] = useState<State>({ kind: "loading" });

  async function load() {
    setState({ kind: "loading" });
    try {
      const res = await fetch("/api/health", { cache: "no-store" });
      if (!res.ok) throw new Error("bad status");
      setState({ kind: "done", health: await res.json() });
    } catch {
      setState({ kind: "error" });
    }
  }

  useEffect(() => {
    load();
  }, []);

  return (
    <div className="rounded-xl border border-border bg-card p-5">
      <div className="mb-4 flex items-center justify-between gap-3">
        <h2 className="text-lg font-semibold">حالة النظام</h2>
        <Button variant="outline" size="sm" onClick={load}>
          تحديث
        </Button>
      </div>
      {state.kind === "loading" && <p className="text-muted-foreground">جاري الفحص...</p>}
      {state.kind === "error" && (
        <p className="text-warn">ما قدرنا نوصل للخادم الخلفي. تأكد إنه شغّال.</p>
      )}
      {state.kind === "done" && (
        <ul className="space-y-2">
          <Row name="الخادم الخلفي" ok={state.health.status !== undefined} text="شغّال" />
          <Row name="قاعدة البيانات" ok={state.health.database === "ok"} text={label(state.health.database)} />
          <Row name="الذاكرة السريعة (ريديس)" ok={state.health.redis === "ok"} text={label(state.health.redis)} />
        </ul>
      )}
    </div>
  );
}

function Row({ name, ok, text }: { name: string; ok: boolean; text: string }) {
  return (
    <li className="flex items-center justify-between rounded-lg bg-muted px-3 py-2">
      <span>{name}</span>
      <span className={ok ? "font-medium text-ok" : "font-medium text-warn"}>{text}</span>
    </li>
  );
}
