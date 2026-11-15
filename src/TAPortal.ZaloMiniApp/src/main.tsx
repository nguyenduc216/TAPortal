import { createRoot } from "react-dom/client";
import { Route } from "react-router-dom";
import { AnimationRoutes, App, SnackbarProvider, ZMPRouter } from "zmp-ui";
import "zmp-ui/zaui.css";
import "./css/app.css";
import { SessionProvider } from "./session";
import AccountPage from "./pages/account";
import ChoosePage from "./pages/choose";
import HomePage from "./pages/home";
import LoginPage from "./pages/login";
import StartPage from "./pages/start";
import { RequireSession } from "./guard";

function Root() {
  return (
    <App>
      <SnackbarProvider>
        <SessionProvider>
          <ZMPRouter>
            <AnimationRoutes>
              <Route path="/" element={<StartPage />} />
              <Route path="/login" element={<LoginPage />} />
              <Route path="/choose" element={<ChoosePage />} />
              <Route path="/home" element={<RequireSession><HomePage /></RequireSession>} />
              <Route path="/account" element={<RequireSession><AccountPage /></RequireSession>} />
            </AnimationRoutes>
          </ZMPRouter>
        </SessionProvider>
      </SnackbarProvider>
    </App>
  );
}

createRoot(document.getElementById("app")!).render(<Root />);
