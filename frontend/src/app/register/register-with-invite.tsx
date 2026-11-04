"use client";

import { useSearchParams } from "next/navigation";
import { AuthForm } from "@/components/auth-form";

export function RegisterWithInvite() {
  const params = useSearchParams();
  return <AuthForm mode="register" invite={params.get("invite") ?? ""} inviteEmail={params.get("email") ?? ""} />;
}
