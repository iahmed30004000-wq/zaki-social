"use client";

import { useCallback, useEffect, useState } from "react";
import { roleLabel, useAuth } from "@/components/auth-provider";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { api, errorText } from "@/lib/api";

type Member = { userId: string; email: string; role: string; managed: boolean };
type Created = { email: string; tempPassword: string; resetFor?: boolean };
type Invitation = { id: string; email: string; role: string; expiresAt: string };

const roles = ["owner", "editor", "viewer"] as const;

export default function TeamPage() {
  const { session } = useAuth();
  const isOwner = session?.workspace.role === "owner";
  const [members, setMembers] = useState<Member[]>([]);
  const [invites, setInvites] = useState<Invitation[]>([]);
  const [error, setError] = useState("");
  const [email, setEmail] = useState("");
  const [role, setRole] = useState<string>("viewer");
  const [link, setLink] = useState("");
  const [newEmail, setNewEmail] = useState("");
  const [newRole, setNewRole] = useState<string>("editor");
  const [newPassword, setNewPassword] = useState("");
  const [created, setCreated] = useState<Created | null>(null);

  const load = useCallback(async () => {
    try {
      setMembers(await api<Member[]>("/api/workspace/members"));
      if (isOwner) setInvites(await api<Invitation[]>("/api/workspace/invitations"));
    } catch (e) {
      setError(errorText(e));
    }
  }, [isOwner]);

  useEffect(() => {
    load();
  }, [load]);

  async function run(action: () => Promise<unknown>) {
    setError("");
    try {
      await action();
      await load();
    } catch (e) {
      setError(errorText(e));
    }
  }

  async function createUser(e: React.FormEvent) {
    e.preventDefault();
    setCreated(null);
    await run(async () => {
      const r = await api<{ email: string; tempPassword: string }>("/api/workspace/members", {
        method: "POST",
        body: { email: newEmail, role: newRole, password: newPassword || undefined },
      });
      setCreated({ email: r.email, tempPassword: r.tempPassword });
      setNewEmail("");
      setNewPassword("");
    });
  }

  async function resetPassword(m: Member) {
    if (!window.confirm(`إعادة تعيين كلمة سر ${m.email}؟ بنسجّل خروجه من كل الأجهزة.`)) return;
    setCreated(null);
    await run(async () => {
      const r = await api<{ tempPassword: string }>(`/api/workspace/members/${m.userId}/reset-password`, { method: "POST", body: {} });
      setCreated({ email: m.email, tempPassword: r.tempPassword, resetFor: true });
    });
  }

  async function invite(e: React.FormEvent) {
    e.preventDefault();
    setLink("");
    await run(async () => {
      const created = await api<{ code: string; email: string }>("/api/workspace/invitations", {
        method: "POST",
        body: { email, role },
      });
      setLink(`${window.location.origin}/register?invite=${encodeURIComponent(created.code)}&email=${encodeURIComponent(created.email)}`);
      setEmail("");
    });
  }

  return (
    <div className="max-w-3xl space-y-8">
      <h1 className="text-2xl font-bold">الفريق والصلاحيات</h1>
      {error && <p role="alert" className="text-sm text-warn">{error}</p>}

      <section className="space-y-2">
        <h2 className="font-semibold">الأعضاء</h2>
        <ul className="divide-y divide-border rounded-xl border border-border bg-card">
          {members.map((m) => (
            <li key={m.userId} className="flex flex-wrap items-center justify-between gap-2 p-3">
              <span dir="ltr" className="text-sm">{m.email}</span>
              {isOwner ? (
                <span className="flex items-center gap-2">
                  <select
                    className="h-8 rounded-lg border border-border bg-background px-2 text-sm"
                    value={m.role}
                    onChange={(e) => run(() => api(`/api/workspace/members/${m.userId}/role`, { method: "PUT", body: { role: e.target.value } }))}
                  >
                    {roles.map((r) => (
                      <option key={r} value={r}>{roleLabel[r]}</option>
                    ))}
                  </select>
                  {m.managed && (
                    <Button variant="outline" size="sm" onClick={() => resetPassword(m)}>
                      كلمة سر جديدة
                    </Button>
                  )}
                  <Button
                    variant="outline"
                    size="sm"
                    onClick={() => window.confirm(`إزالة ${m.email}؟`) && run(() => api(`/api/workspace/members/${m.userId}`, { method: "DELETE" }))}
                  >
                    إزالة
                  </Button>
                </span>
              ) : (
                <span className="text-sm text-muted-foreground">{roleLabel[m.role] ?? m.role}</span>
              )}
            </li>
          ))}
        </ul>
      </section>

      {isOwner ? (
        <section className="space-y-3">
          <h2 className="font-semibold">إنشاء مستخدم</h2>
          <p className="text-sm text-muted-foreground">بتكتب الإيميل والدور، وبنعطيك كلمة سر مؤقتة (أو اكتب أنت وحدة). أول ما يدخل لازم يغيّرها.</p>
          <form onSubmit={createUser} className="flex flex-wrap gap-2">
            <Input className="max-w-xs" type="email" required dir="ltr" placeholder="email@example.com" value={newEmail} onChange={(e) => setNewEmail(e.target.value)} />
            <select className="h-10 rounded-lg border border-border bg-background px-2 text-sm" value={newRole} onChange={(e) => setNewRole(e.target.value)}>
              {roles.map((r) => (
                <option key={r} value={r}>{roleLabel[r]}</option>
              ))}
            </select>
            <Input className="max-w-48" dir="ltr" autoComplete="off" placeholder="كلمة سر (اختياري)" value={newPassword} onChange={(e) => setNewPassword(e.target.value)} />
            <Button type="submit">إنشاء</Button>
          </form>
          {created && (
            <div className="space-y-1 rounded-xl border border-primary bg-card p-3 text-sm">
              <p>{created.resetFor ? "كلمة السر الجديدة" : "انعمل المستخدم"} — سلّمها للشخص هلأ، ما رح تظهر مرة ثانية:</p>
              <p dir="ltr" className="text-start">{created.email}</p>
              <p dir="ltr" className="select-all text-start font-mono text-primary">{created.tempPassword}</p>
            </div>
          )}
        </section>
      ) : (
        <p className="text-sm text-muted-foreground">الدعوات وتغيير الصلاحيات للمالك فقط.</p>
      )}

      {isOwner && (
        <section className="space-y-3">
          <h2 className="font-semibold">دعوة عضو (برابط، لشخص بيسجّل بنفسه)</h2>
          <form onSubmit={invite} className="flex flex-wrap gap-2">
            <Input className="max-w-xs" type="email" required dir="ltr" placeholder="email@example.com" value={email} onChange={(e) => setEmail(e.target.value)} />
            <select className="h-10 rounded-lg border border-border bg-background px-2 text-sm" value={role} onChange={(e) => setRole(e.target.value)}>
              {roles.map((r) => (
                <option key={r} value={r}>{roleLabel[r]}</option>
              ))}
            </select>
            <Button type="submit">إنشاء دعوة</Button>
          </form>
          {link && (
            <div className="space-y-1 rounded-xl border border-border bg-card p-3 text-sm">
              <p>أرسل هالرابط للشخص (بيظهر مرة وحدة، صالح 7 أيام):</p>
              <p dir="ltr" className="break-all text-primary">{link}</p>
            </div>
          )}
          {invites.length > 0 && (
            <ul className="divide-y divide-border rounded-xl border border-border bg-card">
              {invites.map((i) => (
                <li key={i.id} className="flex flex-wrap items-center justify-between gap-2 p-3 text-sm">
                  <span dir="ltr">{i.email}</span>
                  <span className="flex items-center gap-2">
                    <span className="text-muted-foreground">{roleLabel[i.role] ?? i.role} — بانتظار القبول</span>
                    <Button variant="outline" size="sm" onClick={() => run(() => api(`/api/workspace/invitations/${i.id}`, { method: "DELETE" }))}>
                      إلغاء
                    </Button>
                  </span>
                </li>
              ))}
            </ul>
          )}
        </section>
      )}
    </div>
  );
}
