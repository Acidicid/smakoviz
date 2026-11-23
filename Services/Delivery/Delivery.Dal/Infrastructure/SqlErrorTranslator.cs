using Delivery.Domain.Exceptions;
using Microsoft.Data.SqlClient;

namespace Delivery.Dal.Infrastructure;

/// <summary>
/// Перекладає помилки SQL Server у доменні винятки. «Сирі» SQL-повідомлення
/// (з іменами таблиць і обмежень) ніколи не доходять до клієнта API.
///
///   Номер   Звідки                                   Доменний виняток            HTTP
///   50001   THROW у процедурах: не знайдено          NotFoundException           404
///   50002   usp_Order_ChangeStatus: перехід          BusinessConflictException   409
///   50003   usp_Order_ChangeStatus: RowVersion       ConcurrencyConflictException 409
///   50004   бізнес-правило в процедурах              BusinessRuleException       422
///   2601,   порушення UNIQUE (email, номер           BusinessConflictException   409
///   2627    замовлення)
///   547     порушення FK / CHECK                     BusinessRuleException       422
/// </summary>
public static class SqlErrorTranslator
{
    public static Exception? Translate(SqlException ex) => ex.Number switch
    {
        50001 => new NotFoundException(ex.Message),
        50002 => new BusinessConflictException(ex.Message),
        50003 => new ConcurrencyConflictException(ex.Message),
        50004 => new BusinessRuleException(ex.Message),
        2601 or 2627 => new BusinessConflictException("Record with the same unique key already exists"),
        547 => new BusinessRuleException("Operation violates a data integrity rule (foreign key or check constraint)"),
        _ => null // інші помилки (мережа, таймаут) — летять далі як є і стануть 500
    };
}
