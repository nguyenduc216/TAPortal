import { createContext, useCallback, useContext, useMemo, useState, type ReactNode } from "react";
import { api, type Account } from "./api";

// The session is a signed token from the server (12 hours). It is kept on the device so reopening the app goes straight
// in; the server still checks the Zalo link and the account's phone on every call.
const KEY = "ta.miniapp.session";

function read(): string | null {
  try {
    return localStorage.getItem(KEY);
  } catch {
    return null;
  }
}

function write(value: string | null) {
  try {
    if (value) localStorage.setItem(KEY, value);
    else localStorage.removeItem(KEY);
  } catch {
    // Storage unavailable: the app still works, it only asks again next time.
  }
}

interface SessionState {
  session: string | null;
  account: Account | null;
  /** Accounts linked to this Zalo, when there are several (for "Đổi tài khoản"). */
  choices: Account[];
  signIn: (session: string, account: Account) => void;
  setChoices: (accounts: Account[]) => void;
  /** Ends the session on this device only. */
  forget: () => void;
  /** Ends the Zalo link on the server too: the next open asks for the QR or the password again. */
  signOut: () => Promise<void>;
  restore: () => Promise<boolean>;
}

const Context = createContext<SessionState | null>(null);

export function SessionProvider({ children }: { children: ReactNode }) {
  const [session, setSession] = useState<string | null>(read);
  const [account, setAccount] = useState<Account | null>(null);
  const [choices, setChoices] = useState<Account[]>([]);

  const signIn = useCallback((s: string, a: Account) => {
    write(s);
    setSession(s);
    setAccount(a);
  }, []);

  const forget = useCallback(() => {
    write(null);
    setSession(null);
    setAccount(null);
  }, []);

  const signOut = useCallback(async () => {
    if (session) await api.logout(session).catch(() => undefined);
    setChoices([]);
    forget();
  }, [session, forget]);

  const restore = useCallback(async () => {
    const saved = read();
    if (!saved) return false;
    try {
      const { account: a } = await api.me(saved);
      setSession(saved);
      setAccount(a);
      return true;
    } catch {
      forget();
      return false;
    }
  }, [forget]);

  const value = useMemo(() => ({ session, account, choices, signIn, setChoices, forget, signOut, restore }), [session, account, choices, signIn, forget, signOut, restore]);
  return <Context.Provider value={value}>{children}</Context.Provider>;
}

export function useSession(): SessionState {
  const value = useContext(Context);
  if (!value) throw new Error("SessionProvider missing");
  return value;
}
