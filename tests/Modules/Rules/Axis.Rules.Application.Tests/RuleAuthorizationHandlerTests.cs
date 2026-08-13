using Axis.Identity.Contracts;
using Axis.Rules.Application.Commands.CreateRuleBinding;
using Axis.Rules.Application.Commands.CreateRuleDefinition;
using Axis.Rules.Application.Queries.GetRuleDefinition;
using Axis.Rules.Application.Queries.ListRuleDefinitions;
using Axis.Rules.Application.Repositories;
using Axis.Rules.Contracts;
using Axis.Rules.Domain;
using Axis.Shared.Application;
using Axis.Shared.Domain.Primitives;
using FluentAssertions;
using NSubstitute;

namespace Axis.Rules.Application.Tests;

public sealed class RuleAuthorizationHandlerTests
{
    [Fact]
    public async Task ListDefinitions_WhenBuilderAllowed_UsesExactWorkspaceAndHumanSubject()
    {
        RuleDefinitionHandlerTestContext context = new();
        Guid? observedWorkspaceId = null;
        SubjectReference? observedSubject = null;
        context.Authorization.AuthorizeAsync(
                Arg.Do<Guid>(workspaceId => observedWorkspaceId = workspaceId),
                Arg.Do<SubjectReference>(subject => observedSubject = subject),
                Arg.Any<CancellationToken>())
            .Returns(WorkspaceProductBuilderDecision.Allowed);
        ListRuleDefinitionsHandler sut = new(
            context.CurrentUser,
            context.CurrentSubject,
            context.Authorization,
            context.Repository,
            context.CatalogSearch);

        Result<PagedResult<RuleDefinitionSummaryDto>> result = await sut.Handle(
            new ListRuleDefinitionsQuery(1, 20, Origin: Axis.Rules.Contracts.RuleOrigin.Workspace),
            TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        observedWorkspaceId.Should().Be(RuleDefinitionHandlerTestContext.WorkspaceId);
        observedSubject.Should().Be(SubjectReference.Human(RuleDefinitionHandlerTestContext.UserId));
    }

    [Fact]
    public async Task CreateDefinition_WhenNonBuilder_ReturnsForbiddenWithoutMutation()
    {
        RuleDefinitionHandlerTestContext context = new();
        context.Authorization.AuthorizeAsync(
                Arg.Any<Guid>(),
                Arg.Any<SubjectReference>(),
                Arg.Any<CancellationToken>())
            .Returns(WorkspaceProductBuilderDecision.Denied);
        CreateRuleDefinitionHandler sut = new(
            context.CurrentUser,
            context.CurrentSubject,
            context.Authorization,
            context.Repository,
            context.UnitOfWork);

        Result<RuleDefinitionDetailDto> result = await sut.Handle(
            new CreateRuleDefinitionCommand("Denied rule", "Must not be persisted."),
            TestContext.Current.CancellationToken);

        result.ErrorCode.Should().Be(ErrorCodes.Forbidden);
        await context.Repository.DidNotReceive().AddAsync(
            Arg.Any<RuleDefinition>(),
            Arg.Any<CancellationToken>());
        await context.UnitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateDefinition_WhenBuilderAuthorizationUnavailable_ReturnsUnavailableWithoutMutation()
    {
        RuleDefinitionHandlerTestContext context = new();
        context.Authorization.AuthorizeAsync(
                Arg.Any<Guid>(),
                Arg.Any<SubjectReference>(),
                Arg.Any<CancellationToken>())
            .Returns(WorkspaceProductBuilderDecision.Unavailable);
        CreateRuleDefinitionHandler sut = new(
            context.CurrentUser,
            context.CurrentSubject,
            context.Authorization,
            context.Repository,
            context.UnitOfWork);

        Result<RuleDefinitionDetailDto> result = await sut.Handle(
            new CreateRuleDefinitionCommand("Unavailable rule", "Must not be persisted."),
            TestContext.Current.CancellationToken);

        result.ErrorCode.Should().Be(ErrorCodes.Unavailable);
        result.ProblemCode.Should().Be(RulesProblemCodes.AuthorizationUnavailable);
        await context.Repository.DidNotReceive().AddAsync(
            Arg.Any<RuleDefinition>(),
            Arg.Any<CancellationToken>());
        await context.UnitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateDefinition_WhenSubjectIsService_DeniesWithoutCallingBuilderDependency()
    {
        RuleDefinitionHandlerTestContext context = new();
        context.CurrentSubject.Subject.Returns(SubjectReference.Service(Guid.NewGuid()));
        CreateRuleDefinitionHandler sut = new(
            context.CurrentUser,
            context.CurrentSubject,
            context.Authorization,
            context.Repository,
            context.UnitOfWork);

        Result<RuleDefinitionDetailDto> result = await sut.Handle(
            new CreateRuleDefinitionCommand("Service rule", "Services cannot be builders."),
            TestContext.Current.CancellationToken);

        result.ErrorCode.Should().Be(ErrorCodes.Forbidden);
        await context.Authorization.DidNotReceive().AuthorizeAsync(
            Arg.Any<Guid>(),
            Arg.Any<SubjectReference>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetDefinition_WhenBuilderAllowed_ProjectsWorkspaceLifecycleActions()
    {
        RuleDefinitionHandlerTestContext context = new();
        RuleDefinition definition = RuleDefinitionHandlerTestContext.ConfiguredDraft();
        context.Repository.GetByKeyForWorkspaceAsync(
                definition.Key,
                RuleDefinitionHandlerTestContext.WorkspaceId,
                Arg.Any<CancellationToken>())
            .Returns(definition);
        GetRuleDefinitionHandler sut = new(
            context.CurrentUser,
            context.CurrentSubject,
            context.Authorization,
            context.Repository);

        Result<RuleDefinitionDetailDto> result = await sut.Handle(
            new GetRuleDefinitionQuery(definition.Key.Value),
            TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        result.Value.Actions.CanEditDraft.Should().BeTrue();
        result.Value.Actions.CanCreateVersion.Should().BeTrue();
        result.Value.Actions.CanArchive.Should().BeTrue();
    }

    [Fact]
    public async Task CreateBinding_WhenBuilderDependencyThrows_ReturnsUnavailableWithoutMutation()
    {
        RuleDefinitionHandlerTestContext context = new();
        context.Authorization.AuthorizeAsync(
                Arg.Any<Guid>(),
                Arg.Any<SubjectReference>(),
                Arg.Any<CancellationToken>())
            .Returns<Task<WorkspaceProductBuilderDecision>>(_ =>
                throw new InvalidOperationException("authorization unavailable"));
        IRuleBindingRepository repository = Substitute.For<IRuleBindingRepository>();
        CreateRuleBindingHandler sut = new(
            context.CurrentUser,
            context.CurrentSubject,
            context.Authorization,
            context.Repository,
            repository,
            context.UnitOfWork);

        Result<RuleBindingDto> result = await sut.Handle(
            new CreateRuleBindingCommand(RuleBindingHandlerTestData.Request("field-1")),
            TestContext.Current.CancellationToken);

        result.ErrorCode.Should().Be(ErrorCodes.Unavailable);
        result.ProblemCode.Should().Be(RulesProblemCodes.AuthorizationUnavailable);
        await repository.DidNotReceive().AddAsync(Arg.Any<RuleBinding>(), Arg.Any<CancellationToken>());
        await context.UnitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
