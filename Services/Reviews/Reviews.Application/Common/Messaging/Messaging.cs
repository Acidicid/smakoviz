using MediatR;

namespace Reviews.Application.Common.Messaging;

// Базові інтерфейси CQRS поверх MediatR.
// Команда ЗМІНЮЄ стан (Create/Update/Delete), запит лише ЧИТАЄ. Різні інтерфейси роблять
// це видимим у коді: обробник запиту ніколи не викликає репозиторій запису.

/// <summary>Команда без результату (напр. Delete).</summary>
public interface ICommand : IRequest;

/// <summary>Команда з результатом (напр. Id створеного документа).</summary>
public interface ICommand<out TResponse> : IRequest<TResponse>;

/// <summary>Запит на читання.</summary>
public interface IQuery<out TResponse> : IRequest<TResponse>;
