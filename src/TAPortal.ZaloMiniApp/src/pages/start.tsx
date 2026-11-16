import { useEffect, useRef, useState } from "react";
import { Box, Button, Page, Spinner, Text, useNavigate } from "zmp-ui";
import { api } from "../api";
import { useSession } from "../session";
import { accessToken, launchQr, phoneToken } from "../zalo";

/**
 * Opening the app: a saved session goes straight home; opened from a web QR, sign in with it; otherwise ask the server
 * which accounts this Zalo is linked to (one: home, several: pick, none: sign-in screen).
 */
export default function StartPage() {
  const navigate = useNavigate();
  const { restore, signIn, setChoices } = useSession();
  const [error, setError] = useState<string | null>(null);
  const [attempt, setAttempt] = useState(0);
  const started = useRef(-1);

  useEffect(() => {
    if (started.current === attempt) return;
    started.current = attempt;
    (async () => {
      setError(null);
      try {
        const qr = launchQr();
        if (!qr && (await restore())) return navigate("/home", { replace: true, animate: false });
        const token = await accessToken();
        if (qr) {
          const r = await api.qr(qr, token, await phoneToken());
          if (r.session && r.account) signIn(r.session, r.account);
          return navigate("/home", { replace: true, animate: false });
        }
        const r = await api.resume(token);
        if (r.session && r.account) {
          signIn(r.session, r.account);
          return navigate("/home", { replace: true, animate: false });
        }
        if (r.accounts?.length) {
          setChoices(r.accounts);
          return navigate("/choose", { replace: true, animate: false });
        }
        navigate("/login", { replace: true, animate: false });
      } catch (e) {
        setError(e instanceof Error ? e.message : "Đã có lỗi, vui lòng thử lại.");
      }
    })();
  }, [attempt, navigate, restore, signIn, setChoices]);

  return (
    <Page className="ta-page ta-center">
      <Box className="ta-brand">
        <div className="ta-logo">T.A</div>
        <Text.Title size="large">Tư vấn giải pháp T.A</Text.Title>
        <Text className="ta-muted">TA Portal · Hóa đơn tự động</Text>
      </Box>
      {error ? (
        <Box className="ta-card" data-ta-start-error>
          <Text className="ta-error">{error}</Text>
          <Button fullWidth onClick={() => setAttempt((n) => n + 1)}>Thử lại</Button>
          <Button fullWidth variant="secondary" onClick={() => navigate("/login", { replace: true })}>Đăng nhập bằng tài khoản</Button>
        </Box>
      ) : (
        <Box className="ta-wait"><Spinner visible /><Text className="ta-muted">Đang kiểm tra tài khoản…</Text></Box>
      )}
    </Page>
  );
}
