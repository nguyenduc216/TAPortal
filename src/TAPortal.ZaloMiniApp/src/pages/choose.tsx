import { useEffect, useState } from "react";
import { Box, Button, Page, Text, useNavigate } from "zmp-ui";
import { api, type Account } from "../api";
import { useSession } from "../session";
import { accessToken } from "../zalo";

/** This Zalo is linked to several accounts (operator decision: pick one). */
export default function ChoosePage() {
  const navigate = useNavigate();
  const { choices, signIn } = useSession();
  const [busy, setBusy] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (choices.length === 0) navigate("/", { replace: true });
  }, [choices, navigate]);

  const pick = async (a: Account) => {
    setBusy(a.userId);
    setError(null);
    try {
      const r = await api.select(await accessToken(), a.userId);
      if (r.session && r.account) {
        signIn(r.session, r.account);
        navigate("/home", { replace: true });
      }
    } catch (e) {
      setError(e instanceof Error ? e.message : "Không chọn được tài khoản.");
    } finally {
      setBusy(null);
    }
  };

  return (
    <Page className="ta-page">
      <Box className="ta-brand ta-brand--small">
        <Text.Title size="large">Chọn tài khoản</Text.Title>
        <Text className="ta-muted">Zalo của bạn đang liên kết với {choices.length} tài khoản</Text>
      </Box>
      <Box className="ta-list" data-ta-accounts>
        {choices.map((a) => (
          <button key={a.userId} className="ta-account" onClick={() => pick(a)} disabled={busy !== null}>
            <span className="ta-avatar">{initial(a.displayName)}</span>
            <span className="ta-account__text"><b>{a.displayName}</b><small>{a.username}</small></span>
            <span className="ta-chevron">{busy === a.userId ? "…" : "›"}</span>
          </button>
        ))}
      </Box>
      {error && <Text className="ta-error">{error}</Text>}
      <Button variant="tertiary" fullWidth onClick={() => navigate("/login")}>Đăng nhập tài khoản khác</Button>
    </Page>
  );
}

export const initial = (name: string) => (name.trim().split(/\s+/).pop() ?? "T")[0]?.toUpperCase() ?? "T";
