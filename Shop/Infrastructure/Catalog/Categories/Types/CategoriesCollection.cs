namespace Shop.Infrastructure.Catalog.Categories.Types;

public sealed record CategoriesCollection
{
    public CategoriesCollection( Dictionary<Guid, Category> categories, List<Category> primaryCategories )
    {
        Categories = categories;
        PrimaryCategories = primaryCategories;
    }
    
    public readonly IReadOnlyDictionary<Guid, Category> Categories = new Dictionary<Guid, Category>();
    public readonly IReadOnlyList<Category> PrimaryCategories = new List<Category>();

    public static CategoriesCollection From( List<CategoryDto> dtos )
    {
        Dictionary<Guid, Category> dictionary = [];
        List<Category> primaries = [];
        
        foreach ( CategoryDto d in dtos ) {
            Category c = Category.From( d );
            dictionary.TryAdd( c.Id, c );
            if (c.ParentId is null)
                primaries.Add( c );
        }

        foreach ( Category c in dictionary.Values ) {
            if (c.ParentId is null)
                continue;
            Category parent = dictionary[c.ParentId.Value];
            parent.Children.Add( c );
        }
        
        return new CategoriesCollection( dictionary, primaries );
    }
    public static CategoriesCollection None() => 
        new( [], [] );
}