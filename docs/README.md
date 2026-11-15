# OpenCRM technical documentation

**English** | [Русский](README.ru.md)

Guides for running and extending the CRM core, along with component architecture and contract descriptions.

## Getting started

- [Interface gallery: 12 current screenshots](screenshots.md).
- [OpenCRM core overview](../README.md).
- [Local setup: dependencies, database, migrations, and first sign-in](local-run.md).
- [Configure your installation](configuration.md).
- [Notification and payment microservices](microservices.md).
- [First administrator, web and desktop admin interfaces](administration.md).
- [Customization, extension points, and upstream updates](extending-opencrm.md).
- [Development, testing, and contributing to the core](../CONTRIBUTING.md).
- [Project rules](../AGENTS.md).

## Architecture and infrastructure

- [Application architecture](architecture-current.md)
- [Service health checks](service-health-monitoring.md)
- [Notifications](notifications.md)
- [Payment service integration](payments-service-integration.md)

## Workspaces

- [Appointments and schedule availability](booking-availability.md)
- [Rescheduling appointments](calendar-rescheduling.md)
- [Maintenance periods](maintenance-periods.md)
- [Work order services and payments](maintenance-services-payments.md)
- [Catalog integrity](service-catalog-integrity.md)
- [Service duration](service-duration.md)
- [Service journal](service-journal.md)
- [Organization data](organization-setup.md), [members](organization-employees.md), [export](organization-data-export.md)
- [CRM data import](universal-crm-import.md)
- [Vehicle identity](vehicle-identity.md)

## Diagrams

The `uml` directory contains diagrams of the data model, components, and service interactions.

[UML diagram index](uml/README.md).

## Related materials

See [plan.md](plan.md) for the purpose of interface illustrations and links to the relevant sections.

Restart the processes that use the settings after changing them.

## Additional module guides

- [Accounts module: technical design](accounts-module/design.md).
- [Removing account scenarios and capabilities](accounts-module/profile-removal.md).
- [Accounts module: extraction specification](accounts-module/spec.md).
- [CRM administration dashboard](admin-dashboard-analytics.md).
- [Subscription payments and self-employed receipts](admin-finance.md).
- [Organizations in the OpenCRM desktop admin interface](admin-organizations.md).
- [Subscription management in the OpenCRM admin interfaces](admin-subscriptions.md).
- [OpenCRM server-side AI assistant](ai-assistant.md).
- [Customer Profile](client-card.md).
- [Customer conversations](communications.md).
- [Shared CRM Shell](crm-workspace-shell.md).
- [Vehicle Intake and Before/After Photos](detailing-intake.md).
- [Organization Workday](detailing-workday.md).
- [Required email confirmation](email-confirmation.md).
- [Employee Shifts and Time Tracking in OpenCRM](employee-shifts-design.md).
- [Implementation: Employee Workspace and Administrator Oversight](employee-workplace-implementation.md).
- [Employee Workspace](employee-workplace.md).
- [Form and Section Headers](form-headers.md).
- [Manual Inquiries Before Booking: Phase Two Design](inquiries-design.md).
- [Manual Inquiries Before Booking: Implementation](inquiries-implementation.md).
- [CRM Navigation: Phase One](navigation.md).
- [Payment service boundaries](payments-service-design.md).
- [OpenCRM overview](project-overview.md).
- [Shared simplified Detailing and Service Station workflow](simple-maintenance-workflow.md).
- [Service Station and Fleet development](sto-fleet-development.md).
- [Organization Team and Access](team-and-access-design.md).
- [Voice pilot commands for opencrm.example.invalid](voice-ai-actions.md).
