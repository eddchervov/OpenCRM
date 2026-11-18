using System.Runtime.InteropServices;

namespace OpenCRM.Admin.Launcher;

/// <summary> Брендирование окна Chrome иконками и свойствами панели задач. </summary>
internal static class ChromeWindowBranding
{
    /// <summary> Индекс крупной иконки. </summary>
    private const int LargeIcon = 1;

    /// <summary> Индекс мелкой иконки. </summary>
    private const int SmallIcon = 0;

    /// <summary> Количество извлекаемых иконок. </summary>
    private const uint IconCount = 1;

    /// <summary> Сообщение для установки иконки окна. </summary>
    private const uint SetIconMessage = 0x0080;

    /// <summary> Код типа строкового значения PropVariant. </summary>
    private const ushort StringVariantType = 31;

    /// <summary> GUID формата AppUserModel для панели задач. </summary>
    private static readonly Guid AppUserModelFormatId = new("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3");

    /// <summary> GUID интерфейса хранилища свойств окна. </summary>
    private static readonly Guid PropertyStoreInterfaceId = new("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99");

    /// <summary> Применяет иконки и свойства панели задач к окну. </summary>
    public static void Apply(IntPtr windowHandle, string displayName)
    {
        try
        {
            ApplyWindowIcons(windowHandle);
            ApplyTaskbarProperties(windowHandle, displayName);
        }
        catch (Exception exception)
        {
            DesktopLog.Write($"Не удалось применить оформление {displayName} к окну: {exception.Message}");
        }
    }

    /// <summary> Устанавливает иконки окна. </summary>
    private static void ApplyWindowIcons(IntPtr windowHandle)
    {
        var executablePath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            return;
        }

        var largeIcons = new IntPtr[1];
        var smallIcons = new IntPtr[1];
        var extractedIconCount = ExtractIconEx(executablePath, 0, largeIcons, smallIcons, IconCount);
        if (extractedIconCount == 0)
        {
            return;
        }

        SendMessage(windowHandle, SetIconMessage, LargeIcon, largeIcons[0]);
        SendMessage(windowHandle, SetIconMessage, SmallIcon, smallIcons[0]);
    }

    /// <summary> Задаёт свойства окна для панели задач. </summary>
    private static void ApplyTaskbarProperties(IntPtr windowHandle, string displayName)
    {
        var propertyStoreInterfaceId = PropertyStoreInterfaceId;
        var result = SHGetPropertyStoreForWindow(
            windowHandle,
            ref propertyStoreInterfaceId,
            out var propertyStore);

        Marshal.ThrowExceptionForHR(result);

        try
        {
            var executablePath = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(executablePath))
            {
                return;
            }

            SetStringProperty(propertyStore, 5, "OpenCRM.Admin.Desktop");
            SetStringProperty(propertyStore, 2, $"\"{executablePath}\"");
            SetStringProperty(propertyStore, 3, $"{executablePath},0");
            SetStringProperty(propertyStore, 4, $"{displayName} — {LauncherText.Get("Administration")}");
            Marshal.ThrowExceptionForHR(propertyStore.Commit());
        }
        finally
        {
            Marshal.ReleaseComObject(propertyStore);
        }
    }

    /// <summary> Записывает строковое свойство в хранилище окна. </summary>
    private static void SetStringProperty(
        IPropertyStore propertyStore,
        uint propertyId,
        string value)
    {
        var propertyKey = new PropertyKey(AppUserModelFormatId, propertyId);
        var propertyValue = PropVariant.FromString(value);

        try
        {
            Marshal.ThrowExceptionForHR(propertyStore.SetValue(ref propertyKey, ref propertyValue));
        }
        finally
        {
            PropVariantClear(ref propertyValue);
        }
    }

    /// <summary> WinAPI-функция извлечения иконок из файла. </summary>
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern uint ExtractIconEx(
        string fileName,
        int iconIndex,
        IntPtr[] largeIcons,
        IntPtr[] smallIcons,
        uint iconCount);

    /// <summary> WinAPI-функция получения хранилища свойств окна. </summary>
    [DllImport("shell32.dll")]
    private static extern int SHGetPropertyStoreForWindow(
        IntPtr windowHandle,
        ref Guid interfaceId,
        [MarshalAs(UnmanagedType.Interface)] out IPropertyStore propertyStore);

    /// <summary> WinAPI-функция освобождения значения PropVariant. </summary>
    [DllImport("ole32.dll")]
    private static extern int PropVariantClear(ref PropVariant propertyValue);

    /// <summary> WinAPI-функция отправки сообщения окну. </summary>
    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(
        IntPtr windowHandle,
        uint message,
        int messageParameter,
        IntPtr valueParameter);

    /// <summary> COM-интерфейс хранилища свойств окна. </summary>
    [ComImport]
    [Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        /// <summary> Возвращает количество свойств. </summary>
        int GetCount(out uint propertyCount);

        /// <summary> Возвращает ключ свойства по индексу. </summary>
        int GetAt(uint propertyIndex, out PropertyKey propertyKey);

        /// <summary> Читает значение свойства. </summary>
        int GetValue(ref PropertyKey propertyKey, out PropVariant propertyValue);

        /// <summary> Записывает значение свойства. </summary>
        int SetValue(ref PropertyKey propertyKey, ref PropVariant propertyValue);

        /// <summary> Фиксирует изменения свойств. </summary>
        int Commit();
    }

    /// <summary> Ключ свойства Windows API. </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private readonly struct PropertyKey(Guid formatId, uint propertyId)
    {
        /// <summary> GUID формата ключа свойства. </summary>
        public readonly Guid FormatId = formatId;

        /// <summary> Идентификатор свойства. </summary>
        public readonly uint PropertyId = propertyId;
    }

    /// <summary> Структура PropVariant для значений свойств. </summary>
    [StructLayout(LayoutKind.Explicit)]
    private struct PropVariant
    {
        /// <summary> Тип варианта значения. </summary>
        [FieldOffset(0)]
        public ushort VariantType;

        /// <summary> Указатель на значение. </summary>
        [FieldOffset(8)]
        public IntPtr PointerValue;

        /// <summary> Создаёт PropVariant из строки. </summary>
        public static PropVariant FromString(string value)
        {
            return new PropVariant
            {
                VariantType = StringVariantType,
                PointerValue = Marshal.StringToCoTaskMemUni(value)
            };
        }
    }
}
