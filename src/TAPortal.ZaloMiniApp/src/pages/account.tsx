import { useState } from "react";
import { Box, Button, Modal, Page, Text, useNavigate } from "zmp-ui";
import { useSession } from "../session";
import { initial } from "./choose";

export default function AccountPage() {
  const navigate = useNavigate();
  const { account, choices, signOut } = useSession();
  const [confirm, setConfirm] = useState(false);
  const [busy, setBusy] = useState(false);
  if (!account) return null;

  const leave = async () => {
    setBusy(true);
    await signOut();
    setBusy(false);
    setConfirm(false);
    navigate("/login", { replace: true });
  };

  return (
    <Page className="ta-page">
      <Box className="ta-card ta-profile" data-ta-account>
        <span className="ta-avatar ta-avatar--lg">{initial(account.displayName)}</span>
        <Text.Title>{account.displayName}</Text.Title>
        <Text className="ta-muted">{account.username}</Text>
      </Box>
      {choices.length > 1 && <Button variant="secondary" fullWidth onClick={() => navigate("/choose")}>Đổi tài khoản</Button>}
      <Button variant="secondary" type="danger" fullWidth onClick={() => setConfirm(true)} data-ta-logout>Đăng xuất</Button>
      <Text size="small" className="ta-muted ta-foot">Đăng xuất sẽ hủy liên kết Zalo với tài khoản này. Lần sau cần quét mã QR hoặc đăng nhập lại.</Text>
      <Modal visible={confirm} title="Đăng xuất?" description="Zalo của bạn sẽ không còn liên kết với tài khoản này." onClose={() => setConfirm(false)}
        actions={[{ text: "Hủy", close: true }, { text: busy ? "Đang đăng xuất…" : "Đăng xuất", danger: true, onClick: leave }]} />
    </Page>
  );
}
