// npm run mock only (__MOCK__): fake answers of the API so every screen can be seen in a normal browser.
// ?mock=accounts shows the account picker, ?mock=error a wrong phone on sign-in, ?mock=linked a returning user.
import { ApiError, type Account } from "./api";

const people: Account[] = [
  { userId: "11111111-1111-1111-1111-111111111111", displayName: "Nguyễn Văn An", username: "an.nguyen" },
  { userId: "22222222-2222-2222-2222-222222222222", displayName: "Nguyễn Văn An (Công ty B)", username: "an.congtyb" },
];
const scenario = () => new URLSearchParams(window.location.search).get("mock");
const wait = () => new Promise((r) => setTimeout(r, 350));

export async function mockCall<T>(method: string, path: string, body: unknown, session?: string): Promise<T> {
  await wait();
  const b = (body ?? {}) as Record<string, string>;
  const signedIn = (a: Account) => ({ ok: true, session: `mock-session:${a.userId}`, account: a }) as T;
  switch (`${method} ${path}`) {
    case "POST /resume":
      if (scenario() === "accounts") return { ok: true, accounts: people } as T;
      if (scenario() === "linked") return signedIn(people[0]);
      return { ok: true } as T;
    case "POST /qr":
    case "POST /login":
      if (scenario() === "error") throw new ApiError("Số điện thoại Zalo của bạn không trùng với số điện thoại đăng ký của tài khoản.", 400);
      if (path === "/login" && b.password !== "123") throw new ApiError("Tên đăng nhập hoặc mật khẩu không đúng.", 400);
      return signedIn(people[0]);
    case "POST /select":
      return signedIn(people.find((p) => p.userId === b.userId) ?? people[0]);
    case "GET /me": {
      const a = people.find((p) => session === `mock-session:${p.userId}`);
      if (!a) throw new ApiError("", 401);
      return { ok: true, account: a } as T;
    }
    case "POST /logout":
      return { ok: true } as T;
  }
  throw new ApiError("Không tìm thấy", 404);
}
