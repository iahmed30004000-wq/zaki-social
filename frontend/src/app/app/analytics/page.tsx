"use client";

import { useCallback, useEffect, useState } from "react";
import { useAuth } from "@/components/auth-provider";
import { Button } from "@/components/ui/button";
import { api, errorText } from "@/lib/api";

type Summary = {
  days: number;
  totals: { posts: number; reactions: number; comments: number; shares: number; views: number };
  byPlatform: { platform: string; posts: number; reactions: number; comments: number }[];
  byDay: { date: string; posts: number; reactions: number }[];
  top: { postId: string; body: string; platform: string; account: string; reactions: number; comments: number }[];
};

const platformNames: Record<string, string> = { facebook: "فيسبوك", instagram: "إنستغرام", tiktok: "تيك توك", x: "إكس", youtube: "يوتيوب", linkedin: "لينكدإن" };

export default function AnalyticsPage() {
  const { session } = useAuth();
  const canEdit = session?.workspace.role !== "viewer";
  const [days, setDays] = useState(30);
  const [data, setData] = useState<Summary | null>(null);
  const [advice, setAdvice] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");

  const load = useCallback(async () => {
    try {
      setData(await api<Summary>(`/api/analytics/summary?days=${days}`));
    } catch (e) {
      setError(errorText(e));
    }
  }, [days]);

  useEffect(() => {
    load();
  }, [load]);

  async function recommend() {
    setError("");
    setBusy(true);
    setAdvice("");
    try {
      const r = await api<{ text: string }>("/api/analytics/recommendations", { method: "POST", body: { days } });
      setAdvice(r.text);
    } catch (e) {
      const code = (e as { code?: string }).code;
      setError(code === "ai_not_configured" ? "الذكاء الاصطناعي مش مفعّل: أضف مفتاح مزوّد بإعدادات السيرفر" : errorText(e));
    } finally {
      setBusy(false);
    }
  }

  const maxDay = Math.max(1, ...(data?.byDay.map((d) => d.reactions) ?? [1]));

  return (
    <div className="max-w-4xl space-y-6">
      <header className="flex flex-wrap items-center justify-between gap-3">
        <h1 className="text-2xl font-bold">التحليلات</h1>
        <select className="h-9 rounded-lg border border-border bg-background px-2 text-sm" value={days} onChange={(e) => setDays(Number(e.target.value))}>
          <option value={7}>آخر 7 أيام</option>
          <option value={30}>آخر 30 يوم</option>
          <option value={90}>آخر 90 يوم</option>
        </select>
      </header>
      {error && <p role="alert" className="text-sm text-warn">{error}</p>}

      {data && (
        <>
          <ul className="grid grid-cols-2 gap-3 sm:grid-cols-5">
            {[
              ["منشورات", data.totals.posts],
              ["تفاعلات", data.totals.reactions],
              ["تعليقات", data.totals.comments],
              ["مشاركات", data.totals.shares],
              ["مشاهدات", data.totals.views],
            ].map(([label, value]) => (
              <li key={label as string} className="rounded-xl border border-border bg-card p-3 text-center">
                <p className="text-2xl font-bold">{value}</p>
                <p className="text-xs text-muted-foreground">{label}</p>
              </li>
            ))}
          </ul>

          <section className="space-y-2 rounded-xl border border-border bg-card p-4">
            <h2 className="font-semibold">التفاعل اليومي</h2>
            {data.byDay.length === 0 ? (
              <p className="text-sm text-muted-foreground">ما في بيانات بعد. المقاييس بتنجمع تلقائياً كل 15 دقيقة من المنشورات المنشورة.</p>
            ) : (
              <svg viewBox={`0 0 ${Math.max(data.byDay.length, 1) * 24} 110`} className="h-32 w-full" role="img" aria-label="التفاعل اليومي">
                {data.byDay.map((d, i) => {
                  const h = (d.reactions / maxDay) * 80;
                  return (
                    <g key={d.date}>
                      <rect x={i * 24 + 4} y={90 - h} width={16} height={Math.max(h, 1)} rx={3} fill="hsl(var(--primary))" />
                      <text x={i * 24 + 12} y={105} fontSize={8} textAnchor="middle" fill="hsl(var(--muted-foreground))">{d.date.slice(5)}</text>
                    </g>
                  );
                })}
              </svg>
            )}
          </section>

          <section className="space-y-2 rounded-xl border border-border bg-card p-4">
            <h2 className="font-semibold">حسب المنصة</h2>
            <ul className="divide-y divide-border text-sm">
              {data.byPlatform.map((p) => (
                <li key={p.platform} className="flex justify-between py-2">
                  <span>{platformNames[p.platform] ?? p.platform}</span>
                  <span className="text-muted-foreground">{p.posts} منشور · {p.reactions} تفاعل · {p.comments} تعليق</span>
                </li>
              ))}
              {data.byPlatform.length === 0 && <li className="py-2 text-muted-foreground">ما في بيانات.</li>}
            </ul>
          </section>

          <section className="space-y-2 rounded-xl border border-border bg-card p-4">
            <h2 className="font-semibold">أفضل المنشورات</h2>
            <ul className="divide-y divide-border text-sm">
              {data.top.map((t) => (
                <li key={t.postId + t.account} className="py-2">
                  <p className="line-clamp-2">{t.body}</p>
                  <p className="text-xs text-muted-foreground">{platformNames[t.platform] ?? t.platform} · {t.account} · {t.reactions} تفاعل · {t.comments} تعليق</p>
                </li>
              ))}
            </ul>
          </section>

          {canEdit && (
            <section className="space-y-2 rounded-xl border border-border bg-card p-4">
              <h2 className="font-semibold">توصيات الذكاء الاصطناعي</h2>
              <Button variant="outline" disabled={busy} onClick={recommend}>{busy ? "لحظة..." : "اعطني توصيات"}</Button>
              {advice && <p className="whitespace-pre-wrap text-sm">{advice}</p>}
            </section>
          )}
        </>
      )}
    </div>
  );
}
