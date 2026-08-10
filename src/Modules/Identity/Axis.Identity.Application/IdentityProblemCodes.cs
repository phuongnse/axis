namespace Axis.Identity.Application;

public static class IdentityProblemCodes
{
    public const string RegisterFullNameRequired = "identity.register.fullNameRequired";
    public const string RegisterFullNameTooLong = "identity.register.fullNameTooLong";
    public const string RegisterEmailRequired = "identity.register.emailRequired";
    public const string RegisterEmailInvalid = "identity.register.emailInvalid";
    public const string RegisterEmailAlreadyExists = "identity.register.emailAlreadyExists";
    public const string RegisterTermsCurrentRequired = "identity.register.termsCurrentRequired";
    public const string RegisterPrivacyCurrentRequired = "identity.register.privacyCurrentRequired";
    public const string RegisterPreferredLanguageUnsupported =
        "identity.register.preferredLanguageUnsupported";
    public const string RegisterPasswordRequired = "identity.register.passwordRequired";
    public const string RegisterPasswordPolicyFailed = "identity.register.passwordPolicyFailed";
    public const string RegisterPasswordConfirmationMismatch =
        "identity.register.passwordConfirmationMismatch";

    public const string SignInEmailRequired = "identity.signIn.emailRequired";
    public const string SignInEmailInvalid = "identity.signIn.emailInvalid";
    public const string SignInPasswordRequired = "identity.signIn.passwordRequired";
    public const string SignInInvalidCredentials = "identity.signIn.invalidCredentials";
    public const string SignInVerificationRequired = "identity.signIn.verificationRequired";
    public const string SignInAccountUnavailable = "identity.signIn.accountUnavailable";

    public const string EmailVerificationInvalidToken = "identity.emailVerification.invalidToken";
    public const string EmailVerificationExpiredToken = "identity.emailVerification.expiredToken";
    public const string EmailVerificationAlreadyUsedToken =
        "identity.emailVerification.alreadyUsedToken";
    public const string EmailVerificationAccountUnavailable =
        "identity.emailVerification.accountUnavailable";
    public const string EmailVerificationResendRateLimited =
        "identity.emailVerification.resendRateLimited";

    public const string PreferenceLanguageRequired = "identity.preferences.languageRequired";
    public const string PreferenceLanguageUnsupported = "identity.preferences.languageUnsupported";
    public const string PreferenceThemeRequired = "identity.preferences.themeRequired";
    public const string PreferenceThemeUnsupported = "identity.preferences.themeUnsupported";

    public const string CreateOrganizationNameRequired =
        "identity.createOrganization.nameRequired";
    public const string CreateOrganizationNameLength =
        "identity.createOrganization.nameLength";

    public const string InvitationEmailInvalid = "identity.invitation.emailInvalid";
    public const string InvitationRoleUnsupported = "identity.invitation.roleUnsupported";
    public const string InvitationWorkspaceIneligible = "identity.invitation.workspaceIneligible";
    public const string InvitationForbidden = "identity.invitation.forbidden";
    public const string InvitationRateLimited = "identity.invitation.rateLimited";
    public const string InvitationConflict = "identity.invitation.conflict";
    public const string InvitationInvalid = "identity.invitation.invalid";
    public const string InvitationAuditUnavailable = "identity.invitation.auditUnavailable";
    public const string InvitationReadBackFailed = "identity.invitation.readBackFailed";
    public const string InvitationNotFound = "identity.invitation.notFound";
    public const string InvitationNotPending = "identity.invitation.notPending";
    public const string InvitationPageInvalid = "identity.invitation.pageInvalid";
    public const string InvitationAccessInvalid = "identity.invitation.accessInvalid";
    public const string InvitationAccountMismatch = "identity.invitation.accountMismatch";
    public const string InvitationAuthorityStale = "identity.invitation.authorityStale";
    public const string InvitationMembershipSuspended = "identity.invitation.membershipSuspended";
}
