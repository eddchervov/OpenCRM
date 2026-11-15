import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";
import vm from "node:vm";

const source = await readFile(new URL("../../OpenCRM.WebApp/wwwroot/js/payment-complete.js", import.meta.url), "utf8");
const purchaseId = "a2440000-0000-4000-8000-000000000003";
const statusUrl = "/tenant/api/v1/payments/purchases/" + purchaseId;

function createPoller() {
    const window = { location: { href: "https://localhost:7144/tenant/payment/complete", origin: "https://localhost:7144" }, setTimeout, clearTimeout };
    const sandbox = { window, URL, AbortController, Promise, Error, String, Array };
    vm.runInNewContext(source, sandbox, { filename: "payment-complete.js" });
    return window.OpenCRMPaymentCompletion.pollPaymentCompletion;
}

function response(status, body) {
    return { status, ok: status >= 200 && status < 300, json: async () => body };
}

test("polls the exact purchase after 202 and updates when it completes", async () => {
    const poll = createPoller();
    const calls = [];
    const states = [];
    const result = await poll({
        purchaseId, statusUrl, delaysMs: [1, 2], wait: async () => {},
        fetchStatus: async (url, options) => {
            calls.push({ url, options });
            return calls.length === 1
                ? response(202, { purchaseId, code: "InProgress", isPaid: true })
                : response(200, { purchaseId, code: "Completed", isPaid: true });
        },
        onStatus: state => states.push(state)
    });
    assert.deepEqual(JSON.parse(JSON.stringify(result)),  { kind: "terminal", status: "confirmed" });
    assert.equal(calls.length, 2);
    assert.ok(calls.every(call => call.options.method === "GET"));
    assert.ok(calls.every(call => call.url.endsWith("/" + purchaseId)));
    assert.equal(states[0].title, "Оплата получена");
    assert.equal(states.at(-1).title, "Оплата подтверждена");
});

test("rejects a response for a different purchase without trying another ID", async () => {
    const poll = createPoller();
    let calls = 0;
    const result = await poll({
        purchaseId, statusUrl, delaysMs: [1, 2], wait: async () => {},
        fetchStatus: async () => { calls++; return response(200, { purchaseId: "b2440000-0000-4000-8000-000000000003", code: "confirmed" }); }
    });
    assert.deepEqual(JSON.parse(JSON.stringify(result)),  { kind: "error", reason: "purchase_mismatch" });
    assert.equal(calls, 1);
});

test("stops on owner-bound 401 or 404 and never retries another purchase", async () => {
    const poll = createPoller();
    let calls = 0;
    const result = await poll({
        purchaseId, statusUrl, delaysMs: [1, 2, 4], wait: async () => {},
        fetchStatus: async () => { calls++; return response(404, {}); }
    });
    assert.deepEqual(JSON.parse(JSON.stringify(result)),  { kind: "error", reason: "not_available" });
    assert.equal(calls, 1);
});

test("network failures are bounded and return a safe unresolved result", async () => {
    const poll = createPoller();
    let calls = 0;
    const result = await poll({
        purchaseId, statusUrl, delaysMs: [1, 2, 4, 8, 15], wait: async () => {},
        fetchStatus: async () => { calls++; throw new Error("offline"); }
    });
    assert.deepEqual(JSON.parse(JSON.stringify(result)),  { kind: "exhausted" });
    assert.equal(calls, 5);
});

test("cancels during backoff before issuing another request", async () => {
    const poll = createPoller();
    const controller = new AbortController();
    let calls = 0;
    const waiting = new Promise(resolve => {
        controller.signal.addEventListener("abort", resolve, { once: true });
        setTimeout(() => controller.abort(), 1);
    });
    const result = await poll({
        purchaseId, statusUrl, delaysMs: [1, 2], signal: controller.signal,
        wait: async (_delay, signal) => { if (signal.aborted) return; await waiting; },
        fetchStatus: async () => { calls++; return response(200, { purchaseId, code: "pending" }); }
    });
    assert.deepEqual(JSON.parse(JSON.stringify(result)),  { kind: "stopped" });
    assert.equal(calls, 0);
});

test("rejects URLs that do not identify the requested purchase", async () => {
    const poll = createPoller();
    let calls = 0;
    const result = await poll({
        purchaseId, statusUrl: "/api/v1/payments/purchases/b2440000-0000-4000-8000-000000000003",
        delaysMs: [1], fetchStatus: async () => { calls++; return response(200, {}); }
    });
    assert.deepEqual(JSON.parse(JSON.stringify(result)),  { kind: "error", reason: "wrong_purchase_url" });
    assert.equal(calls, 0);
});
