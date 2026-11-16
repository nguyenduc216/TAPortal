import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";
import zaloMiniApp from "zmp-vite-plugin";

// Modes (no secret anywhere: the Mini App secret key stays on the server):
// - build (production): the Mini App calls the Hoa don tu dong API directly (domain whitelisted in the Mini App console).
// - dev: /api/zalo-mini-app is proxied to that site, so the browser needs no CORS for localhost.
// - mock (npm run mock): every screen in a normal browser with a fake Zalo and fake API answers; nothing leaves the machine.
const API = "https://hoadon.tuvangiaiphapta.vn";

export default defineConfig(({ command, mode }) => ({
  root: ".",
  base: "",
  plugins: [zaloMiniApp(), react()],
  define: {
    __API_BASE__: JSON.stringify(command === "build" ? `${API}/api/zalo-mini-app` : "/api/zalo-mini-app"),
    __MOCK__: JSON.stringify(mode === "mock"),
  },
  build: { outDir: "dist", assetsInlineLimit: 0 },
  server: {
    port: 3000,
    proxy: { "/api/zalo-mini-app": { target: process.env.API_PROXY_TARGET || API, changeOrigin: true, secure: true } },
  },
}));
