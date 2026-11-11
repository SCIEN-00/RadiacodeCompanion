namespace Radiacode.App.Views;

internal static class Dialogs
{
    /// <summary>Asks before a destructive device action. Cancel is the default button.</summary>
    public static async Task<bool> ConfirmAsync(XamlRoot? root, string title, string message, string action)
    {
        if (root is null) return false;
        var dialog = new ContentDialog
        {
            Title = title,
            Content = new TextBlock { Text = message, TextWrapping = TextWrapping.WrapWholeWords },
            PrimaryButtonText = action,
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = root,
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }
}
