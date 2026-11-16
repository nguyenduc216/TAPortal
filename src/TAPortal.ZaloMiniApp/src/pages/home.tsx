import { Box, Page, Text, useNavigate } from "zmp-ui";
import { useSession } from "../session";
import { initial } from "./choose";

// Phase 1: the signed-in home. The tiles of phases 2-4 (transactions, invoices, notifications) are shown as coming soon.
const tiles = [
  { key: "transactions", title: "Giao dịch", text: "Tra cứu tiền vào tài khoản ngân hàng", icon: "₫" },
  { key: "invoices", title: "Hóa đơn", text: "Xem và xuất hóa đơn điện tử", icon: "🧾" },
  { key: "notifications", title: "Thông báo", text: "Báo có giao dịch qua Zalo", icon: "🔔" },
];

export default function HomePage() {
  const navigate = useNavigate();
  const { account } = useSession();
  if (!account) return null;
  return (
    <Page className="ta-page">
      <button className="ta-hello" onClick={() => navigate("/account")} data-ta-home>
        <span className="ta-avatar ta-avatar--lg">{initial(account.displayName)}</span>
        <span className="ta-account__text"><small>Xin chào</small><b>{account.displayName}</b></span>
        <span className="ta-chevron">›</span>
      </button>
      <Box className="ta-tiles">
        {tiles.map((t) => (
          <div key={t.key} className="ta-tile is-soon" aria-disabled="true" data-ta-tile={t.key}>
            <span className="ta-tile__icon" aria-hidden="true">{t.icon}</span>
            <b>{t.title}</b>
            <small>{t.text}</small>
            <span className="ta-soon">Sắp có</span>
          </div>
        ))}
      </Box>
      <Text size="small" className="ta-muted ta-foot">Mini App của Tư vấn giải pháp T.A</Text>
    </Page>
  );
}
