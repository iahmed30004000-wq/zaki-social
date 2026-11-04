"use client";

import { useState } from "react";
import { useAuth } from "@/components/auth-provider";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { errorText } from "@/lib/api";

export function ChangePasswordForm({ onDone }: { onDone?: () => void }) {
  const { changePassword } = useAuth();
  const [current, setCurrent] = useState("");
  const [next, setNext] = useState("");
  const [again, setAgain] = useState("");
  const [error, setError] = useState("");
  const [busy, setBusy] = useState(false);

  async function submit(e: React.FormEvent) {
    e.preventDefault();
    setError("");
    if (next !== again) {
      setError("كلمتا السر الجديدتان مش متطابقتين");
      return;
    }
    setBusy(true);
    try {
      await changePassword(current, next);
      setCurrent("");
      setNext("");
      setAgain("");
      onDone?.();
    } catch (err) {
      setError(errorText(err));
    } finally {
      setBusy(false);
    }
  }

  return (
    <form onSubmit={submit} className="space-y-3 rounded-xl border border-border bg-card p-5">
      <label className="block space-y-1 text-sm">
        <span>كلمة السر الحالية</span>
        <Input type="password" required autoComplete="current-password" dir="ltr" value={current} onChange={(e) => setCurrent(e.target.value)} />
      </label>
      <label className="block space-y-1 text-sm">
        <span>كلمة السر الجديدة <span className="text-muted-foreground">(10 أحرف على الأقل)</span></span>
        <Input type="password" required minLength={10} autoComplete="new-password" dir="ltr" value={next} onChange={(e) => setNext(e.target.value)} />
      </label>
      <label className="block space-y-1 text-sm">
        <span>أعد كتابة الجديدة</span>
        <Input type="password" required minLength={10} autoComplete="new-password" dir="ltr" value={again} onChange={(e) => setAgain(e.target.value)} />
      </label>
      {error && <p role="alert" className="text-sm text-warn">{error}</p>}
      <Button type="submit" disabled={busy}>{busy ? "لحظة..." : "تغيير كلمة السر"}</Button>
    </form>
  );
}
