using Microsoft.AspNetCore.Components;
using Shop.Infrastructure.Common;
using Shop.Infrastructure.Common.Optionals;
using Shop.Infrastructure.Http;
using Shop.Utilities;

namespace Shop.UI.Shared;

public abstract class PageBase : ComponentBase
{
    protected PageBase() => redirectTime = GetRedirectTime( Configuration );

    [Inject] protected IConfiguration Configuration { get; init; } = default!;
    [Inject] protected HttpService Http { get; init; } = default!;
    [Inject] protected NavigationManager Navigation { get; init; } = default!;
    [Inject] MainLayout layout { get; init; } = default!;

    readonly string ReturnUrl = string.Empty;
    readonly int redirectTime;
    int redirectCountdown = 0;
    System.Timers.Timer? pageRedirectTimer;
    
    protected override void OnInitialized()
    {
        base.OnInitialized();
        StartLoading( "Loading Page..." );
    }

    protected void GoHome() =>
        Navigate( Urls.PageHome, true );
    protected void Navigate( string page, bool forceReload = false )
    {
        Navigation.NavigateTo( page, forceReload );
    }
    protected void StartRedirect( string? message )
    {
        layout.StartRedirecting( redirectTime, message );
        redirectCountdown = redirectTime;
        pageRedirectTimer = new System.Timers.Timer( redirectTime );
        pageRedirectTimer.Elapsed += CountdownTimerElapsed;
        pageRedirectTimer.AutoReset = true;
        pageRedirectTimer.Enabled = true;
    }
    protected void PushSuccess( string message ) => PushAlert( AlertType.Success, message );
    protected void PushWarning( string message ) => PushAlert( AlertType.Warning, message );
    protected void PushError( string message ) => PushAlert( AlertType.Danger, message );
    protected void PushError( IOpt opt, string message ) => PushAlert( AlertType.Danger, $"{message} : {opt.Message()}" );
    protected void PushError( IOpt opt ) => PushAlert( AlertType.Danger, opt.Message() );
    protected virtual void StartLoading( string? message )
    {
        layout.StartLoading( message );
    }
    protected virtual void StopLoading()
    {
        layout.StopLoading();
    }

    void PushAlert( AlertType type, string message )
    {
        layout.PushAlert( type, message );
    }
    void CountdownTimerElapsed( object? sender, System.Timers.ElapsedEventArgs e )
    {
        if (redirectCountdown > 0) {
            redirectCountdown--;
            layout.TickRedirect();
            return;
        }

        pageRedirectTimer?.Stop();
        pageRedirectTimer?.Dispose();

        string returnUrl = !string.IsNullOrWhiteSpace( ReturnUrl ) ? ReturnUrl : string.Empty;
        Navigation.NavigateTo( returnUrl );
    }
    /*static int GetRedirectTime( IConfiguration configuration ) =>
        int.TryParse( configuration["PageRedirectTime"], out int time )
            ? time
            : 3;*/
    static int GetRedirectTime( IConfiguration configuration ) => 3;
}