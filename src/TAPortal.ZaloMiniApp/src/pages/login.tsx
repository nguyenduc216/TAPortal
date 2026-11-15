import { useState, type FormEvent } from "react";
import { Box, Button, Input, Page, Text, useNavigate } from "zmp-ui";
import { api } from "../api";
import { useSession } from "../session";
import { accessToken, phoneToken } from "../zalo";

/** Way 2: username + password of TA Portal / Hoa don tu dong; the server also checks that the Zalo phone is the account's. */
export default function LoginPage() {
  const navigate = useNavigate();
  const { signIn } = useSession();
  const [username, setUsername] = useState("");
  const [password, setPassword] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const submit = async (e: FormEvent) => {
    e.preventDefault();
    if (busy) return;
    if (!username.trim() || !password) return setError("Vui lòng nhập tên đăng nhập và mật khẩu.");
    setBusy(true);
    setError(null);
    try {
      const r = await api.login(username.trim(), password, await accessToken(), await phoneToken());
      if (r.session && r.account) {
        signIn(r.session, r.account);
        navigate("/home", { replace: true });
      }
    } catch (err) {
      setError(err instanceof Error ? err.message : "Đăng nhập không thành công.");
    } finally {
      setBusy(false);
    }
  };

  return (
    <Page className="ta-page">
      <Box className="ta-brand ta-brand--small">
        <div className="ta-logo">T.A</div>
        <Text.Title size="large">Đăng nhập</Text.Title>
        <Text className="ta-muted">Dùng tài khoản TA Portal hoặc Hóa đơn tự động</Text>
      </Box>
      <form className="ta-card" onSubmit={submit} data-ta-login>
        <Input label="Tên đăng nhập" placeholder="Tên đăng nhập hoặc email" value={username} autoComplete="username" onChange={(e) => setUsername(e.target.value)} />
        <Input.Password label="Mật khẩu" placeholder="Mật khẩu" value={password} autoComplete="current-password" onChange={(e) => setPassword(e.target.value)} />
        {error && <Text className="ta-error" data-ta-login-error>{error}</Text>}
        <Button htmlType="submit" fullWidth loading={busy}>Đăng nhập</Button>
      </form>
      <Box className="ta-note">
        <Text size="small">Số điện thoại Zalo của bạn phải trùng với số điện thoại đăng ký của tài khoản. Ứng dụng sẽ xin phép dùng số điện thoại Zalo để kiểm tra.</Text>
        <Text size="small">Cách nhanh hơn: đăng nhập trang web, mở mục <b>Zalo Mini App</b> (TA Portal) hoặc <b>Ứng dụng trên Zalo</b> (Hóa đơn tự động) rồi quét mã QR bằng Zalo.</Text>
      </Box>
    </Page>
  );
}
