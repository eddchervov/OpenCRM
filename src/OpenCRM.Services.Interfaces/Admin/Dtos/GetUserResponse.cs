namespace OpenCRM.Services.Interfaces.Admin.Dtos
{
    /// <summary> Страница пользователей для административной панели. </summary>
    public class GetUserResponse
    {
        /// <summary> Пользователи на странице. </summary>
        public required IEnumerable<GetUserDto> Users { get; set; }

        /// <summary> Общее количество пользователей. </summary>
        public int AllCount { get; set; }

        /// <summary> Номер страницы. </summary>
        public int Page { get; set; }

        /// <summary> Размер страницы. </summary>
        public int PageSize { get; set; }
    }
}

