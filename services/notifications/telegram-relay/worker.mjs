const TELEGRAM_API = "https://api.telegram.org";
const MAX_REQUEST_BYTES = 1024 * 1024;
const MAX_TEXT_LENGTH = 4096;
const MAX_PHOTO_BYTES = 10 * 1024 * 1024;
const MAX_DOCUMENT_BYTES = 20 * 1024 * 1024;
const PROVIDER_FILE_ID_PATTERN = /^[A-Za-z0-9_-]{10,512}$/;
const BOT_TOKEN_PATTERN = /^\d{5,15}:[A-Za-z0-9_-]{20,100}$/;
const SECRET_TOKEN_PATTERN = /^[A-Za-z0-9_-]{1,256}$/;
const CHAT_ID_PATTERN = /^-?[1-9]\d{0,19}$/;
const ORGANIZATION_ID_PATTERN = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

export default {
  async fetch(request, env) {
    const url = new URL(request.url);
    if (url.search) {
      return new Response(null, { status: 404 });
    }

    if (url.pathname.startsWith("/updates/")) {
      return forwardUpdate(request, env, url.pathname);
    }

    if (!["/sendMessage", "/sendOrganizationMessage", "/sendOrganizationPhoto", "/sendOrganizationDocument",
      "/getOrganizationFile", "/getOrganizationBot", "/setOrganizationWebhook"]
      .includes(url.pathname)) {
      return new Response(null, { status: 404 });
    }

    if (request.method !== "POST") {
      return new Response(null, { status: 405 });
    }

    if (!hasRelayKey(request, env)) {
      return new Response(null, { status: 401 });
    }

    if (url.pathname === "/sendOrganizationPhoto" || url.pathname === "/sendOrganizationDocument") {
      return sendOrganizationMedia(request, url.pathname);
    }

    const payload = await readJson(request, MAX_REQUEST_BYTES);
    if (!payload) {
      return new Response(null, { status: 400 });
    }

    switch (url.pathname) {
      case "/sendMessage":
        if (!env.TELEGRAM_BOT_TOKEN || !env.TELEGRAM_OWNER_CHAT_ID) {
          return new Response(null, { status: 503 });
        }
        if (!isValidText(payload.text)) {
          return new Response(null, { status: 400 });
        }

        return telegramRequest(env.TELEGRAM_BOT_TOKEN, "sendMessage", {
          chat_id: env.TELEGRAM_OWNER_CHAT_ID,
          text: payload.text,
        });
      case "/sendOrganizationMessage":
        if (!isValidBotToken(payload.bot_token)
          || !isValidChatId(payload.chat_id)
          || !isValidText(payload.text)) {
          return new Response(null, { status: 400 });
        }

        return telegramRequest(payload.bot_token, "sendMessage", {
          chat_id: String(payload.chat_id),
          text: payload.text,
        });
      case "/getOrganizationFile":
        return getOrganizationFile(payload);
      case "/getOrganizationBot":
        if (!isValidBotToken(payload.bot_token)) {
          return new Response(null, { status: 400 });
        }

        return telegramRequest(payload.bot_token, "getMe");
      case "/setOrganizationWebhook":
        if (!isValidBotToken(payload.bot_token)
          || !isValidOrganizationId(payload.organization_id)
          || typeof payload.secret !== "string"
          || !SECRET_TOKEN_PATTERN.test(payload.secret)) {
          return new Response(null, { status: 400 });
        }

        const webhookBaseUrl = parseHttpsBaseUrl(env.TELEGRAM_WEBHOOK_BASE_URL);
        if (!webhookBaseUrl) {
          return new Response(null, { status: 503 });
        }

        return telegramRequest(payload.bot_token, "setWebhook", {
          url: `${webhookBaseUrl}/updates/${payload.organization_id}`,
          secret_token: payload.secret,
          allowed_updates: ["message", "my_chat_member"],
        });
      default:
        return new Response(null, { status: 404 });
    }
  },
};

async function readJson(request, maxBytes) {
  const contentLength = Number(request.headers.get("Content-Length"));
  if (contentLength > maxBytes) {
    return null;
  }

  try {
    const body = await request.text();
    if (new TextEncoder().encode(body).byteLength > maxBytes) {
      return null;
    }

    const payload = JSON.parse(body);
    return payload && typeof payload === "object" && !Array.isArray(payload) ? payload : null;
  } catch {
    return null;
  }
}

async function sendOrganizationMedia(request, pathname) {
  const contentLength = Number(request.headers.get("Content-Length"));
  if (contentLength > MAX_DOCUMENT_BYTES + 16 * 1024) {
    return new Response(null, { status: 413 });
  }

  let fields;
  try {
    fields = await request.formData();
  } catch {
    return new Response(null, { status: 400 });
  }

  const botToken = fields.get("bot_token");
  const chatId = fields.get("chat_id");
  const caption = fields.get("caption");
  const file = fields.get("file");
  const isPhoto = pathname === "/sendOrganizationPhoto";
  if (!isValidBotToken(botToken)
    || !isValidChatId(chatId)
    || (caption !== null && !isValidCaption(caption))
    || !(file instanceof File)
    || file.size === 0
    || file.size > (isPhoto ? MAX_PHOTO_BYTES : MAX_DOCUMENT_BYTES)
    || !isSafeFileName(file.name)
    || (isPhoto && !["image/jpeg", "image/png", "image/webp"].includes(file.type))) {
    return new Response(null, { status: 400 });
  }

  const payload = new FormData();
  payload.set("chat_id", String(chatId));
  payload.set(isPhoto ? "photo" : "document", file, file.name);
  if (caption) {
    payload.set("caption", caption);
  }

  return telegramMultipartRequest(
    botToken,
    isPhoto ? "sendPhoto" : "sendDocument",
    payload);
}

async function getOrganizationFile(payload) {
  if (!isValidBotToken(payload.bot_token)
    || typeof payload.file_id !== "string"
    || !PROVIDER_FILE_ID_PATTERN.test(payload.file_id)) {
    return new Response(null, { status: 400 });
  }

  const metadataResponse = await telegramRequest(payload.bot_token, "getFile", { file_id: payload.file_id });
  if (!metadataResponse.ok) {
    return metadataResponse;
  }

  let filePath;
  let fileSize;
  try {
    const metadata = await metadataResponse.json();
    filePath = metadata?.result?.file_path;
    fileSize = metadata?.result?.file_size;
  } catch {
    return new Response(null, { status: 502 });
  }

  if (typeof filePath !== "string"
    || !/^[A-Za-z0-9_./-]+$/.test(filePath)
    || filePath.includes("..")
    || filePath.startsWith("/")
    || !Number.isSafeInteger(fileSize)
    || fileSize <= 0
    || fileSize > MAX_DOCUMENT_BYTES) {
    return new Response(null, { status: 413 });
  }

  try {
    const response = await fetch(`${TELEGRAM_API}/file/bot${payload.bot_token}/${filePath}`);
    if (!response.ok) {
      return new Response(null, { status: response.status === 404 ? 404 : 502 });
    }

    const contentLength = Number(response.headers.get("Content-Length"));
    if (contentLength > MAX_DOCUMENT_BYTES) {
      return new Response(null, { status: 413 });
    }

    const body = await response.arrayBuffer();
    if (body.byteLength === 0 || body.byteLength > MAX_DOCUMENT_BYTES) {
      return new Response(null, { status: 413 });
    }

    return new Response(body, {
      status: 200,
      headers: {
        "Cache-Control": "no-store",
        "Content-Length": String(body.byteLength),
        "Content-Type": "application/octet-stream",
      },
    });
  } catch {
    return new Response(null, { status: 502 });
  }
}

function hasRelayKey(request, env) {
  return Boolean(env.RELAY_KEY)
    && request.headers.get("Authorization") === `Bearer ${env.RELAY_KEY}`;
}

function isValidBotToken(value) {
  return typeof value === "string" && BOT_TOKEN_PATTERN.test(value);
}

function isValidChatId(value) {
  return (typeof value === "string" || typeof value === "number")
    && CHAT_ID_PATTERN.test(String(value));
}

function isValidOrganizationId(value) {
  return typeof value === "string" && ORGANIZATION_ID_PATTERN.test(value);
}

function isValidText(value) {
  return typeof value === "string" && value.length > 0 && value.length <= MAX_TEXT_LENGTH;
}

function isValidCaption(value) {
  return typeof value === "string" && value.length <= 1024;
}

function isSafeFileName(value) {
  return typeof value === "string"
    && value.length > 0
    && value.length <= 255
    && !/[\\/\u0000-\u001f\u007f]/.test(value)
    && value !== "."
    && value !== "..";
}

async function telegramRequest(botToken, method, payload) {
  try {
    return await fetch(`${TELEGRAM_API}/bot${botToken}/${method}`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(payload ?? {}),
    });
  } catch {
    return new Response(null, { status: 502 });
  }
}

async function telegramMultipartRequest(botToken, method, payload) {
  try {
    return await fetch(`${TELEGRAM_API}/bot${botToken}/${method}`, {
      method: "POST",
      body: payload,
    });
  } catch {
    return new Response(null, { status: 502 });
  }
}

async function forwardUpdate(request, env, pathname) {
  if (request.method !== "POST") {
    return new Response(null, { status: 405 });
  }

  const organizationId = pathname.slice("/updates/".length);
  if (!isValidOrganizationId(organizationId)) {
    return new Response(null, { status: 404 });
  }

  const secret = request.headers.get("X-Telegram-Bot-Api-Secret-Token");
  if (!SECRET_TOKEN_PATTERN.test(secret ?? "")) {
    return new Response(null, { status: 401 });
  }

  const contentLength = Number(request.headers.get("Content-Length"));
  if (contentLength > MAX_REQUEST_BYTES) {
    return new Response(null, { status: 413 });
  }

  const body = await request.arrayBuffer();
  if (body.byteLength > MAX_REQUEST_BYTES) {
    return new Response(null, { status: 413 });
  }

  const baseUrl = parseHttpsBaseUrl(env.OPENCRM_BASE_URL);
  if (!baseUrl) {
    return new Response(null, { status: 503 });
  }

  try {
    const response = await fetch(
      `${baseUrl}/api/internal/notification-bots/telegram/${organizationId}/updates`,
      {
        method: "POST",
        headers: {
          "Content-Type": request.headers.get("Content-Type") ?? "application/json",
          "X-Telegram-Bot-Api-Secret-Token": secret,
        },
        body,
      },
    );

    return new Response(response.body, {
      status: response.status,
      headers: { "Cache-Control": "no-store" },
    });
  } catch {
    return new Response(null, { status: 502 });
  }
}

function parseHttpsBaseUrl(value) {
  try {
    const url = new URL(value);
    if (url.protocol !== "https:" || url.username || url.password || url.search || url.hash
      || (url.pathname !== "/" && url.pathname !== "")) {
      return null;
    }

    return url.origin;
  } catch {
    return null;
  }
}
