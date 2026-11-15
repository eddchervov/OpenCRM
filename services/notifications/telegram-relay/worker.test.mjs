import assert from "node:assert/strict";
import test from "node:test";
import worker from "./worker.mjs";

const env = {
  RELAY_KEY: "relay-secret",
  TELEGRAM_BOT_TOKEN: "",
  TELEGRAM_OWNER_CHAT_ID: "123456",
  TELEGRAM_WEBHOOK_BASE_URL: "https://relay.example.com",
  OPENCRM_BASE_URL: "https://opencrm.example.invalid",
};

test("отклоняет запрос без ключа", async () => {
  const request = new Request("https://relay.example.com/sendMessage", {
    method: "POST",
    body: JSON.stringify({ text: "Текст" }),
  });

  const response = await worker.fetch(request, env);

  assert.equal(response.status, 401);
});

test("отправляет только в заданный чат", async () => {
  const originalFetch = globalThis.fetch;
  let telegramRequest;
  globalThis.fetch = async (url, init) => {
    telegramRequest = { url, init };
    return Response.json({ ok: true, result: { message_id: 735 } });
  };

  try {
    const request = new Request("https://relay.example.com/sendMessage", {
      method: "POST",
      headers: { Authorization: "Bearer relay-secret" },
      body: JSON.stringify({ text: "Текст", chat_id: "other-chat" }),
    });

    const response = await worker.fetch(request, env);

    assert.equal(response.status, 200);
    assert.equal((await response.json()).result.message_id, 735);
    assert.equal(telegramRequest.url, "https://api.telegram.org/botbot-secret/sendMessage");
    assert.deepEqual(JSON.parse(telegramRequest.init.body), {
      chat_id: "123456",
      text: "Текст",
    });
  } finally {
    globalThis.fetch = originalFetch;
  }
});

test("отправляет через бота организации только с relay-ключом", async () => {
  const originalFetch = globalThis.fetch;
  let telegramRequest;
  globalThis.fetch = async (url, init) => {
    telegramRequest = { url, init };
    return Response.json({ ok: true, result: { message_id: 812 } });
  };

  try {
    const request = new Request("https://relay.example.com/sendOrganizationMessage", {
      method: "POST",
      headers: { Authorization: "Bearer relay-secret" },
      body: JSON.stringify({
        bot_token: "",
        chat_id: "7654321",
        text: "Заказ готов",
      }),
    });

    const response = await worker.fetch(request, env);

    assert.equal(response.status, 200);
    assert.equal(telegramRequest.url,
      "https://api.telegram.org/bot123456:abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMN/sendMessage");
    assert.deepEqual(JSON.parse(telegramRequest.init.body), {
      chat_id: "7654321",
      text: "Заказ готов",
    });
  } finally {
    globalThis.fetch = originalFetch;
  }
});

test("отправляет документ из multipart в заданный чат", async () => {
  const originalFetch = globalThis.fetch;
  let telegramRequest;
  globalThis.fetch = async (url, init) => {
    telegramRequest = { url, init };
    return Response.json({ ok: true, result: { message_id: 813 } });
  };

  try {
    const form = new FormData();
    form.set("bot_token", "123456:abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMN");
    form.set("chat_id", "7654321");
    form.set("caption", "Файл клиента");
    form.set("file", new File(["safe content"], "report.pdf", { type: "application/pdf" }));
    const response = await worker.fetch(new Request("https://relay.example.com/sendOrganizationDocument", {
      method: "POST",
      headers: { Authorization: "Bearer relay-secret" },
      body: form,
    }), env);

    assert.equal(response.status, 200);
    assert.equal(telegramRequest.url,
      "https://api.telegram.org/bot123456:abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMN/sendDocument");
    const sent = await new Response(telegramRequest.init.body).formData();
    assert.equal(sent.get("chat_id"), "7654321");
    assert.equal(sent.get("caption"), "Файл клиента");
    assert.equal(sent.get("document").name, "report.pdf");
  } finally {
    globalThis.fetch = originalFetch;
  }
});

test("получает файл только по Telegram file_id и фиксированному домену Telegram", async () => {
  const originalFetch = globalThis.fetch;
  const urls = [];
  globalThis.fetch = async (url, init) => {
    urls.push({ url, init });
    if (String(url).endsWith("/getFile")) {
      return Response.json({ ok: true, result: { file_path: "documents/file_123.pdf", file_size: 4 } });
    }

    return new Response("file", { status: 200 });
  };

  try {
    const response = await worker.fetch(new Request("https://relay.example.com/getOrganizationFile", {
      method: "POST",
      headers: { Authorization: "Bearer relay-secret" },
      body: JSON.stringify({
        bot_token: "",
        file_id: "Abcdefghijk-123",
      }),
    }), env);

    assert.equal(response.status, 200);
    assert.equal(await response.text(), "file");
    assert.equal(urls[0].url,
      "https://api.telegram.org/bot123456:abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMN/getFile");
    assert.equal(urls[1].url,
      "https://api.telegram.org/file/bot123456:abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMN/documents/file_123.pdf");
  } finally {
    globalThis.fetch = originalFetch;
  }
});

test("отклоняет путь файла с обходом каталога до скачивания", async () => {
  const originalFetch = globalThis.fetch;
  const urls = [];
  globalThis.fetch = async (url, init) => {
    urls.push({ url, init });
    return Response.json({ ok: true, result: { file_path: "../private/file.pdf", file_size: 4 } });
  };

  try {
    const response = await worker.fetch(new Request("https://relay.example.com/getOrganizationFile", {
      method: "POST",
      headers: { Authorization: "Bearer relay-secret" },
      body: JSON.stringify({
        bot_token: "",
        file_id: "Abcdefghijk-123",
      }),
    }), env);

    assert.equal(response.status, 413);
    assert.equal(urls.length, 1);
  } finally {
    globalThis.fetch = originalFetch;
  }
});

test("bot organization management calls Telegram getMe and fixed setWebhook", async () => {
  const originalFetch = globalThis.fetch;
  const telegramRequests = [];
  globalThis.fetch = async (url, init) => {
    telegramRequests.push({ url, init });
    return Response.json({ ok: true, result: { id: 1 } });
  };

  try {
    const botToken = "";
    const getMeResponse = await worker.fetch(new Request("https://relay.example.com/getOrganizationBot", {
      method: "POST",
      headers: { Authorization: "Bearer relay-secret" },
      body: JSON.stringify({ bot_token: botToken }),
    }), env);
    const setWebhookResponse = await worker.fetch(new Request("https://relay.example.com/setOrganizationWebhook", {
      method: "POST",
      headers: { Authorization: "Bearer relay-secret" },
      body: JSON.stringify({
        bot_token: botToken,
        organization_id: "7ac4ed0f-408f-4d05-86ce-e9e9f0867810",
        secret: "",
      }),
    }), env);

    assert.equal(getMeResponse.status, 200);
    assert.equal(setWebhookResponse.status, 200);
    assert.equal(telegramRequests[0].url,
      `https://api.telegram.org/bot${botToken}/getMe`);
    assert.equal(telegramRequests[1].url,
      `https://api.telegram.org/bot${botToken}/setWebhook`);
    assert.deepEqual(JSON.parse(telegramRequests[1].init.body), {
      url: "https://relay.example.com/updates/7ac4ed0f-408f-4d05-86ce-e9e9f0867810",
      secret_token: "webhook_secret-123",
        allowed_updates: ["message", "my_chat_member"],
    });
  } finally {
    globalThis.fetch = originalFetch;
  }
});

test("incoming webhook forwards only to fixed OpenCRM endpoint and preserves Telegram secret", async () => {
  const originalFetch = globalThis.fetch;
  let forwarded;
  globalThis.fetch = async (url, init) => {
    forwarded = { url, init };
    return new Response(null, { status: 200 });
  };

  try {
    const response = await worker.fetch(new Request(
      "https://relay.example.com/updates/7ac4ed0f-408f-4d05-86ce-e9e9f0867810",
      {
        method: "POST",
        headers: {
          "Content-Type": "application/json",
          "X-Telegram-Bot-Api-Secret-Token": "webhook_secret-123",
        },
        body: JSON.stringify({ update_id: 1 }),
      }), env);

    assert.equal(response.status, 200);
    assert.equal(forwarded.url,
      "https://opencrm.example.invalid/api/internal/notification-bots/telegram/7ac4ed0f-408f-4d05-86ce-e9e9f0867810/updates");
    assert.equal(forwarded.init.headers["X-Telegram-Bot-Api-Secret-Token"], "webhook_secret-123");
    assert.deepEqual(JSON.parse(new TextDecoder().decode(forwarded.init.body)), { update_id: 1 });
  } finally {
    globalThis.fetch = originalFetch;
  }
});

test("rejects arbitrary URL-like routes and invalid dynamic bot tokens", async () => {
  const unknown = await worker.fetch(new Request("https://relay.example.com/sendMessage/https://evil.test", {
    method: "POST",
    headers: { Authorization: "Bearer relay-secret" },
    body: JSON.stringify({ text: "test" }),
  }), env);
  const invalidToken = await worker.fetch(new Request("https://relay.example.com/sendOrganizationMessage", {
    method: "POST",
    headers: { Authorization: "Bearer relay-secret" },
    body: JSON.stringify({ bot_token: "https://evil.test", chat_id: "1", text: "test" }),
  }), env);

  assert.equal(unknown.status, 404);
  assert.equal(invalidToken.status, 400);
});

test("rejects null JSON payload without crashing", async () => {
  const response = await worker.fetch(new Request("https://relay.example.com/sendMessage", {
    method: "POST",
    headers: { Authorization: "Bearer relay-secret" },
    body: "null",
  }), env);

  assert.equal(response.status, 400);
});
