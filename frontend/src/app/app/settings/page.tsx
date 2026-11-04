"use client";

import { useState } from "react";
import { useAuth } from "@/components/auth-provider";
import { ChangePasswordForm } from "@/components/change-password-form";

export default function SettingsPage() {
  const { session } = useAuth();
  const [done, setDone] = useState(false);

  return (
    <div className="max-w-xl space-y-6">
      <h1 className="text-2xl font-bold">الإعدادات</h1>

      <section className="space-y-1 rounded-xl border border-border bg-card p-5">
        <h2 className="font-semibold">حسابك</h2>
        <p dir="ltr" className="text-start text-sm text-muted-foreground">{session?.user.email}</p>
      </section>

      <section className="space-y-3">
        <h2 className="font-semibold">تغيير كلمة السر</h2>
        <p className="text-sm text-muted-foreground">بعد التغيير بنسجّل خروجك من باقي الأجهزة.</p>
        {done && <p role="status" className="text-sm text-primary">انغيّرت كلمة السر ✓</p>}
        <ChangePasswordForm onDone={() => setDone(true)} />
      </section>
    </div>
  );
}
