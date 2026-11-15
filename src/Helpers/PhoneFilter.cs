namespace Helpers
{
    /// <summary> Утилита очистки строки телефонного номера. </summary>
    public static class PhoneFilter
    {
        /// <summary> Оставляет в номере телефона только допустимые символы. </summary>
        public static string Execute(string? phone)
        {
            if (string.IsNullOrEmpty(phone))
            {
                return "";
            }
            if (phone.StartsWith("8")) { phone = phone.Substring(1); phone = "7" + phone; }
            phone = phone.Replace("(", "").Replace(")", "").Replace(" ", "").Replace("-", "").Replace("+", "");
            return phone;

        }
    }
}
