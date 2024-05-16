namespace ShopApplication.Types.Identity;

internal readonly record struct Authentication(
    string JwtToken )
{
    internal static Authentication With( string jwtToken ) =>
        new( jwtToken );
}