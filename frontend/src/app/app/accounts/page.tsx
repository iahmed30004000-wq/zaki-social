"use client";

import { Suspense, useCallback, useEffect, useState } from "react";
import { useSearchParams } from "next/navigation";
import { useAuth } from "@/components/auth-provider";
import { Button } from "@/components/ui/button";
import { api, errorText } from "@/lib/api";

type PlatformInfo = { platform: string; configured: boolean };
type Account = { id: string; platform: string; displayName: string; remoteUserId: string; status: string };

const platformNames: Record<string, string> = {
  facebook: "فيسبوك",
  instagram: "إنستغرام",
  tiktok: "تيك توك",
  x: "إكس",
  youtube: "يوتيوب",
  linkedin: "لينكدإن",
};

const statusNames: Record<string, string> = { active: "شغّال", expired: "منتهي", disabled: "موقوف", error: "خطأ" };

const notices: Record<string, string> = {
  state_invalid: "رابط الربط منتهي أو غير صحيح، جرّب من جديد",
  denied: "تم رفض الموافقة من المنصة",
  forbidden: "ما عندك صلاحية ربط حسابات",
  platform_error: "المنصة رفضت الربط، تأكد من إعدادات التطبيق وجرّب من جديد",
};

function AccountsInner() {
  const { session } = useAuth();
  const params = useSearchParams();
  const canEdit = session?.workspace.role !== "viewer";
  const [accounts, setAccounts] = useState<Account[]>([]);
  const [error, setError] = useState("");
  const [platforms, setPlatforms] = useState<PlatformInfo[]>([]);

  const load = useCallback(async () => {
    try {
      setAccounts(await api<Account[]>("/api/accounts"));
    } catch (e) {
      setError(errorText(e));
    }
  }, []);

  useEffect(() => {
    load();
    api<PlatformInfo[]>("/api/accounts/platforms").then(setPlatforms).catch(() => undefined);
  }, [load]);

  async function connect(platform: string) {
    setError("");
    try {
      const { url } = await api<{ url: string }>(`/api/accounts/${platform}/connect-url`);
      window.location.href = url;
    } catch (e) {
      const code = (e as { code?: string }).code;
      setError(code === `${platform}_not_configured` ? "لازم تضيف مفتاح تطبيق المنصة بإعدادات السيرفر أول (راجع دليل النشر)" : errorText(e));
    }
  }

  async function disconnect(a: Account) {
    if (!window.confirm(`فصل حساب «${a.displayName}»؟`)) return;
    try {
      await api(`/api/accounts/${a.id}`, { method: "DELETE" });
      await load();
    } catch (e) {
      setError(errorText(e));
    }
  }

  const connected = params.get("connected");
  const notice = params.get("error");

  return (
    <div className="max-w-3xl space-y-6">
      <header className="flex flex-wrap items-center justify-between gap-3">
        <h1 className="text-2xl font-bold">إدارة الحسابات</h1>
        {canEdit && (
          <div className="flex flex-wrap gap-2">
            {platforms.map((p) => (
              <Button key={p.platform} variant={p.configured ? "default" : "outline"} onClick={() => connect(p.platform)} title={p.configured ? "" : "المنصة غير مفعّلة بإعدادات السيرفر"}>
                ربط {platformNames[p.platform] ?? p.platform}
              </Button>
            ))}
          </div>
        )}
      </header>

      {connected && <p className="rounded-lg bg-muted p-3 text-sm text-ok">تم ربط {connected} حساب/صفحة بنجاح.</p>}
      {notice && <p role="alert" className="rounded-lg bg-muted p-3 text-sm text-warn">{notices[notice] ?? "صار خطأ بالربط"}</p>}
      {error && <p role="alert" className="text-sm text-warn">{error}</p>}

      {accounts.length === 0 ? (
        <p className="text-muted-foreground">ما في حسابات مربوطة لسا.</p>
      ) : (
        <ul className="divide-y divide-border rounded-xl border border-border bg-card">
          {accounts.map((a) => (
            <li key={a.id} className="flex flex-wrap items-center justify-between gap-2 p-3">
              <span>
                <span className="font-medium">{a.displayName}</span>
                <span className="ms-2 text-xs text-muted-foreground">{platformNames[a.platform] ?? a.platform}</span>
              </span>
              <span className="flex items-center gap-3">
                <span className={a.status === "active" ? "text-sm text-ok" : "text-sm text-warn"}>{statusNames[a.status] ?? a.status}</span>
                {canEdit && (
                  <Button variant="outline" size="sm" onClick={() => disconnect(a)}>
                    فصل
                  </Button>
                )}
              </span>
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}

export default function AccountsPage() {
  return (
    <Suspense>
      <AccountsInner />
    </Suspense>
  );
}
