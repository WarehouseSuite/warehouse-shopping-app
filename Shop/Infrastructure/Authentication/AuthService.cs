using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.IdentityModel.Tokens;
using Shop.Infrastructure.Authentication.Types;
using Shop.Infrastructure.Common.Optionals;
using Shop.Infrastructure.Http;
using Shop.Infrastructure.Storage;
using Shop.Utilities;

namespace Shop.Infrastructure.Authentication;

public sealed class AuthService // Singleton
{
    const string AccessKey = "AccessToken";
    const string RefreshKey = "RefreshToken";

    readonly Jwt _rules;
    readonly HttpService _http;
    readonly IServiceProvider _serviceProvider;

    readonly object _fetchLock = new();
    bool _isFetching = false; // Singleton
    bool _isFirstFetch = true;

    public event Action<Task<AuthenticationState>>? OnStateChanged; 
    public AuthService( IConfiguration config, HttpService http, IServiceProvider serviceProvider )
    {
        _rules = GetJwtRules( config );
        _http = http;
        _serviceProvider = serviceProvider;
    }

    internal async Task<AuthenticationState> GetState()
    {
        while ( _isFetching ) {
            await Task.Delay( 500 );
            Console.WriteLine( "Awaiting another fetch." );
        }

        lock ( _fetchLock )
            _isFetching = true;
        
        StorageService storage = GetStorage();
        
        Reply<string> aTokenResult = await storage.Get<string>( AccessKey );
        if (!aTokenResult.Fail( out aTokenResult ) && !ShouldRefresh( aTokenResult.Data, _rules )) {
            lock ( _fetchLock )
                _isFetching = false;

            if (!_isFirstFetch)
                return new AuthenticationState( GetIdentityClaimsPrincipal( aTokenResult.Data ) );

            _isFirstFetch = false;

            Dictionary<string, object> parameter = [];
            parameter.Add( "AccessToken", aTokenResult.Data );
            Reply<bool> isValid = await _http.TryGetRequest<bool>( Consts.ApiLoginCheck, parameter );
            if (!isValid.IsOkay)
                await ClearAuthentication();
            return new AuthenticationState( new ClaimsPrincipal() );
        }

        _isFirstFetch = false;

        Reply<string> rTokenResult = await storage.Get<string>( RefreshKey );
        if (!rTokenResult.IsOkay) {
            lock ( _fetchLock )
                _isFetching = false;
            return new AuthenticationState( new ClaimsPrincipal() );
        }

        RefreshRequest request = new( aTokenResult.Data, rTokenResult.Data );
        Reply<RefreshReply> refreshResult = await _http.TryPostRequest<RefreshReply>( Consts.ApiLoginRefresh, request );

        if (!refreshResult.IsOkay) {
            lock ( _fetchLock )
                _isFetching = false;
            return new AuthenticationState( new ClaimsPrincipal() );
        }

        Reply<bool> setResult = await SetState( AuthRefreshEventArgs.With( refreshResult.Data.AccessToken, rTokenResult.Data ) );
        lock ( _fetchLock )
            _isFetching = false;
        return !setResult.IsOkay
            ? new AuthenticationState( new ClaimsPrincipal() )
            : new AuthenticationState( GetIdentityClaimsPrincipal( aTokenResult.Data ) );
    }
    internal async Task<Reply<bool>> RefreshState( bool forceRefresh = false )
    {
        StorageService storage = GetStorage();
        
        Reply<string> aTokenResult = await storage.Get<string>( AccessKey );
        if (aTokenResult.Fail( out aTokenResult ) && !forceRefresh && !ShouldRefresh( aTokenResult.Data, _rules ))
            return IReply.None( aTokenResult );
        
        Reply<string> rTokenResult = await storage.Get<string>( RefreshKey );
        if (!rTokenResult.IsOkay)
            return IReply.None( rTokenResult );

        Console.WriteLine( "Refreshing" );

        RefreshRequest request = new( aTokenResult.Data, rTokenResult.Data );
        Reply<RefreshReply> refreshResult = await _http.TryPostRequest<RefreshReply>( Consts.ApiLoginRefresh, request );

        if (!refreshResult.IsOkay)
            return IReply.None( refreshResult );

        return await SetState( AuthRefreshEventArgs.With( refreshResult.Data.AccessToken, rTokenResult.Data ) );
    }
    internal async Task<Reply<bool>> RefreshStateFull()
    {
        StorageService storage = GetStorage();

        Reply<string> aTokenResult = await storage.Get<string>( AccessKey );
        if (aTokenResult.Fail( out aTokenResult ))
            return IReply.None( aTokenResult );

        Reply<string> rTokenResult = await storage.Get<string>( RefreshKey );
        if (!rTokenResult.IsOkay)
            return IReply.None( rTokenResult );

        Console.WriteLine( "Refreshing" );

        RefreshRequest request = new( aTokenResult.Data, rTokenResult.Data );
        Reply<RefreshReply> refreshResult = await _http.TryPostRequest<RefreshReply>( Consts.ApiLoginRefreshFull, request );

        if (!refreshResult.IsOkay)
            return IReply.None( refreshResult );

        return await SetState( AuthRefreshEventArgs.With( refreshResult.Data.AccessToken, rTokenResult.Data ) );
    }
    internal async Task<Reply<bool>> SetState( AuthRefreshEventArgs args )
    {
        if (string.IsNullOrWhiteSpace( args.accessToken ))
            return IReply.None( "Invalid access token" );

        if (string.IsNullOrWhiteSpace( args.refreshToken ))
            return IReply.None( "Invalid refresh token" );

        Reply<bool> setResult = await SetAuthentication( args );
        if (!setResult.IsOkay)
            return IReply.None( "Failed to save authentication" );

        ClaimsPrincipal claims = GetIdentityClaimsPrincipal( args.accessToken );
        OnStateChanged?.Invoke( GetNotifyParams( claims ) );
        return IReply.Okay();
    }
    internal async Task<Reply<bool>> ClearState()
    {
        OnStateChanged?.Invoke( GetNotifyParams( null ) );
        return await ClearAuthentication();
    }
    internal async Task<Reply<string>> GetAccessToken() =>
        await GetStorage().Get<string>( AccessKey );

    async Task<Reply<bool>> SetAuthentication( AuthRefreshEventArgs args )
    {
        StorageService storage = GetStorage();
        
        Reply<bool> accessResult = await storage.Set( AccessKey, args.accessToken );
        Reply<bool> refreshResult = await storage.Set( RefreshKey, args.refreshToken );

        return accessResult.IsOkay && refreshResult.IsOkay
            ? IReply.Okay()
            : IReply.None( accessResult.Message() + refreshResult.Message() );
    }
    async Task<Reply<bool>> ClearAuthentication()
    {
        StorageService storage = GetStorage();
        
        Reply<bool> result1 = await storage.Remove( AccessKey );
        Reply<bool> result2 = await storage.Remove( RefreshKey );

        return result1.IsOkay && result2.IsOkay
            ? IReply.Okay()
            : IReply.None( result1.Message() + result2.Message() );
    }
    
    static bool ShouldRefresh( string token, Jwt rules )
    {
        try {
            Reply<JwtSecurityToken> jwt = ParseJwtFromString( token, rules );
            if (!jwt.IsOkay)
                return true;

            long expiryDateUnix = long.Parse( jwt.Data.Claims.First( claim => claim.Type == "exp" ).Value );
            DateTime expiryDateTime = DateTimeOffset.FromUnixTimeSeconds( expiryDateUnix ).UtcDateTime;

            DateTime currentTime = DateTime.UtcNow;
            TimeSpan timeDifference = expiryDateTime - currentTime;

            return timeDifference.TotalMinutes <= 5;
        }
        catch ( Exception e ) {
            Console.WriteLine( e );
            return true;
        }
    }
    static Jwt GetJwtRules( IConfiguration config ) => new() {
        Key = new SymmetricSecurityKey( Encoding.UTF8.GetBytes( config.GetOrThrow( "Identity:Jwt:Key" ) ) ),
        Audience = config.GetOrThrow( "Identity:Jwt:Audience" ),
        Issuer = config.GetOrThrow( "Identity:Jwt:Issuer" ),
        ValidateAudience = config.GetSection( "Identity:Jwt:ValidateAudience" ).Get<bool>(),
        ValidateIssuer = config.GetSection( "Identity:Jwt:ValidateIssuer" ).Get<bool>(),
        ValidateIssuerSigningKey = config.GetSection( "Identity:Jwt:ValidateIssuerSigningKey" ).Get<bool>(),
        AccessLifetime = TimeSpan.Parse( config.GetOrThrow( "Identity:Jwt:AccessLifetime" ) ),
        RefreshLifetime = TimeSpan.Parse( config.GetOrThrow( "Identity:Jwt:RefreshLifetime" ) )
    };
    static Reply<JwtSecurityToken> ParseJwtFromString( string token, Jwt rules )
    {
        try {
            JwtSecurityTokenHandler tokenHandler = new();
            TokenValidationParameters validationParameters = new() {
                ValidateIssuerSigningKey = rules.ValidateIssuerSigningKey,
                IssuerSigningKey = rules.Key,
                ValidateIssuer = rules.ValidateIssuer,
                ValidIssuer = rules.Issuer,
                ValidateAudience = rules.ValidateAudience,
                ValidAudience = rules.Audience,
                ClockSkew = TimeSpan.Zero // Remove delay of token expiration
            };

            tokenHandler.ValidateToken( token, validationParameters, out SecurityToken securityToken );

            return securityToken is JwtSecurityToken jwtSecurityToken
                ? Reply<JwtSecurityToken>.With( jwtSecurityToken )
                : Reply<JwtSecurityToken>.None( "Jwt string is invalid." );
        }
        catch ( Exception e ) {
            Console.WriteLine( e );
            return Reply<JwtSecurityToken>.None( "An exception was thrown while parsing a jwt." );
        }
    }

    static ClaimsPrincipal GetIdentityClaimsPrincipal( string jwtToken )
    {
        ClaimsIdentity identity = GetIdentityClaims( jwtToken );
        return new ClaimsPrincipal( identity );
    }
    static ClaimsIdentity GetIdentityClaims( string jwtToken )
    {
        JwtSecurityTokenHandler handler = new();
        JwtSecurityToken? token = handler.ReadJwtToken( jwtToken );

        Claim? nameIdentifierClaim = token.Claims.FirstOrDefault( c => c.Type == ClaimTypes.NameIdentifier );
        Claim? nameClaim = token.Claims.FirstOrDefault( c => c.Type == ClaimTypes.Name );

        return new ClaimsIdentity( new[] {
            nameIdentifierClaim ?? new Claim( ClaimTypes.NameIdentifier, "None" ),
            nameClaim ?? new Claim( ClaimTypes.Name, "None" )
        }, "token" );
    }
    static async Task<AuthenticationState> GetNotifyParams( ClaimsPrincipal? claims ) =>
        await Task.FromResult( new AuthenticationState( claims ?? new ClaimsPrincipal() ) );

    StorageService GetStorage()
    {
        using AsyncServiceScope scope = _serviceProvider.CreateAsyncScope();
        return scope.ServiceProvider.GetService<StorageService>() ?? throw new Exception( "Failed to get Storage Service." );
    }
    
    readonly record struct RefreshRequest(
        string? AccessToken,
        string RefreshToken );
    readonly record struct RefreshReply(
        string? AccessToken );
}