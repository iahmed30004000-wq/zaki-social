export type Workspace = { id: string; name: string; role: string };
export type Session = {
  accessToken: string;
  user: { id: string; email: string };
  workspace: Workspace;
  workspaces: Workspace[];
};

// توكن الوصول بذاكرة الصفحة فقط (مش localStorage). توكن التجديد بكوكي httpOnly ما بنقدر نقراه من الجافاسكربت.
let accessToken: string | null = null;
export const setAccessToken = (token: string | null) => {
  accessToken = token;
};

export class ApiError extends Error {
  constructor(
    public status: number,
    public code: string,
  ) {
    super(code);
  }
}

const messages: Record<string, string> = {
  invalid_credentials: "الإيميل أو كلمة السر غير صحيحة",
  email_taken: "هذا الإيميل مسجّل من قبل",
  email_invalid: "الإيميل غير صحيح",
  password_too_short: "كلمة السر لازم تكون 10 أحرف على الأقل",
  invite_invalid: "كود الدعوة غير صحيح أو منتهي",
  invite_email_mismatch: "الدعوة لإيميل ثاني",
  not_a_member: "ما عاد إلك صلاحية بهالمساحة",
  already_member: "هذا الشخص عضو من قبل",
  last_owner: "لازم يبقى مالك واحد على الأقل",
  role_invalid: "الدور غير صحيح",
  no_workspace: "ما في مساحة مرتبطة بحسابك",
  name_required: "اكتب الاسم",
  not_found: "غير موجود",
  invalid_refresh: "انتهت الجلسة، سجّل الدخول من جديد",
};

export function errorText(error: unknown): string {
  if (error instanceof ApiError) return messages[error.code] ?? (error.status === 403 ? "ما عندك صلاحية لهالإجراء" : "صار خطأ، جرّب مرة ثانية");
  return "ما قدرنا نوصل للخادم، تأكد من الاتصال";
}

type Options = { method?: string; body?: unknown };

async function send(path: string, { method = "GET", body }: Options): Promise<Response> {
  const headers: Record<string, string> = {};
  if (body !== undefined) headers["Content-Type"] = "application/json";
  if (accessToken) headers.Authorization = `Bearer ${accessToken}`;
  return fetch(path, {
    method,
    headers,
    body: body === undefined ? undefined : JSON.stringify(body),
    credentials: "same-origin",
    cache: "no-store",
  });
}

async function parse<T>(res: Response): Promise<T> {
  if (res.status === 204) return undefined as T;
  const data = await res.json().catch(() => ({}));
  if (!res.ok) throw new ApiError(res.status, (data as { error?: string }).error ?? "error");
  return data as T;
}

export async function api<T>(path: string, options: Options = {}): Promise<T> {
  let res = await send(path, options);
  if (res.status === 401 && accessToken) {
    const session = await refreshSession();
    if (session) res = await send(path, options);
  }
  return parse<T>(res);
}

/** يجدد الجلسة بكوكي التجديد. بيرجع null لو ما في جلسة. */
export async function refreshSession(): Promise<Session | null> {
  try {
    const res = await fetch("/api/auth/refresh", { method: "POST", credentials: "same-origin", cache: "no-store" });
    if (!res.ok) {
      setAccessToken(null);
      return null;
    }
    const session = (await res.json()) as Session;
    setAccessToken(session.accessToken);
    return session;
  } catch {
    return null;
  }
}

/** يرفع ملف (multipart) بحقل "file". */
export async function uploadFile<T = unknown>(path: string, file: File): Promise<T> {
  const form = new FormData();
  form.append("file", file);
  const send = () =>
    fetch(path, { method: "POST", body: form, headers: accessToken ? { Authorization: `Bearer ${accessToken}` } : {}, credentials: "same-origin" });
  let res = await send();
  if (res.status === 401 && accessToken && (await refreshSession())) res = await send();
  return parse<T>(res);
}

/** ينزّل ملف محمي (يحتاج توكن) كرابط مؤقت للعرض. */
export async function fetchBlobUrl(path: string): Promise<string | null> {
  const send = () => fetch(path, { headers: accessToken ? { Authorization: `Bearer ${accessToken}` } : {}, credentials: "same-origin" });
  let res = await send();
  if (res.status === 401 && accessToken && (await refreshSession())) res = await send();
  return res.ok ? URL.createObjectURL(await res.blob()) : null;
}

export async function authCall(path: string, body: unknown): Promise<Session> {
  const session = await parse<Session>(await send(path, { method: "POST", body }));
  setAccessToken(session.accessToken);
  return session;
}
