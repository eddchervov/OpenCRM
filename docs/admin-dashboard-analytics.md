# CRM administration dashboard

**English** | [Русский](admin-dashboard-analytics.ru.md)

The dashboard shows CRM data: users, organizations, maintenance records, payments, and feature usage. Access to financial metrics is controlled by administrative permissions.

The reporting period and data refresh are selected in the interface. A server-side cache serves repeated requests; after server logic changes, metrics must be recalculated consistently with the selected period.

Public website analytics, advertising sources, and connections to external tracking services are not part of the platform. An application built on OpenCRM implements those itself.

The implementation is in `src/OpenCRM.Infrastructure.Admin/AdminDashboardService.cs`; the API is in the web application's administration controllers. Changes to the metrics must be coordinated across the web and desktop administration interfaces and their tests.
