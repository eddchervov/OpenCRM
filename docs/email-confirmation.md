# Optional email confirmation

**English** | [Русский](email-confirmation.ru.md)

Email confirmation is disabled by default (`EmailConfirmation:Required=false`). Password registration and sign-in do not require SMTP or mailbox confirmation in the base installation. To require confirmation, first configure and verify notification delivery, then set `EmailConfirmation__Required=true`. The flag does not configure mail delivery by itself.

The restrictions below apply only when this policy is enabled. New and existing users with `EmailConfirmed=false` are redirected to `/auth/verify-email` after sign-in. Their data and memberships are retained; application features remain restricted until confirmation.

The page uses the shared sign-in styles. It displays the current account address and asks the user to open their inbox and follow the link in the email. It provides resend, “I confirmed my email — continue”, support, and sign-out actions. Resending is protected by antiforgery and the active `EmailDelivery` policy; the address comes from the account, not from the form.

After an email is queued, an account-level 60-second cooldown applies, including to the initial registration email. The button displays a countdown and is disabled immediately after being pressed. Before contacting the mail service, the server atomically reserves the send: parallel requests, another tab, a page refresh, and the legacy resend route do not create additional emails during this interval. If queueing fails, the reservation is released and another attempt is allowed. Previously issued valid links remain usable for confirmation.

The independent `EmailDelivery` limit of three requests per IP address per ten minutes remains in effect and counts suppressed retries too. When the limit is reached, the required page shows a message and wait time instead of a JSON error. The account cooldown is held in the current process's memory; restarting the application clears it. Multiple application instances will require shared rate-limit storage.

After confirmation, the restriction is lifted on the next request in the same session. Going to the home page preserves normal navigation: an existing member is taken to the selected organization; a user without organizations is taken to the start of organization creation. Membership and permission checks always apply; subscription restrictions apply only in commercial mode.

## Server-side enforcement

When the policy is enabled, `EmailConfirmationMiddleware` runs after `UseAuthentication` and before `UseAuthorization`. For routes with a policy, it first evaluates the authentication part through `IPolicyEvaluator` to recognize JWTs and use the correct scheme. It then reads the current account from the database: previously issued cookies and JWTs do not bypass the restriction.

HTML requests are redirected to the confirmation page; API requests receive HTTP 403 with `code = email_confirmation_required` and `confirmationUrl`. Prohibited actions, including POST requests, are not executed.

Exceptions are marked with `AllowUnconfirmedEmailAttribute`: confirmation and resending, sign-in/registration, account recovery, sign-out/token revocation, account deletion, return from impersonation, support, legal documents, error pages, health checks, and public static assets. `AllowAnonymous` alone is not sufficient to bypass enforcement. The administration panel and Hangfire still require confirmation even when static metadata would otherwise permit access.

In support, the “New request” button opens a separate form at `/personal/support/create`. The list page continues to show requests and the selected conversation. Successful submission returns to the list with the new request; on failure, entered values remain in the form. The list, creation form, and replies are available to a signed-in user without email confirmation. Antiforgery, the request creation limit, and organization access checks remain in force.

Resending is excluded from the general submit loader because its own handler manages the button state: the general handler in the capture phase disabled the button, while the form handler cancelled that same request as a duplicate. Running `site.js` and `email-verification.js` together preserves the intended behavior: the first request proceeds, a duplicate is blocked, the server cooldown is counted down, and sending is allowed again when it expires. The browser does not check whether the mailbox exists and does not change the countdown after the email is queued; successful queueing does not guarantee delivery to a nonexistent address.

The `.auth-links` styles for ordinary links exclude `.btn` elements so they do not cover the white “Continue working” text on the successful-confirmation page. `.auth-btn` buttons center their contents on both axes.

`RequireConfirmedEmail` and `RequireConfirmedAccount` intentionally remain disabled: password sign-in creates a restricted session for the confirmation page. Middleware controls access to features. Signing in with an email code confirms the address and immediately allows normal use.

API registration retains its response with tokens. When the policy is enabled, it also queues a confirmation email. Before confirmation, those tokens allow only the listed exceptions; application requests receive HTTP 403. Failure to send an email does not grant access, and resending remains available.

## Protection boundaries

Confirmation requires access to the mailbox and prevents use of a fabricated or someone else's address without that access. It does not prevent creating an unconfirmed account record and does not block a temporary mailbox that can receive the email. Independent limits for requests, email sends, and failed sign-ins remain in place; CAPTCHA is not part of this confirmation mechanism.
