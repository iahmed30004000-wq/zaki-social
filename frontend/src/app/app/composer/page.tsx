"use client";

import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { useAuth } from "@/components/auth-provider";
import { Button } from "@/components/ui/button";
import { MediaThumb } from "@/components/media-thumb";
import { Input } from "@/components/ui/input";
import { api, errorText } from "@/lib/api";

type Account = { id: string; platform: string; displayName: string; status: string };
type Target = { id: string; accountName: string; platform: string; status: string; error: string | null };
type Post = { id: string; body: string; status: string; aiGenerated: boolean; approvedAt: string | null; createdAt: string; targets: Target[] };
type MediaItem = { id: string; kind: string; originalName: string; status: string; aiGenerated: boolean; approvedAt: string | null };
type Prompt = { id: string; name: string; category: string | null; task: string; body: string; variables: string[]; isActive: boolean };

const statusNames: Record<string, string> = {
  draft: "مسودة",
  scheduled: "مجدول",
  publishing: "جاري النشر",
  published: "تم النشر",
  failed: "فشل",
  pending: "بالانتظار",
};

const aiTasks = ["caption", "ideas", "rewrite"];

export default function ComposerPage() {
  const { session } = useAuth();
  const canEdit = session?.workspace.role !== "viewer";
  const [accounts, setAccounts] = useState<Account[]>([]);
  const [posts, setPosts] = useState<Post[]>([]);
  const [prompts, setPrompts] = useState<Prompt[]>([]);
  const [media, setMedia] = useState<MediaItem[]>([]);
  const [mediaIds, setMediaIds] = useState<string[]>([]);
  const [body, setBody] = useState("");
  const [fromAi, setFromAi] = useState(false);
  const [selected, setSelected] = useState<string[]>([]);
  const [promptName, setPromptName] = useState("");
  const [values, setValues] = useState<Record<string, string>>({});
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
    api<MediaItem[]>("/api/media").then((m) => setMedia(m.filter((x) => x.status === "ready" && (!x.aiGenerated || x.approvedAt)))).catch(() => undefined);
    api<Prompt[]>("/api/prompts")
      .then((p) => {
        const usable = p.filter((x) => x.isActive && aiTasks.includes(x.task));
        setPrompts(usable);
        if (usable[0]) setPromptName(usable[0].name);
      })
      .catch(() => undefined);
    loadPosts();
    return () => {
      if (timer.current) clearTimeout(timer.current);
    };
  }, [loadPosts]);

  const current = useMemo(() => prompts.find((p) => p.name === promptName), [prompts, promptName]);
  const toggle = (id: string) => setSelected((s) => (s.includes(id) ? s.filter((x) => x !== id) : [...s, id]));
  const toggleMedia = (id: string) => setMediaIds((s) => (s.includes(id) ? s.filter((x) => x !== id) : [...s, id]));

  async function guard(action: () => Promise<void>) {
    setError("");
    setBusy(true);
    try {
      await action();
    } catch (e) {
      const code = (e as { code?: string }).code;
      setError(
        code === "no_targets" ? "اختر حساب واحد على الأقل للنشر"
        : code === "media_not_ready" ? "الوسائط لسا بتتعالج أو فشلت معالجتها، شوف مكتبة الوسائط"
        : code === "variable_missing" ? "عبّي كل متغيرات البرومبت أول"
        : code === "ai_not_configured" ? "الذكاء الاصطناعي مش مفعّل: أضف مفتاح مزوّد واحد على الأقل بإعدادات السيرفر"
        : code === "ai_unavailable" ? "كل مزوّدي الذكاء الاصطناعي ما ردّوا، جرّب بعد شوي"
        : code === "ai_quota_exceeded" ? "وصلت حد الاستهلاك الشهري للذكاء الاصطناعي"
        : errorText(e),
      );
    } finally {
      setBusy(false);
    }
  }

  const generate = () =>
    guard(async () => {
      if (!current) return;
      const r = await api<{ text: string }>(`/api/prompts/by-name/${current.name}/generate`, { method: "POST", body: { variables: values } });
      setBody(r.text);
      setFromAi(true); // لازم اعتماد بشري قبل النشر
    });

  const save = (publish: boolean) =>
    guard(async () => {
      const post = await api<Post>("/api/posts", { method: "POST", body: { body, accountIds: selected, aiGenerated: fromAi, mediaIds } });
      // ضغطة "اعتمد وانشر" هي الاعتماد البشري.
      if (publish) {
        if (post.aiGenerated) await api(`/api/posts/${post.id}/approve`, { method: "POST" });
        await api(`/api/posts/${post.id}/publish`, { method: "POST" });
      }
      setBody("");
      setFromAi(false);
      setSelected([]);
      setMediaIds([]);
      await loadPosts();
    });

  const approve = (id: string) => guard(async () => { await api(`/api/posts/${id}/approve`, { method: "POST" }); await loadPosts(); });
  const publishDraft = (id: string) => guard(async () => { await api(`/api/posts/${id}/publish`, { method: "POST" }); await loadPosts(); });

  return (
    <div className="max-w-3xl space-y-8">
      <h1 className="text-2xl font-bold">المُؤلِّف</h1>
      {error && <p role="alert" className="text-sm text-warn">{error}</p>}

      {canEdit ? (
        <section className="space-y-4 rounded-xl border border-border bg-card p-4">
          {prompts.length > 0 && (
            <div className="space-y-2 rounded-lg bg-muted p-3">
              <p className="text-sm font-medium">توليد بالذكاء الاصطناعي</p>
              <select className="h-9 w-full rounded-lg border border-border bg-background px-2 text-sm" value={promptName} onChange={(e) => { setPromptName(e.target.value); setValues({}); }}>
                {prompts.map((p) => <option key={p.id} value={p.name}>{p.name}</option>)}
              </select>
              <div className="grid gap-2 sm:grid-cols-2">
                {current?.variables.map((v) => (
                  <label key={v} className="space-y-1 text-xs">
                    <span dir="ltr">{v}</span>
                    <Input value={values[v] ?? ""} onChange={(e) => setValues({ ...values, [v]: e.target.value })} />
                  </label>
                ))}
              </div>
              <Button size="sm" variant="outline" disabled={busy} onClick={generate}>{busy ? "لحظة..." : "ولّد النص"}</Button>
            </div>
          )}

          <textarea
            className="min-h-32 w-full rounded-lg border border-border bg-background p-3 text-sm focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary"
            placeholder="اكتب المنشور هون..."
            value={body}
            onChange={(e) => setBody(e.target.value)}
          />
          {fromAi && <p className="text-xs text-warn">هالنص مولّد بالذكاء الاصطناعي: راجعه وعدّله قبل ما تعتمده وتنشره.</p>}

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
          {media.length > 0 && (
            <fieldset className="space-y-1">
              <legend className="text-sm text-muted-foreground">أرفق وسائط (فيسبوك: فيديو واحد أو حتى 10 صور):</legend>
              <div className="flex flex-wrap gap-2">
                {media.map((m) => (
                  <button key={m.id} type="button" onClick={() => toggleMedia(m.id)} title={m.originalName}
                    className={`overflow-hidden rounded-lg border-2 ${mediaIds.includes(m.id) ? "border-primary" : "border-transparent"}`}>
                    <MediaThumb id={m.id} kind={m.kind} className="h-16 w-16" />
                  </button>
                ))}
              </div>
            </fieldset>
          )}
          <div className="flex gap-2">
            <Button disabled={busy || !body.trim()} onClick={() => save(true)}>{fromAi ? "اعتمد وانشر" : "نشر الآن"}</Button>
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
              {p.aiGenerated && <p className="text-xs text-muted-foreground">مولّد بالذكاء الاصطناعي — {p.approvedAt ? "معتمد ✓" : "بانتظار الاعتماد"}</p>}
              <ul className="space-y-1 text-xs text-muted-foreground">
                {p.targets.map((t) => (
                  <li key={t.id}>
                    {t.accountName} — {statusNames[t.status] ?? t.status}
                    {t.error && <span className="text-warn"> ({t.error})</span>}
                  </li>
                ))}
              </ul>
              {canEdit && (p.status === "draft" || p.status === "failed") && (
                <div className="flex gap-2">
                  {p.aiGenerated && !p.approvedAt && p.status === "draft" && (
                    <Button size="sm" variant="outline" disabled={busy} onClick={() => approve(p.id)}>اعتماد</Button>
                  )}
                  {(!p.aiGenerated || p.approvedAt) && (
                    <Button size="sm" variant="outline" disabled={busy} onClick={() => publishDraft(p.id)}>
                      {p.status === "failed" ? "إعادة المحاولة" : "نشر"}
                    </Button>
                  )}
                </div>
              )}
            </li>
          ))}
        </ul>
      </section>
    </div>
  );
}
