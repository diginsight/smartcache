namespace Common.SmartCache;

public static class CacheConstants
{
    public const int ZEROLATENCY = 0;
    public const int SHORTLATENCY = 120;
    public const int MEDIUMLATENCY = 600;
    public const int LONGLATENCY = 3600;

    public const string EDCSRoleMapping = "EDCSRoleMapping";
    public const string MRC2RoleMapping = "MRC2RoleMapping";
    public const string MRC3RoleMapping = "MRC3RoleMapping";
    public const string FrontendPermissions = "FrontendPermissions";
    public const string BackendPermissions = "BackendPermissions";
    public const string ELCPRoles = "ELCPRoles";
    public const string EDCSToken = "_EDCSToken";
    public const string DocumentMapping = "DocumentMapping";
    public const string FrontendOrganizationPermissions = "FrontendOrganizationPermissions";
}
