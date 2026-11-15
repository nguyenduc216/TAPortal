// Client of /api/zalo-mini-app (TACustomerPortal.Web ZaloMiniAppController). Signed-in calls carry "Bearer <session>".

export interface Account {
  userId: string;
  displayName: string;
  username: string;
}

/** A sign-in step: a session (signed in), accounts to pick from, or neither (show the sign-in screen). */
export interface SignIn {
  session?: string | null;
  account?: Account | null;
  accounts?: Account[] | null;
}

export class ApiError extends Error {
  constructor(message: string, readonly status: number) {
    super(message);
  }
}

const NETWORK = "Không kết nối được máy chủ. Vui lòng kiểm tra mạng rồi thử lại.";

async function call<T>(method: "GET" | "POST", path: string, body?: unknown, session?: string): Promise<T> {
  if (__MOCK__) return (await import("./mock")).mockCall<T>(method, path, body, session);
  let response: Response;
  try {
    response = await fetch(`${__API_BASE__}${path}`, {
      method,
      headers: { ...(body ? { "Content-Type": "application/json" } : {}), ...(session ? { Authorization: `Bearer ${session}` } : {}) },
      body: body ? JSON.stringify(body) : undefined,
    });
  } catch {
    throw new ApiError(NETWORK, 0);
  }
  const data = (await response.json().catch(() => null)) as ({ ok?: boolean; error?: string } & T) | null;
  if (response.status === 429) throw new ApiError("Bạn thao tác quá nhanh. Vui lòng đợi một phút rồi thử lại.", 429);
  if (!response.ok || !data || data.ok === false) throw new ApiError(data?.error || NETWORK, response.status);
  return data;
}

export const api = {
  resume: (accessToken: string) => call<SignIn>("POST", "/resume", { accessToken }),
  qr: (qr: string, accessToken: string, phoneToken: string) => call<SignIn>("POST", "/qr", { qr, accessToken, phoneToken }),
  login: (username: string, password: string, accessToken: string, phoneToken: string) =>
    call<SignIn>("POST", "/login", { username, password, accessToken, phoneToken }),
  select: (accessToken: string, userId: string) => call<SignIn>("POST", "/select", { accessToken, userId }),
  me: (session: string) => call<{ account: Account }>("GET", "/me", undefined, session),
  logout: (session: string) => call<unknown>("POST", "/logout", undefined, session),
};
