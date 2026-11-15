using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using OpenCRM.Domain;
using OpenCRM.Domain.Assistant;
using OpenCRM.Domain.Identity;
using System.Reflection;

namespace OpenCRM.Infrastructure.Data;

/// <summary> Контекст базы данных приложения на базе Identity. </summary>
public sealed class OpenCRMContext : IdentityDbContext<ApplicationUser, IdentityRole, string>
{

#nullable disable

    /// <summary> Набор пользователей приложения. </summary>
    public DbSet<ApplicationUser> ApplicationUsers { get; set; }

    public DbSet<AssistantSettings> AssistantSettingsRecords { get; set; }

    public DbSet<AssistantOrganizationAccess> AssistantOrganizationAccessRecords { get; set; }

    public DbSet<AssistantConversation> AssistantConversations { get; set; }

    public DbSet<AssistantMessage> AssistantMessages { get; set; }

    public DbSet<AssistantDraft> AssistantDrafts { get; set; }

    public DbSet<AssistantUsage> AssistantUsageRecords { get; set; }

    public DbSet<AssistantTelegramBinding> AssistantTelegramBindings { get; set; }

    public DbSet<AssistantTelegramLink> AssistantTelegramLinks { get; set; }

    public DbSet<AssistantTelegramUpdate> AssistantTelegramUpdates { get; set; }

    public DbSet<AssistantAudit> AssistantAuditRecords { get; set; }

    /// <summary> Набор разрешений ролей администратора. </summary>
    public DbSet<AdminRolePermission> AdminRolePermissions { get; set; }

    /// <summary> Набор отметок просмотра разделов административной панели. </summary>
    public DbSet<AdminNavigationReadState> AdminNavigationReadStates { get; set; }

    /// <summary> Набор аудитов статусов платежей. </summary>
    public DbSet<AdminPaymentStatusAudit> AdminPaymentStatusAudits { get; set; }

    /// <summary> Набор аудитов чеков НПД. </summary>
    public DbSet<AdminNpdReceiptAudit> AdminNpdReceiptAudits { get; set; }

    /// <summary> Набор аудитов подписок. </summary>
    public DbSet<AdminSubscriptionAudit> AdminSubscriptionAudits { get; set; }

    /// <summary> Набор фотографий обслуживаний. </summary>
    public DbSet<MaintenancePhoto> MaintenancePhotos { get; set; }

    /// <summary> Повреждения, отмеченные при приёмке. </summary>
    public DbSet<MaintenanceIntakeDamage> MaintenanceIntakeDamages { get; set; }

    /// <summary> Фотографии повреждений приёмки. </summary>
    public DbSet<MaintenanceIntakeDamagePhoto> MaintenanceIntakeDamagePhotos { get; set; }

    /// <summary> Набор фотографий техники. </summary>
    public DbSet<VehiclePhoto> VehiclePhotos { get; set; }

    /// <summary> Набор фотоальбомов техники. </summary>
    public DbSet<VehiclePhotoAlbum> VehiclePhotoAlbums { get; set; }

    /// <summary> Набор истории отправки SMS. </summary>
    public DbSet<SmsSendingHistory> SmsSendingHistories { get; set; }

    /// <summary> Набор марок техники. </summary>
    public DbSet<Brand> Brands { get; set; }

    /// <summary> Набор моделей техники. </summary>
    public DbSet<Model> Models { get; set; }

    /// <summary> Набор предложений в каталог техники. </summary>
    public DbSet<VehicleCatalogSuggestion> VehicleCatalogSuggestions { get; set; }

    /// <summary> Набор техники. </summary>
    public DbSet<Vehicle> Vehicles { get; set; }

    /// <summary> Набор событий истории техники. </summary>
    public DbSet<VehicleHistoryEvent> VehicleHistoryEvents { get; set; }

    /// <summary> Набор обслуживаний. </summary>
    public DbSet<Maintenance> Maintenance { get; set; }

    /// <summary> Набор строк работ и услуг обслуживаний. </summary>
    public DbSet<MaintenanceDetail> MaintenanceDetails { get; set; }

    /// <summary> Готовые финансовые показатели одного обслуживания. </summary>
    public DbSet<MaintenanceFinancialTotal> MaintenanceFinancialTotals { get; set; }

    /// <summary> Производные почасовые суммы услуг для быстрого рейтинга. </summary>
    public DbSet<OrganizationServiceHourlyTotal> OrganizationServiceHourlyTotals { get; set; }

    /// <summary> Набор событий истории обслуживаний. </summary>
    public DbSet<MaintenanceHistoryEvent> MaintenanceHistoryEvents { get; set; }

    /// <summary> Набор документов обслуживаний. </summary>
    public DbSet<MaintenanceDocument> MaintenanceDocuments { get; set; }

    /// <summary> Набор платежей обслуживаний. </summary>
    public DbSet<MaintenancePayment> MaintenancePayments { get; set; }

    /// <summary> Набор операций работ по обслуживанию. </summary>
    public DbSet<MaintenanceWorkOperation> MaintenanceWorkOperations { get; set; }

    /// <summary> Набор событий работ по обслуживанию. </summary>
    public DbSet<MaintenanceWorkEvent> MaintenanceWorkEvents { get; set; }

    /// <summary> Набор интервалов работ по обслуживанию. </summary>
    public DbSet<MaintenanceWorkInterval> MaintenanceWorkIntervals { get; set; }

    /// <summary> Набор отчётов о работах по обслуживанию. </summary>
    public DbSet<MaintenanceWorkReport> MaintenanceWorkReports { get; set; }

    /// <summary> Набор сообщений в поддержку. </summary>
    public DbSet<SupportMessage> SupportMessages { get; set; }

    /// <summary> Набор записей переписки поддержки. </summary>
    public DbSet<SupportMessageEntry> SupportMessageEntries { get; set; }

    /// <summary> Метаданные приватных вложений поддержки. </summary>
    public DbSet<SupportAttachment> SupportAttachments { get; set; }

    /// <summary> Набор email-сообщений. </summary>
    public DbSet<EmailMessage> EmailMessages { get; set; }

    /// <summary> Набор исходящих Telegram-уведомлений. </summary>
    public DbSet<TelegramNotificationOutbox> TelegramNotificationOutbox { get; set; }

    /// <summary> Набор событий email-сообщений. </summary>
    public DbSet<EmailMessageEvent> EmailMessageEvents { get; set; }

    /// <summary> Набор email-рассылок. </summary>
    public DbSet<EmailCampaign> EmailCampaigns { get; set; }

    /// <summary> Набор получателей email-рассылок. </summary>
    public DbSet<EmailCampaignRecipient> EmailCampaignRecipients { get; set; }

    /// <summary> Набор тарифных планов. </summary>
    public DbSet<SubscriptionPlan> SubscriptionPlans { get; set; }

    /// <summary> Набор определений возможностей. </summary>
    public DbSet<FeatureDefinition> FeatureDefinitions { get; set; }

    /// <summary> Набор возможностей тарифных планов. </summary>
    public DbSet<SubscriptionPlanFeature> SubscriptionPlanFeatures { get; set; }

    /// <summary> Набор предоставленных пользователям возможностей. </summary>
    public DbSet<UserFeatureGrant> UserFeatureGrants { get; set; }

    /// <summary> Набор подписок пользователей. </summary>
    public DbSet<UserSubscription> UserSubscriptions { get; set; }

    /// <summary> Набор подписок организаций. </summary>
    public DbSet<OrganizationSubscription> OrganizationSubscriptions { get; set; }

    /// <summary> Набор напоминаний об истечении подписок. </summary>
    public DbSet<SubscriptionExpiryReminder> SubscriptionExpiryReminders { get; set; }

    /// <summary> Набор платежей за подписки. </summary>
    public DbSet<SubscriptionPayment> SubscriptionPayments { get; set; }

    public DbSet<SubscriptionPurchase> SubscriptionPurchases { get; set; }

    public DbSet<SubscriptionPaymentCommand> SubscriptionPaymentCommands { get; set; }

    public DbSet<SubscriptionPaymentEvent> SubscriptionPaymentEvents { get; set; }

    public DbSet<SubscriptionPaymentAdminAction> SubscriptionPaymentAdminActions { get; set; }

    /// <summary> Набор операций создания платежей. </summary>
    public DbSet<PaymentCreationOperation> PaymentCreationOperations { get; set; }

    /// <summary> Набор промокодов. </summary>
    public DbSet<PromoCode> PromoCodes { get; set; }

    /// <summary> Набор планов промокодов. </summary>
    public DbSet<PromoCodePlan> PromoCodePlans { get; set; }

    /// <summary> Набор погашений промокодов. </summary>
    public DbSet<PromoCodeRedemption> PromoCodeRedemptions { get; set; }

    /// <summary> Набор сценариев промокодов. </summary>
    public DbSet<PromoCodeScenario> PromoCodeScenarios { get; set; }

    /// <summary> Набор применений промокодов. </summary>
    public DbSet<PromoCodeApplication> PromoCodeApplications { get; set; }

    /// <summary> Набор историй подписок пользователей. </summary>
    public DbSet<UserSubscriptionHistory> UserSubscriptionHistory { get; set; }

    /// <summary> Каталог поддерживаемых типов организаций. </summary>
    public DbSet<OrganizationTypeDefinition> OrganizationTypeDefinitions { get; set; }

    /// <summary> Набор организаций. </summary>
    public DbSet<Organization> Organizations { get; set; }

    /// <summary> Набор рабочих дней организаций. </summary>
    public DbSet<OrganizationWorkingDay> OrganizationWorkingDays { get; set; }

    /// <summary> Набор перерывов рабочих дней организаций. </summary>
    public DbSet<OrganizationWorkingDayBreak> OrganizationWorkingDayBreaks { get; set; }

    /// <summary> Набор сотрудников организаций. </summary>
    public DbSet<OrganizationEmployee> OrganizationEmployees { get; set; }

    /// <summary> Набор смен сотрудников организаций. </summary>
    public DbSet<OrganizationEmployeeShift> OrganizationEmployeeShifts { get; set; }

    /// <summary> Набор участников организаций. </summary>
    public DbSet<OrganizationMember> OrganizationMembers { get; set; }

    /// <summary> Набор приглашений в организации. </summary>
    public DbSet<OrganizationInvitation> OrganizationInvitations { get; set; }

    /// <summary> Набор событий аудита организаций. </summary>
    public DbSet<OrganizationAuditEvent> OrganizationAuditEvents { get; set; }

    /// <summary> Набор переопределений разрешений ролей организаций. </summary>
    public DbSet<OrganizationRolePermissionOverride> OrganizationRolePermissionOverrides { get; set; }

    /// <summary> Набор клиентов организаций. </summary>
    public DbSet<OrganizationClient> OrganizationClients { get; set; }

    /// <summary> Набор ручных обращений организаций. </summary>
    public DbSet<OrganizationInquiry> OrganizationInquiries { get; set; }

    /// <summary> Рекомендации повторных визитов организаций. </summary>
    public DbSet<OrganizationRepeatVisit> OrganizationRepeatVisits { get; set; }

    /// <summary> Набор событий истории ручных обращений. </summary>
    public DbSet<OrganizationInquiryEvent> OrganizationInquiryEvents { get; set; }

    /// <summary> Диалоги Telegram с клиентами организаций. </summary>
    public DbSet<OrganizationConversation> OrganizationConversations { get; set; }

    /// <summary> Сообщения диалогов организаций. </summary>
    public DbSet<OrganizationConversationMessage> OrganizationConversationMessages { get; set; }

    /// <summary> Вложения сообщений диалогов организаций. </summary>
    public DbSet<OrganizationConversationAttachment> OrganizationConversationAttachments { get; set; }

    /// <summary> Позиции прочтения диалогов сотрудниками. </summary>
    public DbSet<OrganizationConversationReadState> OrganizationConversationReadStates { get; set; }

    /// <summary> Очередь публикации wakeup-событий исходящих сообщений. </summary>
    public DbSet<OrganizationConversationDispatch> OrganizationConversationDispatches { get; set; }

    public DbSet<OrganizationChannelConnection> OrganizationChannelConnections { get; set; }

    public DbSet<OrganizationClientChannelLink> OrganizationClientChannelLinks { get; set; }

    public DbSet<MessageTemplateGroup> MessageTemplateGroups { get; set; }

    public DbSet<MessageTemplate> MessageTemplates { get; set; }

    /// <summary> Набор ботов организаций в MAX. </summary>
    public DbSet<OrganizationMaxBot> OrganizationMaxBots { get; set; }

    /// <summary> Набор уведомлений клиентов в MAX. </summary>
    public DbSet<MaxClientNotification> MaxClientNotifications { get; set; }

    /// <summary> Набор Telegram-ботов организаций. </summary>
    public DbSet<OrganizationTelegramBot> OrganizationTelegramBots { get; set; }

    /// <summary> Набор уведомлений клиентов в Telegram. </summary>
    public DbSet<TelegramClientNotification> TelegramClientNotifications { get; set; }

    /// <summary> Набор пакетов импорта CRM. </summary>
    public DbSet<CrmImportBatch> CrmImportBatches { get; set; }

    /// <summary> Набор событий истории клиентов организаций. </summary>
    public DbSet<OrganizationClientHistoryEvent> OrganizationClientHistoryEvents { get; set; }

    /// <summary> Набор тегов клиентов организаций. </summary>
    public DbSet<OrganizationClientTag> OrganizationClientTags { get; set; }

    /// <summary> Набор привязок тегов к клиентам организаций. </summary>
    public DbSet<OrganizationClientTagAssignment> OrganizationClientTagAssignments { get; set; }

    /// <summary> Набор дефектов техники. </summary>
    public DbSet<VehicleDefect> VehicleDefects { get; set; }

    /// <summary> Набор дефектов обслуживаний. </summary>
    public DbSet<MaintenanceDefect> MaintenanceDefects { get; set; }

    /// <summary> Набор записей счётчиков техники. </summary>
    public DbSet<VehicleCounterEntry> VehicleCounterEntries { get; set; }

    /// <summary> Набор состояний задач планировщика организаций. </summary>
    public DbSet<OrganizationSchedulerTaskState> OrganizationSchedulerTaskStates { get; set; }

    /// <summary> Набор задач планировщика организаций. </summary>
    public DbSet<OrganizationSchedulerTask> OrganizationSchedulerTasks { get; set; }

    /// <summary> Набор элементов каталога сценариев организаций. </summary>
    public DbSet<OrganizationScenarioCatalogItem> OrganizationScenarioCatalogItems { get; set; }

    /// <summary> Набор элементов наборов услуг организаций. </summary>
    public DbSet<OrganizationServiceBundleItem> OrganizationServiceBundleItems { get; set; }

    /// <summary> Набор запасов материалов организаций. </summary>
    public DbSet<OrganizationMaterialStock> OrganizationMaterialStocks { get; set; }

    /// <summary> Набор движений материалов организаций. </summary>
    public DbSet<OrganizationMaterialMovement> OrganizationMaterialMovements { get; set; }

    /// <summary> Набор уведомлений на сайте. </summary>
    public DbSet<SiteNotification> SiteNotifications { get; set; }

    /// <summary> Набор закрытий уведомлений на сайте. </summary>
    public DbSet<SiteNotificationClosure> SiteNotificationClosures { get; set; }

#nullable restore

    /// <summary> Создаёт контекст с заданными параметрами. </summary>
    public OpenCRMContext(DbContextOptions<OpenCRMContext> options)
        : base(options) { }

    /// <summary> Сохраняет изменения и обновляет финансовые итоги и почасовую сводку услуг в общей транзакции. </summary>
    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        var maintenanceIds = MaintenanceFinancialTotalsWriter.GetAffectedMaintenanceIds(this);
        if (maintenanceIds.Length == 0 && !MaintenanceFinancialTotalsWriter.HasRelevantChanges(this))
        {
            return base.SaveChanges(acceptAllChangesOnSuccess);
        }

        return MaintenanceFinancialTotalsWriter.Save(
            this, maintenanceIds, () => base.SaveChanges(false), acceptAllChangesOnSuccess);
    }

    /// <summary> Сохраняет изменения и финансовые итоги асинхронно в общей транзакции. </summary>
    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        var maintenanceIds = MaintenanceFinancialTotalsWriter.GetAffectedMaintenanceIds(this);
        if (maintenanceIds.Length == 0 && !MaintenanceFinancialTotalsWriter.HasRelevantChanges(this))
        {
            return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        }

        return MaintenanceFinancialTotalsWriter.SaveAsync(
            this, maintenanceIds, token => base.SaveChangesAsync(false, token), acceptAllChangesOnSuccess, cancellationToken);
    }

    /// <summary> Применяет конфигурации сущностей к модели. </summary>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.HasPostgresExtension("citext");
        ConfigureIdentitySchema(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());
    }

    /// <summary> Настраивает соглашения модели, включая преобразование дат в UTC. </summary>
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<DateTime>()
            .HaveConversion<DateTimeToUtcConverter>()
            .HaveColumnType("timestamp with time zone");
    }

    /// <summary> Настраивает схему таблиц ASP.NET Identity. </summary>
    /// <param name="modelBuilder">Построитель модели EF Core.</param>
    private static void ConfigureIdentitySchema(ModelBuilder modelBuilder)
    {
        const string identitySchema = "identity";

        modelBuilder.Entity<ApplicationUser>().ToTable("AspNetUsers", identitySchema);
        modelBuilder.Entity<IdentityRole>().ToTable("AspNetRoles", identitySchema);
        modelBuilder.Entity<IdentityUserClaim<string>>().ToTable("AspNetUserClaims", identitySchema);
        modelBuilder.Entity<IdentityUserLogin<string>>().ToTable("AspNetUserLogins", identitySchema);
        modelBuilder.Entity<IdentityUserToken<string>>().ToTable("AspNetUserTokens", identitySchema);
        modelBuilder.Entity<IdentityRoleClaim<string>>().ToTable("AspNetRoleClaims", identitySchema);
        modelBuilder.Entity<IdentityUserRole<string>>().ToTable("AspNetUserRoles", identitySchema);
    }
}


