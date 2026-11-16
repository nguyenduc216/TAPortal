// What the Mini App asks Zalo for. The server reads the Zalo user and the Zalo-verified phone from Zalo itself with these
// tokens: the app never sends a phone number of its own.
import { authorize, getAccessToken, getPhoneNumber, getRouteParams } from "zmp-sdk/apis";

export class ZaloError extends Error {}

/** Zalo access token of the user (identifies the Zalo account). */
export async function accessToken(): Promise<string> {
  if (__MOCK__) return "zalo:mock-user";
  try {
    const token = await getAccessToken();
    if (!token) throw new Error("empty");
    return token;
  } catch {
    throw new ZaloError("Không lấy được phiên Zalo. Vui lòng mở lại ứng dụng.");
  }
}

/** One-time token the server exchanges for the Zalo-verified phone; asks the user's permission the first time. */
export async function phoneToken(): Promise<string> {
  if (__MOCK__) return "phone:0912345678";
  try {
    await authorize({ scopes: ["scope.userPhonenumber"] });
  } catch {
    // Already granted on some Zalo versions, or refused: getPhoneNumber below tells which.
  }
  try {
    const { token } = await getPhoneNumber();
    if (!token) throw new Error("empty");
    return token;
  } catch {
    throw new ZaloError("Bạn cần cho phép ứng dụng dùng số điện thoại Zalo để đăng nhập.");
  }
}

/** The one-time QR token when the app was opened from the QR of TA Portal / Hoa don tu dong (…/?qr=…). */
export function launchQr(): string | null {
  const fromUrl = new URLSearchParams(window.location.search).get("qr");
  if (fromUrl) return fromUrl;
  try {
    const params = getRouteParams() as Record<string, string | undefined>;
    return params?.qr ?? null;
  } catch {
    return null;
  }
}
