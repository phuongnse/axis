using System.Text.Json;
using Axis.Audit.Contracts;
using Axis.Authorization.Application;
using Axis.Authorization.Contracts;
using Axis.Authorization.Infrastructure.Persistence;
using Axis.Authorization.Infrastructure.Tests.Fixtures;
using Axis.Identity.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Axis.Authorization.Infrastructure.Tests;

[Collection("AuthorizationDb")]
public sealed class ProductRoleAssignmentPersistenceTests(AuthorizationDatabaseFixture database)
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-08-07T00:00:00Z");

    [Fact]
    public async Task Assign_WhenFirstRequest_CommitsAssignmentAndAudit()
    {
        TestCase test = await CreateTestCaseAsync();

        ProductRoleAssignmentResult result = await AssignAsync(test.AssignRequest);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Assignment);
        Assert.True(result.Assignment.IsActive);
        Assert.Equal(1, result.Assignment.Revision);

        await using AuthorizationDbContext read = database.CreateContext();
        ProductRoleAssignmentRow assignment = await read.Assignments
            .AsNoTracking()
            .SingleAsync(row => row.WorkspaceId == test.WorkspaceId, TestContext.Current.CancellationToken);
        AuthorizationIdempotencyRow idempotency = await read.IdempotencyRecords
            .AsNoTracking()
            .SingleAsync(row => row.WorkspaceId == test.WorkspaceId, TestContext.Current.CancellationToken);
        AuthorizationAuditOutboxRow audit = Assert.Single(
            await AuditRowsAsync(read, test.WorkspaceId));

        Assert.Equal(result.Assignment.Subject.Id, assignment.SubjectId);
        Assert.Equal(result.Assignment.Revision, assignment.Revision);
        Assert.Equal(assignment.Id, idempotency.AssignmentId);
        Assert.Equal(audit.Id, idempotency.AuditEventId);
        Assert.Equal("Pending", audit.DeliveryState);
        Assert.NotNull(audit.ReadBackAt);
    }

    [Fact]
    public async Task Assign_WhenEquivalentRetry_ReturnsCanonicalWithoutDuplicates()
    {
        TestCase test = await CreateTestCaseAsync();
        ProductRoleAssignmentResult first = await AssignAsync(test.AssignRequest);

        ProductRoleAssignmentResult retry = await AssignAsync(test.AssignRequest);

        Assert.True(first.IsSuccess);
        Assert.True(retry.IsSuccess);
        Assert.Equal(first.Assignment, retry.Assignment);
        await using AuthorizationDbContext read = database.CreateContext();
        Assert.Equal(1, await read.Assignments.CountAsync(
            row => row.WorkspaceId == test.WorkspaceId,
            TestContext.Current.CancellationToken));
        Assert.Equal(1, await read.IdempotencyRecords.CountAsync(
            row => row.WorkspaceId == test.WorkspaceId,
            TestContext.Current.CancellationToken));
        Assert.Single(await AuditRowsAsync(read, test.WorkspaceId));
    }

    [Fact]
    public async Task Assign_WhenIdempotencyContentChanges_RejectsConflictAndAudits()
    {
        TestCase test = await CreateTestCaseAsync();
        ProductRoleAssignmentResult first = await AssignAsync(test.AssignRequest);
        AssignProductRoleRequest changed = test.AssignRequest with { RoleKey = "Reviewer" };

        ProductRoleAssignmentResult conflict = await AssignAsync(changed);

        Assert.True(first.IsSuccess);
        Assert.False(conflict.IsSuccess);
        Assert.Equal("idempotency_conflict", conflict.Error);
        await using AuthorizationDbContext read = database.CreateContext();
        Assert.Equal(1, await read.Assignments.CountAsync(
            row => row.WorkspaceId == test.WorkspaceId,
            TestContext.Current.CancellationToken));
        Assert.Equal(1, await read.IdempotencyRecords.CountAsync(
            row => row.WorkspaceId == test.WorkspaceId,
            TestContext.Current.CancellationToken));
        IReadOnlyList<AuthorizationAuditOutboxRow> audits = await AuditRowsAsync(read, test.WorkspaceId);
        Assert.Equal(2, audits.Count);
        Assert.Contains(audits, row => Deserialize(row).Outcome == "idempotency_conflict");
    }

    [Fact]
    public async Task Revoke_WhenExpectedRevisionMatches_CommitsCanonicalRevision()
    {
        TestCase test = await CreateTestCaseAsync();
        ProductRoleAssignmentResult assigned = await AssignAsync(test.AssignRequest);
        RevokeProductRoleRequest revoke = new(
            test.WorkspaceId,
            test.Actor,
            test.Target,
            test.PolicyVersionId,
            test.AssignRequest.RoleKey,
            "revoke-1",
            "corr-revoke-1",
            assigned.Assignment!.Revision);

        ProductRoleAssignmentResult result = await RevokeAsync(revoke);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Assignment);
        Assert.False(result.Assignment.IsActive);
        Assert.Equal(2, result.Assignment.Revision);
        await using AuthorizationDbContext read = database.CreateContext();
        ProductRoleAssignmentRow canonical = await read.Assignments
            .AsNoTracking()
            .SingleAsync(row => row.WorkspaceId == test.WorkspaceId, TestContext.Current.CancellationToken);
        Assert.False(canonical.IsActive);
        Assert.Equal(2, canonical.Revision);
        Assert.NotNull(canonical.RevokedAt);
        Assert.Equal(2, await read.IdempotencyRecords.CountAsync(
            row => row.WorkspaceId == test.WorkspaceId,
            TestContext.Current.CancellationToken));
        Assert.Equal(2, (await AuditRowsAsync(read, test.WorkspaceId)).Count);
    }

    [Fact]
    public async Task Assignments_WhenConcurrentRequests_ReturnSingleCanonicalRow()
    {
        TestCase test = await CreateTestCaseAsync();
        AssignProductRoleRequest second = test.AssignRequest with { IdempotencyKey = "assign-2" };

        ProductRoleAssignmentResult[] results = await Task.WhenAll(
            AssignAsync(test.AssignRequest),
            AssignAsync(second));

        Assert.Contains(results, result => result.IsSuccess);
        await using AuthorizationDbContext read = database.CreateContext();
        ProductRoleAssignmentRow canonical = await read.Assignments
            .AsNoTracking()
            .SingleAsync(row => row.WorkspaceId == test.WorkspaceId, TestContext.Current.CancellationToken);
        Assert.True(canonical.IsActive);
        Assert.Equal(1, canonical.Revision);
        Assert.Equal(
            results.Count(result => result.IsSuccess),
            await read.IdempotencyRecords.CountAsync(
                row => row.WorkspaceId == test.WorkspaceId,
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Revocations_WhenConcurrentRequests_ReturnSingleCanonicalRevision()
    {
        TestCase test = await CreateTestCaseAsync();
        ProductRoleAssignmentResult assigned = await AssignAsync(test.AssignRequest);
        RevokeProductRoleRequest first = new(
            test.WorkspaceId,
            test.Actor,
            test.Target,
            test.PolicyVersionId,
            test.AssignRequest.RoleKey,
            "revoke-1",
            "corr-revoke-1",
            assigned.Assignment!.Revision);
        RevokeProductRoleRequest second = first with { IdempotencyKey = "revoke-2" };

        ProductRoleAssignmentResult[] results = await Task.WhenAll(
            RevokeAsync(first),
            RevokeAsync(second));

        Assert.Single(results, result => result.IsSuccess);
        await using AuthorizationDbContext read = database.CreateContext();
        ProductRoleAssignmentRow canonical = await read.Assignments
            .AsNoTracking()
            .SingleAsync(row => row.WorkspaceId == test.WorkspaceId, TestContext.Current.CancellationToken);
        Assert.False(canonical.IsActive);
        Assert.Equal(2, canonical.Revision);
    }

    [Fact]
    public async Task Assign_WhenAuditReadBackFails_RollsBackAllRows()
    {
        TestCase test = await CreateTestCaseAsync();

        ProductRoleAssignmentResult result = await AssignAsync(test.AssignRequest, failAuditReadBack: true);

        Assert.False(result.IsSuccess);
        Assert.Equal("audit_unavailable", result.Error);
        await using AuthorizationDbContext read = database.CreateContext();
        Assert.Equal(0, await read.Assignments.CountAsync(
            row => row.WorkspaceId == test.WorkspaceId,
            TestContext.Current.CancellationToken));
        Assert.Equal(0, await read.IdempotencyRecords.CountAsync(
            row => row.WorkspaceId == test.WorkspaceId,
            TestContext.Current.CancellationToken));
        Assert.Empty(await AuditRowsAsync(read, test.WorkspaceId));
    }

    private async Task<TestCase> CreateTestCaseAsync()
    {
        Guid workspaceId = Guid.NewGuid();
        Guid policyVersionId = Guid.NewGuid();
        SubjectReference actor = SubjectReference.Human(Guid.NewGuid());
        SubjectReference target = SubjectReference.Service(Guid.NewGuid());
        ProductPolicyComponent component = new(
            "reference",
            policyVersionId,
            [
                new("Applicant", new Dictionary<string, ProductRolePresentation>()),
                new("Reviewer", new Dictionary<string, ProductRolePresentation>()),
            ],
            []);
        await using AuthorizationDbContext context = database.CreateContext();
        await context.Policies.AddAsync(
            new InstalledPolicyRow
            {
                WorkspaceId = workspaceId,
                VersionId = policyVersionId,
                PolicyKey = component.PolicyKey,
                CanonicalContent = JsonSerializer.Serialize(component),
                Provenance = "test",
                InstalledAt = Now,
            },
            TestContext.Current.CancellationToken);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        return new(
            workspaceId,
            policyVersionId,
            actor,
            target,
            new(
                workspaceId,
                actor,
                target,
                policyVersionId,
                "Applicant",
                "assign-1",
                "corr-assign-1"));
    }

    private Task<ProductRoleAssignmentResult> AssignAsync(
        AssignProductRoleRequest request,
        bool failAuditReadBack = false) =>
        ExecuteAsync(
            (service, cancellationToken) => service.AssignAsync(request, cancellationToken),
            failAuditReadBack);

    private Task<ProductRoleAssignmentResult> RevokeAsync(RevokeProductRoleRequest request) =>
        ExecuteAsync(
            (service, cancellationToken) => service.RevokeAsync(request, cancellationToken),
            failAuditReadBack: false);

    private async Task<ProductRoleAssignmentResult> ExecuteAsync(
        Func<ProductRoleAssignmentService, CancellationToken, Task<ProductRoleAssignmentResult>> operation,
        bool failAuditReadBack)
    {
        await using AuthorizationDbContext context = database.CreateContext();
        FixedClock clock = new(Now);
        AuthorizationAuditOutbox outbox = new(context, clock);
        IAuthorizationAuditSink audit = failAuditReadBack
            ? new MissingReadBackAuditSink(outbox)
            : outbox;
        ProductRoleAssignmentService service = new(
            new ActiveSubjects(),
            new Administrators(),
            new InstalledProductRoleStore(context),
            new ProductRoleAssignmentStore(context),
            audit,
            new AuthorizationUnitOfWork(context),
            clock);
        return await operation(service, TestContext.Current.CancellationToken);
    }

    private static async Task<IReadOnlyList<AuthorizationAuditOutboxRow>> AuditRowsAsync(
        AuthorizationDbContext context,
        Guid workspaceId)
    {
        List<AuthorizationAuditOutboxRow> rows = await context.AuditOutbox
            .AsNoTracking()
            .ToListAsync(TestContext.Current.CancellationToken);
        return rows.Where(row => Deserialize(row).WorkspaceId == workspaceId).ToArray();
    }

    private static AuditEventV1 Deserialize(AuthorizationAuditOutboxRow row) =>
        JsonSerializer.Deserialize<AuditEventV1>(row.Payload)
        ?? throw new InvalidOperationException("Stored Authorization audit payload is invalid.");

    private sealed record TestCase(
        Guid WorkspaceId,
        Guid PolicyVersionId,
        SubjectReference Actor,
        SubjectReference Target,
        AssignProductRoleRequest AssignRequest);

    private sealed class ActiveSubjects : IAuthorizationSubjectActivity
    {
        public Task<bool> IsActiveAsync(
            Guid workspaceId,
            SubjectReference subject,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(true);
    }

    private sealed class Administrators : IAuthorizationAdministratorAuthority
    {
        public Task<bool> IsAdministratorAsync(
            Guid workspaceId,
            SubjectReference actor,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(true);
    }

    private sealed class MissingReadBackAuditSink(AuthorizationAuditOutbox inner) : IAuthorizationAuditSink
    {
        public Task<AuditIngestionResult> IngestAsync(
            AuditEventV1 auditEvent,
            CancellationToken cancellationToken = default) =>
            inner.IngestAsync(auditEvent, cancellationToken);

        public Task<AuditEventReadBackV1?> ReadBackAsync(
            Guid eventId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<AuditEventReadBackV1?>(null);
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
