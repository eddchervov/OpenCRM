(function (global) {
    const delays = [1000, 2000, 4000, 8000, 15000];
    const requestTimeoutMs = 5000;
    const messages = {
        confirmed: { title: window.OpenCRMText('paymentConfirmed'), message: window.OpenCRMText('paymentConfirmedHelp'), icon: "fa-check-circle text-success", tone: "is-success" },
        canceled: { title: window.OpenCRMText('paymentCanceled'), message: window.OpenCRMText('paymentCanceledHelp'), icon: "fa-times-circle text-danger", tone: "is-danger" },
        refunded: { title: window.OpenCRMText('paymentRefunded'), message: window.OpenCRMText('paymentRefundedHelp'), icon: "fa-reply text-secondary", tone: "is-warning" },
        manual_review: { title: window.OpenCRMText('paymentReview'), message: window.OpenCRMText('paymentReviewHelp'), icon: "fa-exclamation-triangle text-warning", tone: "is-warning" },
        paid_waiting_activation: { title: window.OpenCRMText('paymentReceived'), message: window.OpenCRMText('paymentReceivedHelp'), icon: "fa-clock-o text-primary", tone: "is-warning" },
        pending: { title: window.OpenCRMText('paymentPending'), message: window.OpenCRMText('paymentPendingHelp'), icon: "fa-clock-o text-primary", tone: "is-warning" }
    };

    function isPending(code) {
        return ["pending", "payment_required", "inprogress", "in_progress", "paid_waiting_activation"].includes(code);
    }

    function getStatus(result) {
        const code = String(result.code ?? result.Code ?? "").toLowerCase();
        if (code === "inprogress" && (result.isPaid ?? result.IsPaid)) return "paid_waiting_activation";
        return ({ completed: "confirmed", rejected: "canceled", manualreview: "manual_review", paymentrequired: "pending", payment_required: "pending", inprogress: "pending", in_progress: "pending" })[code] || code;
    }

    function getBrandName() {
        const meta = global.document?.querySelector?.('meta[name="project-brand"]');
        return meta?.content?.trim() ?? "";
    }

    async function pollPaymentCompletion(options) {
        const { purchaseId, statusUrl, fetchStatus, signal, wait = (ms, abortSignal) => new Promise((resolve, reject) => {
            const timer = global.setTimeout(() => { abortSignal?.removeEventListener("abort", cancel); resolve(); }, ms);
            const cancel = () => { global.clearTimeout(timer); reject(new Error("Polling stopped.")); };
            abortSignal?.addEventListener("abort", cancel, { once: true });
        }), delaysMs = delays, timeoutMs = requestTimeoutMs, onStatus } = options;
        const id = String(purchaseId || "").toLowerCase();
        if (!/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/.test(id)) {
            return { kind: "error", reason: "invalid_purchase_id" };
        }

        let target;
        try {
            target = new URL(statusUrl, global.location.href);
        } catch {
            return { kind: "error", reason: "invalid_status_url" };
        }
        if (target.origin !== global.location.origin || !target.pathname.toLowerCase().endsWith("/api/v1/payments/purchases/" + id)) {
            return { kind: "error", reason: "wrong_purchase_url" };
        }

        for (const delayMs of delaysMs) {
            if (signal?.aborted) return { kind: "stopped" };
            try { await wait(delayMs, signal); } catch { return { kind: "stopped" }; }
            if (signal?.aborted) return { kind: "stopped" };

            const controller = new AbortController();
            const abortFromPage = () => controller.abort();
            signal?.addEventListener("abort", abortFromPage, { once: true });
            const timeout = global.setTimeout(() => controller.abort(), timeoutMs);
            try {
                const response = await fetchStatus(target.href, { method: "GET", credentials: "same-origin", headers: { Accept: "application/json" }, cache: "no-store", signal: controller.signal });
                if (response.status === 401 || response.status === 404) return { kind: "error", reason: "not_available" };
                if (!response.ok) continue;
                const result = await response.json();
                const returnedId = String(result.purchaseId ?? result.PurchaseId ?? "").toLowerCase();
                if (returnedId !== id) return { kind: "error", reason: "purchase_mismatch" };
                const code = getStatus(result);
                if (messages[code] && !isPending(code)) {
                    onStatus?.(messages[code]);
                    return { kind: "terminal", status: code };
                }
                if (messages[code] && code !== "pending") onStatus?.(messages[code]);
            } catch {
                if (signal?.aborted) return { kind: "stopped" };
            } finally {
                global.clearTimeout(timeout);
                signal?.removeEventListener("abort", abortFromPage);
            }
        }

        return signal?.aborted ? { kind: "stopped" } : { kind: "exhausted" };
    }

    function start() {
        const card = global.document?.getElementById("payment-completion");
        if (!card || card.dataset.pollEnabled !== "true") return;
        const purchaseId = card.dataset.purchaseId;
        const statusUrl = card.dataset.statusUrl;
        const title = global.document.getElementById("payment-status-title");
        const message = global.document.getElementById("payment-status-message");
        const icon = global.document.getElementById("payment-status-icon");
        const glyph = global.document.getElementById("payment-status-icon-glyph");
        const controller = new AbortController();
        const update = state => {
            const brandName = getBrandName();
            global.document.title = brandName ? state.title + " - " + brandName : state.title;
            title.textContent = state.title;
            message.textContent = state.message;
            icon.className = "public-status-icon " + state.tone;
            glyph.className = "fa " + state.icon + " fa-2x";
        };
        const stop = () => controller.abort();
        global.addEventListener("pagehide", stop, { once: true });
        pollPaymentCompletion({ purchaseId, statusUrl, fetchStatus: global.fetch.bind(global), signal: controller.signal, onStatus: update })
            .then(result => {
                if (result.kind === "exhausted") update({ title: window.OpenCRMText('paymentChecking'), message: window.OpenCRMText('paymentCheckingHelp'), icon: "fa-clock-o text-primary", tone: "is-warning" });
                if (result.kind === "error") update({ title: window.OpenCRMText('paymentCheckError'), message: window.OpenCRMText('paymentCheckErrorHelp'), icon: "fa-exclamation-triangle text-warning", tone: "is-warning" });
            })
            .catch(() => update({ title: window.OpenCRMText('paymentCheckError'), message: window.OpenCRMText('paymentCheckCatchHelp'), icon: "fa-exclamation-triangle text-warning", tone: "is-warning" }))
            .finally(() => global.removeEventListener("pagehide", stop));
    }

    global.OpenCRMPaymentCompletion = { pollPaymentCompletion, start };
    if (global.document) {
        if (global.document.readyState === "loading") global.document.addEventListener("DOMContentLoaded", start, { once: true });
        else start();
    }
}(window));
