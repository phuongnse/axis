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
}
