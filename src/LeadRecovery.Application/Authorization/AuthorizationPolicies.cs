namespace LeadRecovery.Application.Authorization;

public static class AuthorizationPolicies
{
    public const string TenantMember = "TenantMember";
    public const string DashboardOperator = "DashboardOperator";
    public const string AutomationManager = "AutomationManager";
    public const string OwnerOnly = "OwnerOnly";
}
