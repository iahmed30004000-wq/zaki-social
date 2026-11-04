export function ComingSoon({ title, step }: { title: string; step: number }) {
  return (
    <div className="max-w-xl space-y-2">
      <h1 className="text-2xl font-bold">{title}</h1>
      <p className="text-muted-foreground">هالشاشة بتنبني بالخطوة {step} من خطة البناء.</p>
    </div>
  );
}
