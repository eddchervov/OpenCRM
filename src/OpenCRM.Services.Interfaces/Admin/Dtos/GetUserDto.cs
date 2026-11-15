namespace OpenCRM.Services.Interfaces.Admin.Dtos
{
    /// <summary> Сведения о пользователе для административной панели. </summary>
    public class GetUserDto
    {
        /// <summary> Идентификатор пользователя. </summary>
        public string Id { get; set; } = default!;

        /// <summary> Телефон пользователя, если указан. </summary>
        public string? Phone { get; set; } = default!;

        /// <summary> Признак подтверждённого телефона. </summary>
        public bool PhoneConfirmed { get; set; }

        /// <summary> Текущий SMS-код пользователя, если есть. </summary>
        public string? SMSCode { get; set; }

        /// <summary> Email пользователя, если указан. </summary>
        public string? Email { get; set; } = default!;

        /// <summary> Признак подтверждённого email. </summary>
        public bool EmailConfirmed { get; set; }

        /// <summary> Признак наличия пароля у пользователя. </summary>
        public bool HasPassword { get; set; } 

        /// <summary> Признак доступности блокировки учётной записи. </summary>
        public bool LockoutEnabled { get; set; } 

        /// <summary> Время создания учётной записи. </summary>
        public DateTime DatetimeCreation { get; set; }

        /// <summary> Время последнего входа в приложение, если было. </summary>
        public DateTime? LastDateOfEntry { get; set; }

        /// <summary> Количество посещений. </summary>
        public int CountVisits { get; set; }

        /// <summary> Дата последнего входа, если была. </summary>
        public DateTime? LastLoginDate { get; set; }

        /// <summary> Количество входов. </summary>
        public int CountLogins { get; set; }

        /// <summary> UTM-источник первой регистрации. </summary>
        public string? FirstUtmSource { get; set; }

        /// <summary> UTM-канал первой регистрации. </summary>
        public string? FirstUtmMedium { get; set; }

        /// <summary> Количество записей техники пользователя. </summary>
        public int CountVehicles { get; set; }

        /// <summary> Количество записей об обслуживании пользователя. </summary>
        public int CountMaintenance { get; set; }

        /// <summary> Количество организаций пользователя. </summary>
        public int OrganizationsCount { get; set; }

        /// <summary> Признак разрешения использования тестовых подписок. </summary>
        public bool CanUseTestSubscriptions { get; set; }

        /// <summary> Название текущей подписки, если есть. </summary>
        public string? CurrentSubscriptionName { get; set; }

        /// <summary> Дата окончания текущей подписки, если есть. </summary>
        public DateTime? CurrentSubscriptionEndDate { get; set; }

        /// <summary> Признак того, что текущая подписка демонстрационная. </summary>
        public bool CurrentSubscriptionIsDemo { get; set; }

        /// <summary> Название фактически действующего плана. </summary>
        public string? EffectivePlanName { get; set; }

        /// <summary> Тип фактически действующего плана. </summary>
        public string EffectivePlanType { get; set; } = "none";

        /// <summary> Количество активных предоставленных возможностей. </summary>
        public int ActiveFeatureGrantsCount { get; set; }

        /// <summary> Признак того, что пользователь помечен удалённым. </summary>
        public bool IsDeleted { get; set; }
    }
}


