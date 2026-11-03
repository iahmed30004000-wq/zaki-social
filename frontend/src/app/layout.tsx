import type { Metadata } from "next";
import "./globals.css";

export const metadata: Metadata = {
  title: "منصة زكي للسوشال ميديا",
  description: "إدارة كل حسابات السوشال ميديا من مكان واحد بمساعدة الذكاء الاصطناعي",
};

export default function RootLayout({ children }: { children: React.ReactNode }) {
  return (
    <html lang="ar" dir="rtl">
      <body className="min-h-screen font-sans antialiased">{children}</body>
    </html>
  );
}
