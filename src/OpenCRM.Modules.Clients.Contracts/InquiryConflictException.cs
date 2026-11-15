namespace OpenCRM.Modules.Clients.Contracts;

/// <summary> Конфликт версии или состояния обращения при конкурентном изменении. </summary>
public sealed class InquiryConflictException(string message) : InvalidOperationException(message);
