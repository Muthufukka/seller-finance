namespace SellerFinance.Api;

public enum ApplicationMode { Demo, Pilot, Production, Testing }

public sealed record DeploymentProfile(ApplicationMode Mode,bool SeedDemoData,bool DataResidencyConfirmed,bool LegalReviewConfirmed,bool BackupRestoreConfirmed,bool CredentialsRotated,bool EmailConfirmationRequired)
{
    public bool IsDemo=>Mode==ApplicationMode.Demo;
    public bool IsRealDataMode=>Mode is ApplicationMode.Pilot or ApplicationMode.Production;
    public bool ProductionGatesSatisfied=>!IsRealDataMode||DataResidencyConfirmed&&LegalReviewConfirmed&&BackupRestoreConfirmed&&CredentialsRotated&&EmailConfirmationRequired;
    public bool MarketplaceConnectionsEnabled=>!IsDemo&&ProductionGatesSatisfied;

    public static DeploymentProfile Create(IConfiguration configuration,IHostEnvironment environment)
    {
        var configured=configuration["APP_MODE"]?.Trim();
        if(String.IsNullOrWhiteSpace(configured))
        {
            var publicOrigin=configuration["PUBLIC_BASE_URL"];
            var knownDemo=Uri.TryCreate(publicOrigin,UriKind.Absolute,out var uri)&&uri.Host.Equals("seller-finance.onrender.com",StringComparison.OrdinalIgnoreCase);
            if(environment.IsProduction()&&!knownDemo)throw new InvalidOperationException("APP_MODE is required in Production");
            configured=environment.IsEnvironment("Testing")?nameof(ApplicationMode.Testing):nameof(ApplicationMode.Demo);
        }
        if(!Enum.TryParse<ApplicationMode>(configured,true,out var mode))throw new InvalidOperationException("APP_MODE must be Demo, Pilot, Production or Testing");
        var seed=configuration.GetValue<bool>("SEED_DEMO_DATA");
        if(seed&&mode!=ApplicationMode.Demo)throw new InvalidOperationException("SEED_DEMO_DATA is allowed only when APP_MODE=Demo");
        var residency=String.Equals(configuration["DATA_RESIDENCY"]?.Trim(),"KZ",StringComparison.OrdinalIgnoreCase);
        return new(mode,seed,residency,configuration.GetValue<bool>("LEGAL_REVIEW_CONFIRMED"),configuration.GetValue<bool>("BACKUP_RESTORE_CONFIRMED"),configuration.GetValue<bool>("CREDENTIALS_ROTATED"),configuration.GetValue<bool>("EMAIL_CONFIRMATION_REQUIRED"));
    }
}
