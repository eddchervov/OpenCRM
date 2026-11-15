# Service duration and appointment scheduling

**English** | [Русский](service-duration.ru.md)

Service and service-set directories now have an optional “Duration, min” field. It specifies the continuous time for which a bay is occupied, including drying and waiting. The value is an integer from 1 to 525600; an empty field means the duration is unknown. Lists and selection menus display time in hours and minutes. Service duration is also available in the Detailing setup wizard.

When creating an appointment, the planned end date and time are calculated after the start and services are selected. Durations are summed using each service quantity. A complete service set selected from the menu can have its own duration; if none is set, the durations of its component services are used. If the complete set is not part of the appointment, the remaining individual services are used. Shared services in overlapping sets are counted only once. Additional units of a service increase the duration separately.

If the duration of any required service is unknown, an exact calculation is unavailable. A previously auto-calculated end time is cleared; a manually entered end time is retained. When a user changes the end date or time, automatic recalculation is disabled. The “Calculate end time” button explicitly resumes calculation from the services.

Automatic recalculation is disabled for existing appointments: opening the editor, changing the directory, or changing the service list does not itself move the schedule. A service set is expanded into individual services as before; its overridden duration is used when the set is selected in the current form. Specific planned appointment dates and times are saved. An explicit recalculation in the editor uses the available service composition because the stored rows do not retain their historical set membership.

Nighttime and non-working hours count toward duration: this is bay occupancy, not technician labor time. Occupancy is checked by the existing interval validation, including across midnight. An appointment cannot be saved if it conflicts. Public pages are unchanged; the calculation is used in shared organization forms and quick Detailing and Service Station appointments.

## Migration

`20260916122918_AddServiceDuration` (EF Core 10.0.7) adds nullable integer `DurationMinutes` to `OrganizationScenarioCatalogItems`. Existing entries remain empty; maintenance records are unchanged. DbContext/Migration metadata in Designer, the snapshot, the build, model consistency, and application of the migration chain to a separate test PostgreSQL instance were checked.

`Down` removes only this column: directory durations are lost, while saved planned appointment dates and times remain.

## Verification

Server tests verify saving, updating, and clearing duration, invalid values, service sets, preservation of an existing appointment schedule, and saving through the wizard. A browser check of the built client with a test API covers duration summation and quantities, midnight, manual end time, unknown duration, service sets, bay conflicts, submitted dates and times, existing appointments, and light/dark themes at widths of 320–1440 px.
