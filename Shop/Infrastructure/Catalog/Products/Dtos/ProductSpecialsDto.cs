namespace Shop.Infrastructure.Catalog.Products.Dtos;

internal readonly record struct ProductSpecialsDto(
    List<ProductSummaryDto> TopFeatured,
    List<ProductSummaryDto> TopSales,
    List<ProductSummaryDto> TopSelling
)
{
    public static ProductSpecialsDto Empty() =>
        new( [], [], [] );
}