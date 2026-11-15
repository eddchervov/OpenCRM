# CRM administration dashboard

**English** | [Русский](admin-dashboard-analytics.ru.md)

The OpenCRM administration dashboard reports on CRM activity: registrations, organizations, maintenance records, and (when commercial mode is enabled) payments. It does not include analytics for a public website, advertising attribution, search-engine services, or third-party tracking providers. Installations can add their own site analytics outside the OpenCRM core.

Dashboard access requires `Admin.Dashboard.View` or the `System` role. The desktop dashboard supports Today, 7 days, and Month periods, automatic or manual refresh, and a separately loaded overall statistics view. Payment data is available only to administrators with `Admin.Billing.View` or the `System` role. The web dashboard applies the same server-side permission checks.

The server API is `GET /api/v1/admin/dashboard`; implementation is in `src/OpenCRM.Infrastructure.Admin/AdminDashboardService.cs` and `src/OpenCRM.WebApp/Controllers/Admin/Api/DashboardApiController.cs`. Coordinate changes to the response DTO with both administration clients.