"use client";

import { useCallback, useEffect, useMemo, useState } from "react";
import { useAuth } from "@/components/auth-provider";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { api, errorText } from "@/lib/api";

type Prompt = {
  id: string;
  name: string;
  category: string | null;
  task: string;
  body: string;
  variables: string[];
  preferredAi: string | null;
  version: number;
  isActive: boolean;
};
type Detail = { prompt: Prompt; versions: { version: number; body: string; createdAt: string }[] };
type RunResult = { text: string; provider: string; model: string; fellBack: boolean };

const tasks: Record<string, string> = {
  caption: "كابشن / منشور",
  ideas: "أفكار",
  rewrite: "إعادة صياغة",
  analysis: "تحليل",
  classify: "تصنيف",
  hashtags: "هاشتاغات",
  summarize: "تلخيص",
  reply: "رد",
};
const ais: Record<string, string> = { "": "تلقائي (حسب سياسة التوجيه)", claude: "Claude", openai: "OpenAI", local: "النموذج الداخلي" };

export default function PromptsPage() {
  const { session } = useAuth();
  const canEdit = session?.workspace.role !== "viewer";
  const [list, setList] = useState<Prompt[]>([]);
  const [detail, setDetail] = useState<Detail | null>(null);
  const [draft, setDraft] = useState({ name: "", category: "content", task: "caption", body: "", preferredAi: "" });
  const [creating, setCreating] = useState(false);
  const [values, setValues] = useState<Record<string, string>>({});
  const [result, setResult] = useState<RunResult | null>(null);
  const [error, setError] = useState("");
  const [busy, setBusy] = useState(false);

  const load = useCallback(async () => {
    try {
      setList(await api<Prompt[]>("/api/prompts"));
    } catch (e) {
      setError(errorText(e));
    }
  }, []);

  useEffect(() => {
    load();
  }, [load]);

  const open = useCallback(async (id: string) => {
    setError("");
    setResult(null);
    setCreating(false);
    try {
      const d = await api<Detail>(`/api/prompts/${id}`);
      setDetail(d);
      setDraft({ name: d.prompt.name, category: d.prompt.category ?? "", task: d.prompt.task, body: d.prompt.body, preferredAi: d.prompt.preferredAi ?? "" });
      setValues({});
    } catch (e) {
      setError(errorText(e));
    }
  }, []);

  const variables = useMemo(() => [...new Set([...draft.body.matchAll(/\{\{\s*([A-Za-z_][A-Za-z0-9_]*)\s*\}\}/g)].map((m) => m[1]))], [draft.body]);

  async function run(action: () => Promise<unknown>) {
    setError("");
    setBusy(true);
    try {
      await action();
    } catch (e) {
      const code = (e as { code?: string }).code;
      setError(
        code === "variable_missing" ? "عبّي كل المتغيرات أول" : code === "name_invalid" ? "الاسم: أحرف إنجليزية صغيرة وأرقام و _ فقط (يبدأ بحرف)" : errorText(e),
      );
    } finally {
      setBusy(false);
    }
  }

  const save = () =>
    run(async () => {
      const body = { category: draft.category || null, task: draft.task, body: draft.body, preferredAi: draft.preferredAi || null };
      if (creating) {
        const created = await api<Prompt>("/api/prompts", { method: "POST", body: { ...body, name: draft.name } });
        await load();
        await open(created.id);
      } else if (detail) {
        await api(`/api/prompts/${detail.prompt.id}`, { method: "PUT", body });
        await load();
        await open(detail.prompt.id);
      }
    });

  const test = () =>
    run(async () => {
      if (!detail) return;
      setResult(await api<RunResult>(`/api/prompts/${detail.prompt.id}/test`, { method: "POST", body: { variables: values } }));
    });

  const newPrompt = () => {
    setCreating(true);
    setDetail(null);
    setResult(null);
    setDraft({ name: "", category: "content", task: "caption", body: "", preferredAi: "" });
  };

  const showEditor = creating || detail;

  return (
    <div className="max-w-5xl space-y-4">
      <header className="flex flex-wrap items-center justify-between gap-3">
        <h1 className="text-2xl font-bold">مركز البرومبتات</h1>
        {canEdit && <Button onClick={newPrompt}>＋ برومبت جديد</Button>}
      </header>
      <p className="text-sm text-muted-foreground">
        كل تعليمات الذكاء الاصطناعي هون. عدّل أي برومبت واحفظ، وبتتغير النتائج فوراً بدون أي تحديث للنظام. استعمل <span dir="ltr">{"{{اسم}}"}</span> للمتغيرات (بحروف إنجليزية).
      </p>
      {error && <p role="alert" className="text-sm text-warn">{error}</p>}

      <div className="grid gap-4 md:grid-cols-[16rem_1fr]">
        <ul className="max-h-[70vh] space-y-1 overflow-y-auto rounded-xl border border-border bg-card p-2">
          {list.map((p) => (
            <li key={p.id}>
              <button
                onClick={() => open(p.id)}
                className={`w-full rounded-lg px-3 py-2 text-start text-sm hover:bg-muted ${detail?.prompt.id === p.id ? "bg-muted font-semibold" : ""} ${p.isActive ? "" : "opacity-50"}`}
              >
                <span dir="ltr" className="block">{p.name}</span>
                <span className="text-xs text-muted-foreground">{tasks[p.task] ?? p.task} · v{p.version}{p.isActive ? "" : " · موقوف"}</span>
              </button>
            </li>
          ))}
        </ul>

        {showEditor ? (
          <section className="space-y-4">
            <div className="space-y-3 rounded-xl border border-border bg-card p-4">
              {creating ? (
                <label className="block space-y-1 text-sm">
                  <span>الاسم (إنجليزي)</span>
                  <Input dir="ltr" value={draft.name} onChange={(e) => setDraft({ ...draft, name: e.target.value })} placeholder="my_caption" />
                </label>
              ) : (
                <p dir="ltr" className="text-sm font-semibold">{detail?.prompt.name} <span className="text-muted-foreground">v{detail?.prompt.version}</span></p>
              )}
              <div className="flex flex-wrap gap-3">
                <label className="space-y-1 text-sm">
                  <span>نوع المهمة</span>
                  <select className="block h-10 rounded-lg border border-border bg-background px-2" value={draft.task} onChange={(e) => setDraft({ ...draft, task: e.target.value })} disabled={!canEdit}>
                    {Object.entries(tasks).map(([k, v]) => <option key={k} value={k}>{v}</option>)}
                  </select>
                </label>
                <label className="space-y-1 text-sm">
                  <span>المزوّد المفضّل</span>
                  <select className="block h-10 rounded-lg border border-border bg-background px-2" value={draft.preferredAi} onChange={(e) => setDraft({ ...draft, preferredAi: e.target.value })} disabled={!canEdit}>
                    {Object.entries(ais).map(([k, v]) => <option key={k} value={k}>{v}</option>)}
                  </select>
                </label>
              </div>
              <textarea
                className="min-h-48 w-full rounded-lg border border-border bg-background p-3 text-sm focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary"
                value={draft.body}
                readOnly={!canEdit}
                onChange={(e) => setDraft({ ...draft, body: e.target.value })}
              />
              {canEdit && (
                <div className="flex flex-wrap gap-2">
                  <Button disabled={busy || !draft.body.trim()} onClick={save}>{creating ? "إنشاء" : "حفظ (إصدار جديد)"}</Button>
                  {detail && !creating && (
                    <Button variant="outline" disabled={busy} onClick={() => run(async () => { await api(`/api/prompts/${detail.prompt.id}/active`, { method: "POST", body: { isActive: !detail.prompt.isActive } }); await load(); await open(detail.prompt.id); })}>
                      {detail.prompt.isActive ? "إيقاف" : "تفعيل"}
                    </Button>
                  )}
                </div>
              )}
            </div>

            {detail && canEdit && (
              <div className="space-y-3 rounded-xl border border-border bg-card p-4">
                <h2 className="font-semibold">تجربة</h2>
                {variables.length === 0 && <p className="text-sm text-muted-foreground">ما في متغيرات.</p>}
                <div className="grid gap-2 sm:grid-cols-2">
                  {variables.map((v) => (
                    <label key={v} className="space-y-1 text-sm">
                      <span dir="ltr">{v}</span>
                      <Input value={values[v] ?? ""} onChange={(e) => setValues({ ...values, [v]: e.target.value })} />
                    </label>
                  ))}
                </div>
                <Button variant="outline" disabled={busy} onClick={test}>جرّب البرومبت المحفوظ</Button>
                {result && (
                  <div className="space-y-1 rounded-lg bg-muted p-3 text-sm">
                    <p className="whitespace-pre-wrap">{result.text}</p>
                    <p className="text-xs text-muted-foreground">المزوّد: {result.provider}{result.fellBack ? " (تحويل تلقائي)" : ""}</p>
                  </div>
                )}
              </div>
            )}

            {detail && (
              <div className="space-y-2 rounded-xl border border-border bg-card p-4">
                <h2 className="font-semibold">الإصدارات</h2>
                <ul className="divide-y divide-border">
                  {detail.versions.map((v) => (
                    <li key={v.version} className="flex items-center justify-between gap-2 py-2 text-sm">
                      <span>
                        v{v.version} <span className="text-xs text-muted-foreground">{new Date(v.createdAt).toLocaleString("ar")}</span>
                        <span className="line-clamp-1 block text-xs text-muted-foreground">{v.body}</span>
                      </span>
                      {canEdit && v.version !== detail.prompt.version && (
                        <Button size="sm" variant="outline" disabled={busy} onClick={() => run(async () => { await api(`/api/prompts/${detail.prompt.id}/rollback`, { method: "POST", body: { version: v.version } }); await load(); await open(detail.prompt.id); })}>
                          رجوع لهالإصدار
                        </Button>
                      )}
                    </li>
                  ))}
                </ul>
              </div>
            )}
          </section>
        ) : (
          <p className="text-muted-foreground">اختر برومبت من القائمة.</p>
        )}
      </div>
    </div>
  );
}
