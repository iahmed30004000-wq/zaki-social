"use client";

import { useCallback, useEffect, useState } from "react";
import { useAuth } from "@/components/auth-provider";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { api, errorText } from "@/lib/api";

type Rule = { id: string; name: string; accountId: string | null; matchPattern: string | null; promptName: string | null; isEnabled: boolean; delayMinSeconds: number; delayMaxSeconds: number };
type Reply = { id: string; commentId: string; status: string; text: string; error: string | null; at: string };
type Proxy = { id: string; name: string; host: string; port: number; hasCredentials: boolean; status: string; lastCheckedAt: string | null; accounts: number };
type Account = { id: string; displayName: string; platform: string };
type Status = { comments: number; replies: number; lastCollectedAt: string | null };

const replyStatus: Record<string, string> = { generating: "قيد التوليد", queued: "بالانتظار", sent: "تم الإرسال", failed: "فشل" };
const proxyStatus: Record<string, string> = { healthy: "سليم", unhealthy: "مش شغّال", unknown: "لسا ما انفحص" };

export default function AutomationsPage() {
  const { session } = useAuth();
  const role = session?.workspace.role ?? "viewer";
  const canEdit = role !== "viewer";
  const isOwner = role === "owner";
  const [rules, setRules] = useState<Rule[]>([]);
  const [replies, setReplies] = useState<Reply[]>([]);
  const [proxies, setProxies] = useState<Proxy[]>([]);
  const [accounts, setAccounts] = useState<Account[]>([]);
  const [status, setStatus] = useState<Status | null>(null);
  const [error, setError] = useState("");
  const [rule, setRule] = useState({ name: "", accountId: "", matchPattern: "", delayMinSeconds: 30, delayMaxSeconds: 300 });
  const [proxy, setProxy] = useState({ name: "", host: "", port: 8080, username: "", password: "" });

  const load = useCallback(async () => {
    try {
      setRules(await api<Rule[]>("/api/automation/rules"));
      setReplies(await api<Reply[]>("/api/automation/replies"));
      setStatus(await api<Status>("/api/automation/status"));
      setAccounts(await api<Account[]>("/api/accounts"));
      if (canEdit) setProxies(await api<Proxy[]>("/api/proxies"));
    } catch (e) {
      setError(errorText(e));
    }
  }, [canEdit]);

  useEffect(() => {
    load();
  }, [load]);

  async function run(action: () => Promise<unknown>) {
    setError("");
    try {
      await action();
      await load();
    } catch (e) {
      const code = (e as { code?: string }).code;
      setError(code === "proxy_host_not_allowed" ? "عنوان البروكسي داخلي/محلي ومش مسموح" : code === "delay_invalid" ? "التأخير: الحد الأدنى ≥ 1 والأعلى ≥ الأدنى" : code === "no_healthy_proxy" ? "ما في بروكسي سليم" : errorText(e));
    }
  }

  const ruleBody = (r: Rule, enabled: boolean) => ({ name: r.name, accountId: r.accountId, matchPattern: r.matchPattern, promptName: r.promptName, isEnabled: enabled, delayMinSeconds: r.delayMinSeconds, delayMaxSeconds: r.delayMaxSeconds });

  return (
    <div className="max-w-4xl space-y-8">
      <h1 className="text-2xl font-bold">الأتمتة</h1>
      {error && <p role="alert" className="text-sm text-warn">{error}</p>}

      <section className="space-y-3 rounded-xl border border-border bg-card p-4">
        <div className="flex flex-wrap items-center justify-between gap-2">
          <h2 className="font-semibold">جامع البيانات</h2>
          {canEdit && <Button size="sm" variant="outline" onClick={() => run(() => api("/api/automation/collect-now", { method: "POST" }))}>اجمع الآن</Button>}
        </div>
        <p className="text-sm text-muted-foreground">
          بيجمع كل 15 دقيقة مقاييس منشوراتك (للتحليلات) وتعليقاتها (للمجيب الآلي). التعليقات المجموعة: {status?.comments ?? 0} · الردود: {status?.replies ?? 0}
        </p>
      </section>

      <section className="space-y-3 rounded-xl border border-border bg-card p-4">
        <h2 className="font-semibold">المجيب الآلي</h2>
        <p className="text-xs text-muted-foreground">كل قاعدة بتنشأ <b>مطفية</b>. لما تفعّلها، الذكاء الاصطناعي بيرد على التعليقات المطابقة بعد تأخير عشوائي بين الحدّين، وبمرة وحدة لكل تعليق. الرد بيتولّد من برومبت «reply_comment» بمركز البرومبتات (عدّله براحتك).</p>
        <ul className="divide-y divide-border text-sm">
          {rules.map((r) => (
            <li key={r.id} className="flex flex-wrap items-center justify-between gap-2 py-2">
              <span>
                <b>{r.name}</b>
                <span className="ms-2 text-xs text-muted-foreground">
                  {r.matchPattern ? `يطابق: ${r.matchPattern}` : "كل التعليقات"} · تأخير {r.delayMinSeconds}–{r.delayMaxSeconds}ث · {r.accountId ? accounts.find((a) => a.id === r.accountId)?.displayName : "كل الحسابات"}
                </span>
              </span>
              {canEdit && (
                <span className="flex gap-2">
                  <Button size="sm" variant={r.isEnabled ? "default" : "outline"} onClick={() => run(() => api(`/api/automation/rules/${r.id}`, { method: "PUT", body: ruleBody(r, !r.isEnabled) }))}>{r.isEnabled ? "مفعّلة" : "مطفية"}</Button>
                  <Button size="sm" variant="outline" onClick={() => run(() => api(`/api/automation/rules/${r.id}`, { method: "DELETE" }))}>حذف</Button>
                </span>
              )}
            </li>
          ))}
          {rules.length === 0 && <li className="py-2 text-muted-foreground">ما في قواعد.</li>}
        </ul>
        {canEdit && (
          <form
            className="flex flex-wrap items-end gap-2"
            onSubmit={(e) => {
              e.preventDefault();
              run(() => api("/api/automation/rules", { method: "POST", body: { name: rule.name, accountId: rule.accountId || null, matchPattern: rule.matchPattern || null, isEnabled: false, delayMinSeconds: rule.delayMinSeconds, delayMaxSeconds: rule.delayMaxSeconds } }));
            }}
          >
            <label className="space-y-1 text-xs"><span>الاسم</span><Input required value={rule.name} onChange={(e) => setRule({ ...rule, name: e.target.value })} /></label>
            <label className="space-y-1 text-xs"><span>يطابق (كلمة أو regex)</span><Input value={rule.matchPattern} onChange={(e) => setRule({ ...rule, matchPattern: e.target.value })} placeholder="سعر|كم" /></label>
            <label className="space-y-1 text-xs"><span>الحساب</span>
              <select className="h-10 rounded-lg border border-border bg-background px-2 text-sm" value={rule.accountId} onChange={(e) => setRule({ ...rule, accountId: e.target.value })}>
                <option value="">كل الحسابات</option>
                {accounts.map((a) => <option key={a.id} value={a.id}>{a.displayName}</option>)}
              </select>
            </label>
            <label className="space-y-1 text-xs"><span>تأخير من (ث)</span><Input type="number" className="w-24" value={rule.delayMinSeconds} onChange={(e) => setRule({ ...rule, delayMinSeconds: Number(e.target.value) })} /></label>
            <label className="space-y-1 text-xs"><span>إلى (ث)</span><Input type="number" className="w-24" value={rule.delayMaxSeconds} onChange={(e) => setRule({ ...rule, delayMaxSeconds: Number(e.target.value) })} /></label>
            <Button type="submit">إضافة قاعدة</Button>
          </form>
        )}
        {replies.length > 0 && (
          <div className="space-y-1">
            <h3 className="text-sm font-medium">آخر الردود</h3>
            <ul className="divide-y divide-border text-xs">
              {replies.slice(0, 10).map((r) => (
                <li key={r.id} className="py-1.5">
                  <span className={r.status === "failed" ? "text-warn" : "text-ok"}>{replyStatus[r.status] ?? r.status}</span> — {r.text || "…"} {r.error && <span className="text-warn">({r.error})</span>}
                </li>
              ))}
            </ul>
          </div>
        )}
      </section>

      {canEdit && (
        <section className="space-y-3 rounded-xl border border-border bg-card p-4">
          <h2 className="font-semibold">البروكسيات</h2>
          <p className="text-xs text-muted-foreground">كل حساب بيتربط ببروكسي ثابت، ونداءات المنصة بتمر منه. بروكسي مش شغّال = النشر بيوقف (ما بيطلع من عنوان السيرفر مباشرة).</p>
          <ul className="divide-y divide-border text-sm">
            {proxies.map((p) => (
              <li key={p.id} className="flex flex-wrap items-center justify-between gap-2 py-2">
                <span dir="ltr">{p.name} — {p.host}:{p.port}{p.hasCredentials ? " 🔒" : ""}</span>
                <span className="flex items-center gap-2">
                  <span className={p.status === "healthy" ? "text-ok" : "text-warn"}>{proxyStatus[p.status] ?? p.status}</span>
                  <span className="text-xs text-muted-foreground">{p.accounts} حساب</span>
                  {isOwner && <Button size="sm" variant="outline" onClick={() => run(() => api(`/api/proxies/${p.id}`, { method: "DELETE" }))}>حذف</Button>}
                </span>
              </li>
            ))}
            {proxies.length === 0 && <li className="py-2 text-muted-foreground">ما في بروكسيات.</li>}
          </ul>
          {isOwner && (
            <>
              <form className="flex flex-wrap items-end gap-2" onSubmit={(e) => { e.preventDefault(); run(() => api("/api/proxies", { method: "POST", body: { name: proxy.name, host: proxy.host, port: proxy.port, username: proxy.username || null, password: proxy.password || null } })); }}>
                <label className="space-y-1 text-xs"><span>الاسم</span><Input required value={proxy.name} onChange={(e) => setProxy({ ...proxy, name: e.target.value })} /></label>
                <label className="space-y-1 text-xs"><span>العنوان</span><Input required dir="ltr" value={proxy.host} onChange={(e) => setProxy({ ...proxy, host: e.target.value })} /></label>
                <label className="space-y-1 text-xs"><span>المنفذ</span><Input required type="number" className="w-24" dir="ltr" value={proxy.port} onChange={(e) => setProxy({ ...proxy, port: Number(e.target.value) })} /></label>
                <label className="space-y-1 text-xs"><span>المستخدم</span><Input dir="ltr" autoComplete="off" value={proxy.username} onChange={(e) => setProxy({ ...proxy, username: e.target.value })} /></label>
                <label className="space-y-1 text-xs"><span>كلمة السر</span><Input type="password" autoComplete="new-password" dir="ltr" value={proxy.password} onChange={(e) => setProxy({ ...proxy, password: e.target.value })} /></label>
                <Button type="submit">إضافة</Button>
              </form>
              <Button variant="outline" onClick={() => run(() => api("/api/proxies/auto-assign", { method: "POST" }))}>اربط الحسابات تلقائياً ببروكسيات سليمة</Button>
            </>
          )}
        </section>
      )}
    </div>
  );
}
