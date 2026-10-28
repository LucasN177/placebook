using MudBlazor;

namespace Placebook.Components.Account;

/// <summary>Öffnet die Konto-Dialoge und wechselt zwischen Anmelden und Registrieren.</summary>
public sealed class AccountDialogs(IDialogService dialogs, ISnackbar snackbar)
{
    public const string SignedIn = "signed-in";
    public const string SwitchToRegister = "register";
    public const string SwitchToLogin = "login";

    private static readonly DialogOptions Options = new()
    {
        MaxWidth = MaxWidth.ExtraSmall,
        FullWidth = true,
        CloseButton = true,
        CloseOnEscapeKey = true
    };

    /// <returns>true, wenn man danach angemeldet ist.</returns>
    public Task<bool> SignInAsync() => RunAsync(SwitchToLogin);

    /// <returns>true, wenn man danach angemeldet ist.</returns>
    public Task<bool> RegisterAsync() => RunAsync(SwitchToRegister);

    public async Task ChangePasswordAsync(bool isRecovery = false)
    {
        var parameters = new DialogParameters<PasswordDialog> { { d => d.IsRecovery, isRecovery } };
        var dialog = await dialogs.ShowAsync<PasswordDialog>(null, parameters, Options);
        if (await dialog.Result is { Canceled: false })
            snackbar.Add("Dein Passwort wurde geändert.", Severity.Success);
    }

    private async Task<bool> RunAsync(string start)
    {
        var next = start;
        while (true)
        {
            var dialog = next == SwitchToRegister
                ? await dialogs.ShowAsync<RegisterDialog>(title: null, Options)
                : await dialogs.ShowAsync<LoginDialog>(title: null, Options);
            var result = await dialog.Result;
            if (result is null || result.Canceled) return false;

            switch (result.Data as string)
            {
                case SignedIn:
                    snackbar.Add("Du bist angemeldet. Deine Besuche werden mit deinem Konto synchronisiert.", Severity.Success);
                    return true;
                case SwitchToRegister or SwitchToLogin:
                    next = (string)result.Data;
                    continue;
                default:
                    return false;
            }
        }
    }
}
