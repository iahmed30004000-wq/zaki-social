import { HealthStatus } from "@/components/health-status";

const platforms = ["فيسبوك", "إنستغرام", "تيك توك", "إكس", "يوتيوب", "لينكدإن"];

export default function Home() {
  return (
    <main className="mx-auto flex min-h-screen max-w-3xl flex-col justify-center gap-8 px-4 py-12">
      <header className="space-y-3">
        <p className="text-sm font-medium text-primary">قيد البناء — الخطوة 01</p>
        <h1 className="text-3xl font-bold leading-tight sm:text-4xl">منصة زكي للسوشال ميديا</h1>
        <p className="text-lg text-muted-foreground">
          كل حساباتك بمكان واحد، والذكاء الاصطناعي بيكتب ويجدول ويرد ويحلل معك.
        </p>
      </header>

      <ul className="flex flex-wrap gap-2">
        {platforms.map((p) => (
          <li key={p} className="rounded-full border border-border bg-card px-3 py-1 text-sm">
            {p}
          </li>
        ))}
      </ul>

      <HealthStatus />
    </main>
  );
}
