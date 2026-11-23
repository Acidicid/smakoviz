using AutoMapper;
using Delivery.Bll.Dtos;
using Delivery.Dal.Abstractions;
using Delivery.Domain.Exceptions;
using Delivery.Domain.Models;

namespace Delivery.Bll.Services;

public sealed class CustomerService(IUnitOfWork uow, IMapper mapper) : ICustomerService
{
    public async Task<CustomerDto> GetByIdAsync(long id, CancellationToken ct = default)
    {
        var customer = await uow.Customers.GetByIdAsync(id, ct)
            ?? throw new NotFoundException($"Customer {id} not found");
        return mapper.Map<CustomerDto>(customer);
    }

    public async Task<PagedResult<CustomerDto>> SearchAsync(string? query, int page, int pageSize, CancellationToken ct = default)
    {
        ServiceGuards.Paging(page, pageSize);
        var result = await uow.Customers.SearchAsync(query, page, pageSize, ct);
        return new PagedResult<CustomerDto>(
            mapper.Map<List<CustomerDto>>(result.Items), result.Page, result.PageSize, result.TotalCount);
    }

    public async Task<CustomerDto> CreateAsync(CreateCustomerDto input, CancellationToken ct = default)
    {
        var email = input.Email.Trim().ToLowerInvariant();

        // Бізнес-валідація (потрібен стан БД): email має бути унікальним.
        // Друга лінія захисту — UNIQUE-обмеження в БД (на випадок паралельних запитів).
        if (await uow.Customers.GetByEmailAsync(email, ct) is not null)
            throw new BusinessConflictException($"Customer with email {email} already exists");

        var id = await uow.Customers.AddAsync(new Customer
        {
            Email = email,
            FullName = input.FullName.Trim(),
            Phone = input.Phone,
            CreatedBy = ServiceGuards.CurrentUser
        }, ct);

        return await GetByIdAsync(id, ct);
    }

    public async Task<CustomerDto> UpdateAsync(long id, UpdateCustomerDto input, CancellationToken ct = default)
    {
        var rowVersion = ServiceGuards.RowVersion(input.RowVersion);
        var email = input.Email.Trim().ToLowerInvariant();

        var sameEmail = await uow.Customers.GetByEmailAsync(email, ct);
        if (sameEmail is not null && sameEmail.Id != id)
            throw new BusinessConflictException($"Email {email} is already used by another customer");

        await uow.Customers.UpdateAsync(new Customer
        {
            Id = id,
            Email = email,
            FullName = input.FullName.Trim(),
            Phone = input.Phone,
            UpdatedBy = ServiceGuards.CurrentUser
        }, rowVersion, ct);   // не той RowVersion → ConcurrencyConflictException (409)

        return await GetByIdAsync(id, ct);
    }

    public async Task DeleteAsync(long id, CancellationToken ct = default)
    {
        if (!await uow.Customers.SoftDeleteAsync(id, ServiceGuards.CurrentUser, ct))
            throw new NotFoundException($"Customer {id} not found");
    }
}
