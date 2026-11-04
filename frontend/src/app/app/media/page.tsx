"use client";

import { useCallback, useEffect, useRef, useState } from "react";
import { useAuth } from "@/components/auth-provider";
import { MediaThumb } from "@/components/media-thumb";
import { Button } from "@/components/ui/button";
import { api, errorText, uploadFile } from "@/lib/api";

type Media = {
  id: string;
  kind: string;
  originalName: string;
  status: string;
  error: string | null;
  aiGenerated: boolean;
  approvedAt: string | null;
  sizeBytes: number;
  width: number | null;
  height: number | null;
  durationSeconds: number | null;
};

const statusNames: Record<string, string> = { processing: "قيد المعالجة", ready: "جاهز", failed: "فشل" };
const mb = (n: number) => `${(n / 1024 / 1024).toFixed(1)} MB`;

export default function MediaPage() {
  const { session } = useAuth();
  const canEdit = session?.workspace.role !== "viewer";
  const [items, setItems] = useState<Media[]>([]);
  const [error, setError] = useState("");
  const [uploading, setUploading] = useState(false);
  const input = useRef<HTMLInputElement>(null);
  const timer = useRef<ReturnType<typeof setTimeout> | null>(null);

  const load = useCallback(async () => {
    try {
      const list = await api<Media[]>("/api/media");
      setItems(list);
      if (timer.current) clearTimeout(timer.current);
      if (list.some((m) => m.status === "processing")) timer.current = setTimeout(load, 2500);
    } catch (e) {
      setError(errorText(e));
    }
  }, []);

  useEffect(() => {
    load();
    return () => {
      if (timer.current) clearTimeout(timer.current);
    };
  }, [load]);

  async function onFiles(files: FileList | null) {
    if (!files?.length) return;
    setError("");
    setUploading(true);
    try {
      for (const file of Array.from(files)) await uploadFile("/api/media", file);
      await load();
    } catch (e) {
      const code = (e as { code?: string }).code;
      setError(code === "media_type_unsupported" ? "نوع الملف مش مدعوم (صور JPG/PNG/WebP وفيديو MP4/MOV/WebM)" : code === "file_too_large" ? "الملف أكبر من الحد المسموح (صور 25MB، فيديو 300MB)" : errorText(e));
    } finally {
      setUploading(false);
      if (input.current) input.current.value = "";
    }
  }

  async function remove(m: Media) {
    if (!window.confirm(`حذف «${m.originalName}»؟`)) return;
    try {
      await api(`/api/media/${m.id}`, { method: "DELETE" });
      await load();
    } catch (e) {
      const code = (e as { code?: string }).code;
      setError(code === "media_in_use" ? "الملف مستعمل بمنشور مجدول أو قيد النشر" : errorText(e));
    }
  }

  async function approve(m: Media) {
    try {
      await api(`/api/media/${m.id}/approve`, { method: "POST" });
      await load();
    } catch (e) {
      setError(errorText(e));
    }
  }

  return (
    <div className="max-w-4xl space-y-4">
      <header className="flex flex-wrap items-center justify-between gap-3">
        <h1 className="text-2xl font-bold">مكتبة الوسائط</h1>
        {canEdit && (
          <>
            <input ref={input} type="file" multiple accept="image/jpeg,image/png,image/webp,video/mp4,video/quicktime,video/webm" className="hidden" onChange={(e) => onFiles(e.target.files)} />
            <Button disabled={uploading} onClick={() => input.current?.click()}>{uploading ? "جاري الرفع..." : "رفع صور / فيديو"}</Button>
          </>
        )}
      </header>
      {error && <p role="alert" className="text-sm text-warn">{error}</p>}
      {items.length === 0 && <p className="text-muted-foreground">المكتبة فاضية.</p>}

      <ul className="grid grid-cols-2 gap-3 sm:grid-cols-3 md:grid-cols-4">
        {items.map((m) => (
          <li key={m.id} className="space-y-1 overflow-hidden rounded-xl border border-border bg-card text-xs">
            <MediaThumb id={m.id} kind={m.kind} className="h-32 w-full" />
            <div className="space-y-1 p-2">
              <p className="truncate font-medium" title={m.originalName}>{m.originalName}</p>
              <p className="text-muted-foreground">
                {m.kind === "video" ? "فيديو" : "صورة"} · {mb(m.sizeBytes)}
                {m.width ? ` · ${m.width}×${m.height}` : ""}
                {m.durationSeconds ? ` · ${Math.round(m.durationSeconds)}s` : ""}
              </p>
              <p className={m.status === "failed" ? "text-warn" : m.status === "ready" ? "text-ok" : "text-muted-foreground"}>
                {statusNames[m.status] ?? m.status}
                {m.error ? ` (${m.error})` : ""}
              </p>
              {m.aiGenerated && <p className="text-warn">{m.approvedAt ? "مولّد بالذكاء الاصطناعي — معتمد ✓" : "مولّد بالذكاء الاصطناعي — بانتظار الاعتماد"}</p>}
              {canEdit && (
                <div className="flex gap-1 pt-1">
                  {m.aiGenerated && !m.approvedAt && <Button size="sm" onClick={() => approve(m)}>اعتماد</Button>}
                  <Button size="sm" variant="outline" onClick={() => remove(m)}>حذف</Button>
                </div>
              )}
            </div>
          </li>
        ))}
      </ul>
    </div>
  );
}
