using System.Diagnostics;
using System.Net.Mail;
using System.Text.RegularExpressions;
using System.Windows;

namespace DevOpsPulse;
public partial class ConnectionWindow : Window
{
    public AppSettings Settings { get; private set; }
    public string Token { get; private set; } = "";
    public bool ForgetRequested { get; private set; }
    public ConnectionWindow(AppSettings settings, string currentToken)
    {
        InitializeComponent(); Settings = settings;
        OrganizationBox.Text = settings.Organization; EmailBox.Text = settings.Email;
        TokenBox.Password = currentToken; RememberBox.IsChecked = settings.RememberToken;
    }
    private void ConnectClick(object sender, RoutedEventArgs e)
    {
        var organization = OrganizationBox.Text.Trim(); var email = EmailBox.Text.Trim().Replace(" ", "");
        if (!Regex.IsMatch(organization, "^[A-Za-z0-9][A-Za-z0-9-]{0,49}$")) { ValidationText.Text = "Skriv bare organisasjonsnavnet, for eksempel helsedir."; return; }
        if (!MailAddress.TryCreate(email, out var address) || address.Address != email) { ValidationText.Text = "Skriv en gyldig e-postadresse."; return; }
        if (string.IsNullOrWhiteSpace(TokenBox.Password)) { ValidationText.Text = "Legg inn et Personal Access Token."; return; }
        Settings = new AppSettings(organization, email, RememberBox.IsChecked == true); Token = TokenBox.Password.Trim();
        DialogResult = true;
    }
    private void CreateToken(object sender, RoutedEventArgs e)
    {
        var org = OrganizationBox.Text.Trim();
        if (!Regex.IsMatch(org, "^[A-Za-z0-9][A-Za-z0-9-]{0,49}$")) { ValidationText.Text = "Skriv et gyldig organisasjonsnavn først."; return; }
        try { Process.Start(new ProcessStartInfo($"https://dev.azure.com/{org}/_usersSettings/tokens") { UseShellExecute = true }); }
        catch (Exception) { ValidationText.Text = "Nettleseren kunne ikke åpnes. Gå til Azure DevOps → User settings → Personal access tokens."; }
    }
    private void ForgetClick(object sender, RoutedEventArgs e)
    {
        try { SettingsStore.Forget(); ForgetRequested = true; TokenBox.Clear(); Token = ""; DialogResult = false; }
        catch (Exception ex) { ValidationText.Text = "Kunne ikke slette token: " + ex.Message; }
    }
}
