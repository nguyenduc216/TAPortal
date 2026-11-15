import { useEffect, type ReactNode } from "react";
import { useNavigate } from "zmp-ui";
import { useSession } from "./session";

/** Pages that need a signed-in account: without one, back to the start screen (which restores or asks to sign in). */
export function RequireSession({ children }: { children: ReactNode }) {
  const { account } = useSession();
  const navigate = useNavigate();
  useEffect(() => {
    if (!account) navigate("/", { replace: true, animate: false });
  }, [account, navigate]);
  return account ? <>{children}</> : null;
}
