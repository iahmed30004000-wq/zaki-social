import { Suspense } from "react";
import { RegisterWithInvite } from "./register-with-invite";

export default function RegisterPage() {
  return (
    <Suspense>
      <RegisterWithInvite />
    </Suspense>
  );
}
