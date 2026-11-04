"use client";

import { useEffect, useState } from "react";
import { fetchBlobUrl } from "@/lib/api";

/** مصغّر وسيط: الطلب بيحتاج توكن، فبننزّله كـ blob وبنعرضه. */
export function MediaThumb({ id, kind, className = "" }: { id: string; kind: string; className?: string }) {
  const [url, setUrl] = useState<string | null>(null);

  useEffect(() => {
    let revoked = false;
    let created: string | null = null;
    fetchBlobUrl(`/api/media/${id}/thumb`).then((u) => {
      if (revoked) {
        if (u) URL.revokeObjectURL(u);
        return;
      }
      created = u;
      setUrl(u);
    });
    return () => {
      revoked = true;
      if (created) URL.revokeObjectURL(created);
    };
  }, [id]);

  if (!url) return <div className={`flex items-center justify-center bg-muted text-xs text-muted-foreground ${className}`}>{kind === "video" ? "🎬" : "🖼️"}</div>;
  // eslint-disable-next-line @next/next/no-img-element
  return <img src={url} alt="" className={`object-cover ${className}`} />;
}
