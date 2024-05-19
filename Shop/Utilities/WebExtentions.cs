using Microsoft.AspNetCore.Components;

namespace Shop.Utilities;

internal static class WebExtentions
{
    internal static void NavigateToOrDefault( this NavigationManager navigationManager, string? url )
    {
        navigationManager.NavigateTo( url ?? "/" );
    }
}