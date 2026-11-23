using AutoMapper;
using Delivery.Bll.Dtos;
using Delivery.Domain.Models;

namespace Delivery.Bll.Mapping;

/// <summary>
/// Усі мапінги «модель → DTO» в одному місці. Конфігурацію перевіряє
/// тест AssertConfigurationIsValid (Delivery.Tests): якщо в DTO з'явиться поле,
/// якого немає в моделі, тест впаде ще до запуску застосунку.
/// </summary>
public class DeliveryProfile : Profile
{
    public DeliveryProfile()
    {
        // ROWVERSION (byte[8]) → рядок base64 для JSON
        CreateMap<byte[], string>().ConvertUsing(bytes => Convert.ToBase64String(bytes));

        CreateMap<Customer, CustomerDto>();
        CreateMap<Dish, DishDto>();

        CreateMap<DeliveryDetails, DeliveryDetailsDto>();
        CreateMap<OrderLine, OrderLineDto>();
        CreateMap<OrderStatusChange, OrderStatusChangeDto>();
        CreateMap<DeliveryOrder, OrderDto>();
        CreateMap<DeliveryOrder, OrderSummaryDto>();
    }
}
