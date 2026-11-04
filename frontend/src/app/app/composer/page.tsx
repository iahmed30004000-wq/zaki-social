"use client";

import { useCallback, useEffect, useRef, useState } from "react";
import { useAuth } from "@/components/auth-provider";
import { Button } from "@/components/ui/button";
import { api, errorText } from "@/lib/api";

type Account = { id: string; platform: string; displayName: string; status: string };
type Target = { id: string; accountName: string; platform: string; status: string; error: string | null };
type Post = { id: string; body: string; status: string; createdAt: string; targets: Target[] };

const statusNames: Record<string, string> = {
  draft: "مسودة",
  scheduled: "مجدول",
  publishing: "جاري النشر",
  published: "تم النشر",
  failed: "فشل",
  pending: "بالانتظار",
};

export default function ComposerPage() {
  const { session } = useAuth();
  const canEdit = session?.workspace.role !== "viewer";
  const [accounts, setAccounts] = useState<Account[]>([]);
  const [posts, setPosts] = useState<Post[]>([]);
  const [body, setBody] = useState("");
  const [selected, setSelected] = useState<string[]>([]);
  const [error, setError] = useState("");
  const [busy, setBusy] = useState(false);
  const timer = useRef<ReturnType<typeof setTimeout> | null>(null);

  const loadPosts = useCallback(async () => {
    try {
      const list = await api<Post[]>("/api/posts");
      setPosts(list);
      if (timer.current) clearTimeout(timer.current);
      // نتابع الحالة كل 3 ثواني لسا في منشور قيد النشر.
      if (list.some((p) => p.status === "publishing")) timer.current = setTimeout(loadPosts, 3000);
    } catch (e) {
      setError(errorText(e));
    }
  }, []);

  useEffect(() => {
    api<Account[]>("/api/accounts").then((a) => setAccounts(a.filter((x) => x.status === "active"))).catch((e) => setError(errorText(e)));
    loadPosts();
    return () => {
      if (timer.current) clearTimeout(timer.current);
    };
  }, [loadPosts]);

  const toggle = (id: string) => setSelected((s) => (s.includes(id) ? s.filter((x) => x !== id) : [...s, id]));

  async function save(publish: boolean) {
    setError("");
    setBusy(true);
    try {
      const post = await api<Post>("/api/posts", { method: "POST", body: { body, accountIds: selected } });
      if (publish) await api(`/api/posts/${post.id}/publish`, { method: "POST" });
      setBody("");
      setSelected([]);
      await loadPosts();
    } catch (e) {
      const code = (e as { code?: string }).code;
      setError(code === "no_targets" ? "اختر حساب واحد على الأقل للنشر" : errorText(e));
    } finally {
      setBusy(false);
    }
  }

  async function publishDraft(id: string) {
    setError("");
    try {
      await api(`/api/posts/${id}/publish`, { method: "POST" });
      await loadPosts();
    } catch (e) {
      const code = (e as { code?: string }).code;
      setError(code === "no_targets" ? "المسودة بدون حسابات" : errorText(e));
    }
  }

  return (
    <div className="max-w-3xl space-y-8">
      <h1 className="text-2xl font-bold">المُؤلِّف</h1>
      {error && <p role="alert" className="text-sm text-warn">{error}</p>}

      {canEdit ? (
        <section className="space-y-3 rounded-xl border border-border bg-card p-4">
          <textarea
            className="min-h-32 w-full rounded-lg border border-border bg-background p-3 text-sm focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary"
            placeholder="اكتب المنشور هون..."
            value={body}
            onChange={(e) => setBody(e.target.value)}
          />
          <fieldset className="space-y-1">
            <legend className="text-sm text-muted-foreground">انشر على:</legend>
            {accounts.length === 0 && <p className="text-sm text-muted-foreground">ما في حسابات مربوطة. اربط حساب من «إدارة الحسابات».</p>}
            {accounts.map((a) => (
              <label key={a.id} className="flex items-center gap-2 text-sm">
                <input type="checkbox" checked={selected.includes(a.id)} onChange={() => toggle(a.id)} />
                {a.displayName} <span className="text-xs text-muted-foreground">({a.platform})</span>
              </label>
            ))}
          </fieldset>
          <div className="flex gap-2">
            <Button disabled={busy || !body.trim()} onClick={() => save(true)}>نشر الآن</Button>
            <Button variant="outline" disabled={busy || !body.trim()} onClick={() => save(false)}>حفظ كمسودة</Button>
          </div>
        </section>
      ) : (
        <p className="text-sm text-muted-foreground">صلاحية المشاهد ما بتسمح بإنشاء منشورات.</p>
      )}

      <section className="space-y-2">
        <h2 className="font-semibold">آخر المنشورات</h2>
        {posts.length === 0 && <p className="text-sm text-muted-foreground">ما في منشورات بعد.</p>}
        <ul className="space-y-2">
          {posts.map((p) => (
            <li key={p.id} className="space-y-2 rounded-xl border border-border bg-card p-3">
              <div className="flex items-start justify-between gap-3">
                <p className="whitespace-pre-wrap text-sm">{p.body}</p>
                <span className={p.status === "failed" ? "shrink-0 text-sm text-warn" : "shrink-0 text-sm text-ok"}>{statusNames[p.status] ?? p.status}</span>
              </div>
              <ul className="space-y-1 text-xs text-muted-foreground">
                {p.targets.map((t) => (
                  <li key={t.id}>
                    {t.accountName} — {statusNames[t.status] ?? t.status}
                    {t.error && <span className="text-warn"> ({t.error})</span>}
                  </li>
                ))}
              </ul>
              {canEdit && (p.status === "draft" || p.status === "failed") && (
                <Button size="sm" variant="outline" onClick={() => publishDraft(p.id)}>
                  {p.status === "failed" ? "إعادة المحاولة" : "نشر"}
                </Button>
              )}
            </li>
          ))}
        </ul>
      </section>
    </div>
  );
}
