"use client";

import { useCallback, useEffect, useMemo, useState } from "react";
import { useAuth } from "@/components/auth-provider";
import { Button } from "@/components/ui/button";
import { api, errorText } from "@/lib/api";

type Post = { id: string; body: string; status: string; scheduledAt: string | null; repeatRule: string; createdAt: string; aiGenerated: boolean; approvedAt: string | null };

const weekdays = ["الأحد", "الإثنين", "الثلاثاء", "الأربعاء", "الخميس", "الجمعة", "السبت"];
const statusColor: Record<string, string> = { scheduled: "bg-primary text-primary-foreground", published: "bg-ok text-background", draft: "bg-muted", failed: "bg-warn text-background", publishing: "bg-muted" };

const toLocalInput = (iso: string) => {
  const d = new Date(iso);
  const p = (n: number) => String(n).padStart(2, "0");
  return `${d.getFullYear()}-${p(d.getMonth() + 1)}-${p(d.getDate())}T${p(d.getHours())}:${p(d.getMinutes())}`;
};

export default function CalendarPage() {
  const { session } = useAuth();
  const canEdit = session?.workspace.role !== "viewer";
  const [month, setMonth] = useState(() => new Date(new Date().getFullYear(), new Date().getMonth(), 1));
  const [posts, setPosts] = useState<Post[]>([]);
  const [selected, setSelected] = useState<Post | null>(null);
  const [when, setWhen] = useState("");
  const [repeat, setRepeat] = useState("none");
  const [error, setError] = useState("");
  const [best, setBest] = useState("");

  const range = useMemo(() => {
    const start = new Date(month.getFullYear(), month.getMonth(), 1 - month.getDay());
    return { start, end: new Date(start.getFullYear(), start.getMonth(), start.getDate() + 42) };
  }, [month]);

  const load = useCallback(async () => {
    try {
      setPosts(await api<Post[]>(`/api/posts?from=${encodeURIComponent(range.start.toISOString())}&to=${encodeURIComponent(range.end.toISOString())}`));
    } catch (e) {
      setError(errorText(e));
    }
  }, [range]);

  useEffect(() => {
    load();
  }, [load]);

  const days = useMemo(() => Array.from({ length: 42 }, (_, i) => new Date(range.start.getFullYear(), range.start.getMonth(), range.start.getDate() + i)), [range]);
  const dayKey = (d: Date) => `${d.getFullYear()}-${d.getMonth()}-${d.getDate()}`;
  const byDay = useMemo(() => {
    const map = new Map<string, Post[]>();
    for (const p of posts) {
      const k = dayKey(new Date(p.scheduledAt ?? p.createdAt));
      map.set(k, [...(map.get(k) ?? []), p]);
    }
    return map;
  }, [posts]);

  async function act(action: () => Promise<unknown>) {
    setError("");
    try {
      await action();
      setSelected(null);
      await load();
    } catch (e) {
      const code = (e as { code?: string }).code;
      setError(code === "schedule_in_past" ? "الوقت بالماضي" : code === "approval_required" ? "لازم اعتماد المنشور أول (من المؤلّف)" : errorText(e));
    }
  }

  // سحب وإفلات: نفس الساعة، يوم جديد (بتوقيت المتصفح، والخادم بيخزّن UTC)
  function onDrop(day: Date, postId: string) {
    const post = posts.find((p) => p.id === postId);
    if (!post || post.status !== "scheduled" || !post.scheduledAt) return;
    const old = new Date(post.scheduledAt);
    const next = new Date(day.getFullYear(), day.getMonth(), day.getDate(), old.getHours(), old.getMinutes());
    act(() => api(`/api/posts/${post.id}/schedule`, { method: "PUT", body: { when: next.toISOString() } }));
  }

  async function suggest() {
    setError("");
    setBest("لحظة...");
    try {
      const r = await api<{ text: string }>(`/api/schedule/best-time?timezone=${encodeURIComponent(Intl.DateTimeFormat().resolvedOptions().timeZone)}`);
      setBest(r.text);
    } catch (e) {
      setBest("");
      setError(errorText(e));
    }
  }

  function open(p: Post) {
    setSelected(p);
    setWhen(toLocalInput(p.scheduledAt ?? new Date(Date.now() + 3600_000).toISOString()));
    setRepeat(p.repeatRule);
  }

  return (
    <div className="max-w-5xl space-y-4">
      <header className="flex flex-wrap items-center justify-between gap-3">
        <h1 className="text-2xl font-bold">التقويم</h1>
        <div className="flex items-center gap-2">
          <Button size="sm" variant="outline" onClick={() => setMonth(new Date(month.getFullYear(), month.getMonth() - 1, 1))}>السابق</Button>
          <span className="min-w-32 text-center font-medium">{month.toLocaleDateString("ar", { month: "long", year: "numeric" })}</span>
          <Button size="sm" variant="outline" onClick={() => setMonth(new Date(month.getFullYear(), month.getMonth() + 1, 1))}>التالي</Button>
        </div>
      </header>
      {error && <p role="alert" className="text-sm text-warn">{error}</p>}

      {canEdit && (
        <div className="space-y-2 rounded-xl border border-border bg-card p-3 text-sm">
          <Button size="sm" variant="outline" onClick={suggest}>اقترح أفضل وقت للنشر (ذكاء اصطناعي)</Button>
          {best && <p className="whitespace-pre-wrap">{best}</p>}
        </div>
      )}

      <div className="grid grid-cols-7 gap-px overflow-hidden rounded-xl border border-border bg-border text-xs">
        {weekdays.map((w) => <div key={w} className="bg-muted p-2 text-center font-medium">{w}</div>)}
        {days.map((d) => (
          <div
            key={dayKey(d)}
            className={`min-h-24 space-y-1 bg-card p-1 ${d.getMonth() === month.getMonth() ? "" : "opacity-50"}`}
            onDragOver={(e) => canEdit && e.preventDefault()}
            onDrop={(e) => canEdit && onDrop(d, e.dataTransfer.getData("text/plain"))}
          >
            <p className="text-muted-foreground">{d.getDate()}</p>
            {(byDay.get(dayKey(d)) ?? []).map((p) => (
              <button
                key={p.id}
                draggable={canEdit && p.status === "scheduled"}
                onDragStart={(e) => e.dataTransfer.setData("text/plain", p.id)}
                onClick={() => open(p)}
                className={`block w-full truncate rounded px-1 py-0.5 text-start ${statusColor[p.status] ?? "bg-muted"}`}
                title={p.body}
              >
                {p.scheduledAt ? new Date(p.scheduledAt).toLocaleTimeString("ar", { hour: "2-digit", minute: "2-digit" }) + " " : ""}
                {p.body}
              </button>
            ))}
          </div>
        ))}
      </div>

      {selected && (
        <section className="space-y-3 rounded-xl border border-border bg-card p-4">
          <p className="whitespace-pre-wrap text-sm">{selected.body}</p>
          <p className="text-xs text-muted-foreground">الحالة: {selected.status}</p>
          {canEdit && (selected.status === "draft" || selected.status === "scheduled") && (
            <div className="flex flex-wrap items-end gap-2">
              <label className="space-y-1 text-xs">
                <span>الوقت</span>
                <input type="datetime-local" className="block h-9 rounded-lg border border-border bg-background px-2 text-sm" value={when} onChange={(e) => setWhen(e.target.value)} />
              </label>
              <label className="space-y-1 text-xs">
                <span>التكرار</span>
                <select className="block h-9 rounded-lg border border-border bg-background px-2 text-sm" value={repeat} onChange={(e) => setRepeat(e.target.value)}>
                  <option value="none">بدون</option>
                  <option value="daily">يومي</option>
                  <option value="weekly">أسبوعي</option>
                </select>
              </label>
              <Button size="sm" onClick={() => act(() => api(`/api/posts/${selected.id}/schedule`, { method: "PUT", body: { when: new Date(when).toISOString(), repeat } }))}>حفظ الجدولة</Button>
              {selected.status === "scheduled" && (
                <>
                  <Button size="sm" variant="outline" onClick={() => act(() => api(`/api/posts/${selected.id}/schedule`, { method: "DELETE" }))}>إلغاء الجدولة</Button>
                  <Button size="sm" variant="outline" onClick={() => act(() => api(`/api/posts/${selected.id}/publish`, { method: "POST" }))}>انشر الآن</Button>
                </>
              )}
            </div>
          )}
          <Button size="sm" variant="outline" onClick={() => setSelected(null)}>إغلاق</Button>
        </section>
      )}
    </div>
  );
}
