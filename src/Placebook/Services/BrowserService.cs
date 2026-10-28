using Microsoft.JSInterop;

namespace Placebook.Services;

public sealed record GeoPosition(double Lat, double Lon, double Accuracy);

/// <summary>Zugriff auf Browser-Funktionen aus <c>wwwroot/js/browser.js</c>.</summary>
public sealed class BrowserService(IJSRuntime js) : IAsyncDisposable
{
    private IJSObjectReference? _module;

    /// <summary>Zuletzt ermittelter Standort, damit Entfernungen ohne neue Abfrage angezeigt werden können.</summary>
    public GeoPosition? LastPosition { get; private set; }

    public async Task DownloadTextAsync(string fileName, string text, string mimeType = "application/json")
    {
        var module = await GetModuleAsync();
        await module.InvokeVoidAsync("downloadText", fileName, text, mimeType);
    }

    /// <exception cref="JSException">Standort nicht verfügbar oder Zugriff verweigert (Meldung ist für Nutzer lesbar).</exception>
    public async Task<GeoPosition> GetCurrentPositionAsync()
    {
        var module = await GetModuleAsync();
        LastPosition = await module.InvokeAsync<GeoPosition>("getCurrentPosition");
        return LastPosition;
    }

    private async Task<IJSObjectReference> GetModuleAsync() =>
        _module ??= await js.InvokeAsync<IJSObjectReference>("import", "./js/browser.js");

    public async ValueTask DisposeAsync()
    {
        if (_module is not null)
        {
            try
            {
                await _module.DisposeAsync();
            }
            catch (JSDisconnectedException)
            {
            }
        }
    }
}
