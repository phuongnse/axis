using Axis.Authorization.Domain;

namespace Axis.Authorization.Domain.Tests;

public sealed class ProductPolicyValidationTests
{
    private static readonly ProductActionDescriptor Record = new("business-object.record.read", "business-object-record", ProductActionKind.Record);

    [Fact]
    public void Validate_WhenRecordGrantUsesNone_RejectsPolicy()
    {
        ProductPolicyComponent policy = Policy(ProductActionScope.None);
        Assert.Equal("authorization.grant_invalid", ProductPolicyValidation.Validate(policy, [Record]));
    }

    [Fact]
    public void Validate_WhenPresentationLacksEnglish_RejectsPolicy()
    {
        ProductPolicyComponent policy = new("reference", Guid.NewGuid(), [new("Applicant", new Dictionary<string, ProductRolePresentation> { ["vi"] = new("Người nộp", null) })], []);
        Assert.Equal("authorization.role_invalid", ProductPolicyValidation.Validate(policy, [Record]));
    }

    [Theory]
    [InlineData("language")]
    [InlineData("duplicate-language")]
    [InlineData("display-not-nfc")]
    [InlineData("display-too-long")]
    [InlineData("description-not-nfc")]
    [InlineData("description-too-long")]
    public void Validate_WhenPresentationContractIsInvalid_RejectsPolicy(string caseName)
    {
        Dictionary<string, ProductRolePresentation> presentation = caseName switch
        {
            "language" => new() { ["en_US"] = new("Applicant", null), ["en"] = new("Applicant", null) },
            "duplicate-language" => new() { ["en"] = new("Applicant", null), ["EN"] = new("Applicant", null) },
            "display-not-nfc" => new() { ["en"] = new("Cafe\u0301", null) },
            "display-too-long" => new() { ["en"] = new(new string('a', 257), null) },
            "description-not-nfc" => new() { ["en"] = new("Applicant", "Cafe\u0301") },
            "description-too-long" => new() { ["en"] = new("Applicant", new string('a', 2049)) },
            _ => throw new ArgumentOutOfRangeException(nameof(caseName)),
        };
        ProductPolicyComponent policy = new(
            "reference",
            Guid.NewGuid(),
            [new("Applicant", presentation)],
            []);

        Assert.Equal(
            "authorization.role_presentation_invalid",
            ProductPolicyValidation.Validate(policy, [Record]));
    }

    [Fact]
    public void Validate_WhenExactRecordGrantIsValid_AcceptsPolicy()
    {
        Assert.Null(ProductPolicyValidation.Validate(Policy(ProductActionScope.Own), [Record]));
    }

    [Fact]
    public void Policy_UnknownDescriptor_RejectsGrant()
    {
        ProductPolicyComponent policy = Policy(ProductActionScope.Own) with
        {
            Grants = [new("Applicant", "record.write", "business-object-record", null, ProductActionScope.Own)],
        };

        Assert.Equal("authorization.grant_invalid", ProductPolicyValidation.Validate(policy, [Record]));
    }

    [Fact]
    public void Policy_DuplicateGrant_RejectsPolicy()
    {
        ProductPolicyComponent policy = Policy(ProductActionScope.Own) with
        {
            Grants =
            [
                new("Applicant", Record.ActionKey, Record.ResourceType, null, ProductActionScope.Own),
                new("Applicant", Record.ActionKey, Record.ResourceType, null, ProductActionScope.Own),
            ],
        };

        Assert.Equal("authorization.grant_invalid", ProductPolicyValidation.Validate(policy, [Record]));
    }

    [Fact]
    public void Policy_DuplicateDescriptor_RejectsRegistry()
    {
        Assert.Equal("authorization.descriptor_invalid", ProductPolicyValidation.Validate(
            Policy(ProductActionScope.Own), [Record, Record]));
    }

    private static ProductPolicyComponent Policy(ProductActionScope scope) => new(
        "reference", Guid.NewGuid(),
        [new("Applicant", new Dictionary<string, ProductRolePresentation> { ["en"] = new("Applicant", null) })],
        [new("Applicant", Record.ActionKey, Record.ResourceType, null, scope)]);

    [Fact]
    public void Revoke_WhenRevisionCurrent_RevokesAssignment()
    {
        ProductRoleAssignment assignment = ProductRoleAssignment.Create(Guid.NewGuid(), new(AuthorizationSubjectKind.Human, Guid.NewGuid()), Guid.NewGuid(), "Applicant", DateTime.UtcNow)!;
        Assert.False(assignment.Revoke(2, DateTime.UtcNow));
        Assert.True(assignment.Revoke(1, DateTime.UtcNow));
        Assert.False(assignment.IsActive);
    }
}
