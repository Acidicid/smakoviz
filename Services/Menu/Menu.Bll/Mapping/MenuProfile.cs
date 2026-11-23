using AutoMapper;
using Menu.Bll.Dtos;
using Menu.Domain.Entities;

namespace Menu.Bll.Mapping;

/// <summary>
/// Усі мапінги «сутність → DTO» в одному місці. Реєструється в Program.cs (AddAutoMapper).
/// Пласкі поля (RestaurantName, CategoryName) AutoMapper заповнює сам з Restaurant.Name / Category.Name.
/// </summary>
public class MenuProfile : Profile
{
    public MenuProfile()
    {
        // ROWVERSION → base64 для JSON
        CreateMap<byte[], string>().ConvertUsing(bytes => Convert.ToBase64String(bytes));

        CreateMap<Restaurant, RestaurantDto>();
        CreateMap<RestaurantProfile, RestaurantProfileDto>();
        CreateMap<Restaurant, RestaurantDetailsDto>();

        CreateMap<DishCategory, DishCategoryDto>();
        CreateMap<Dish, DishSummaryDto>();
        CreateMap<Dish, DishListItemDto>();
        CreateMap<Dish, DishDto>()
            .ForCtorParam(nameof(DishDto.Categories), opt => opt.MapFrom(src => src.DishCategories));

        CreateMap<Category, CategoryDto>();
    }
}
