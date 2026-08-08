using Axis.Authorization.Contracts;
using Axis.Identity.Contracts;
using Axis.Rules.Application.Commands.CreateRuleBinding;
using Axis.Rules.Application.Commands.CreateRuleDefinition;
using Axis.Rules.Application.Commands.CreateRuleDefinitionVersion;
using Axis.Rules.Application.Queries.GetRuleBinding;
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
    public async Task ListDefinitions_WhenAuthorized_UsesExactKeylessRequest()
    {
        RuleDefinitionHandlerTestContext context = new();
        SubjectReference service = SubjectReference.Service(Guid.NewGuid());
        context.CurrentSubject.Subject.Returns(service);
        ProductAuthorizationRequest? observed = null;
        context.Authorization.AuthorizeAsync(
                Arg.Do<ProductAuthorizationRequest>(request => observed = request),
                Arg.Any<CancellationToken>())
            .Returns(new ProductAuthorizationDecision(true, ProductActionScope.None));
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
        observed.Should().NotBeNull();
        observed!.WorkspaceId.Should().Be(RuleDefinitionHandlerTestContext.WorkspaceId);
        observed.Subject.Should().Be(service);
        observed.ActionKey.Should().Be(RuleProductActions.DefinitionRead);
        observed.ResourceType.Should().Be(RuleProductActions.DefinitionResourceType);
        observed.ResourceKey.Should().BeNull();
        observed.CorrelationId.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task CreateDefinition_WhenServiceIsAllowed_PersistsServiceActor()
    {
        RuleDefinitionHandlerTestContext context = new();
        SubjectReference service = SubjectReference.Service(Guid.NewGuid());
        context.CurrentSubject.Subject.Returns(service);
        RuleDefinition? persisted = null;
        context.Repository.KeyExistsAsync(
                Arg.Any<RuleDefinitionKey>(),
                RuleDefinitionHandlerTestContext.WorkspaceId,
                Arg.Any<CancellationToken>())
            .Returns(false);
        context.Repository.AddAsync(
                Arg.Do<RuleDefinition>(definition => persisted = definition),
                Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        CreateRuleDefinitionHandler sut = new(
            context.CurrentUser,
            context.CurrentSubject,
            context.Authorization,
            context.Repository,
            context.UnitOfWork);

        Result<RuleDefinitionDetailDto> result = await sut.Handle(
            new CreateRuleDefinitionCommand("Service rule", "Created by an assigned service."),
            TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        persisted.Should().NotBeNull();
        persisted!.CreatedBySubject.Should().Be(RuleSubjectReference.Service(service.Id));
        persisted.UpdatedBySubject.Should().Be(RuleSubjectReference.Service(service.Id));
    }

    [Fact]
    public async Task GetDefinition_WhenExactManageDenied_ProjectsLifecycleActionsAsFalse()
    {
        RuleDefinitionHandlerTestContext context = new();
        RuleDefinition definition = RuleDefinitionHandlerTestContext.ConfiguredDraft();
        context.Repository.GetByKeyForWorkspaceAsync(
                definition.Key,
                RuleDefinitionHandlerTestContext.WorkspaceId,
                Arg.Any<CancellationToken>())
            .Returns(definition);
        context.Authorization.AuthorizeAsync(
                Arg.Any<ProductAuthorizationRequest>(),
                Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<ProductAuthorizationRequest>().ActionKey == RuleProductActions.DefinitionRead
                ? new ProductAuthorizationDecision(true, ProductActionScope.None)
                : ProductAuthorizationDecision.Denied);
        GetRuleDefinitionHandler sut = new(
            context.CurrentUser,
            context.CurrentSubject,
            context.Authorization,
            context.Repository);

        Result<RuleDefinitionDetailDto> result = await sut.Handle(
            new GetRuleDefinitionQuery(definition.Key.Value),
            TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        result.Value.Actions.CanEditDraft.Should().BeFalse();
        result.Value.Actions.CanCreateVersion.Should().BeFalse();
        result.Value.Actions.CanArchive.Should().BeFalse();
    }

    [Fact]
    public async Task CreateDefinition_WhenAuthorizationUnavailable_DoesNotMutate()
    {
        RuleDefinitionHandlerTestContext context = new();
        context.Authorization.AuthorizeAsync(
                Arg.Any<ProductAuthorizationRequest>(),
                Arg.Any<CancellationToken>())
            .Returns(ProductAuthorizationDecision.Unavailable);
        CreateRuleDefinitionHandler sut = new(
            context.CurrentUser,
            context.CurrentSubject,
            context.Authorization,
            context.Repository,
            context.UnitOfWork);

        Result<RuleDefinitionDetailDto> result = await sut.Handle(
            new CreateRuleDefinitionCommand("Unavailable rule", "Must not be persisted."),
            TestContext.Current.CancellationToken);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.Unavailable);
        result.ProblemCode.Should().Be(RulesProblemCodes.AuthorizationUnavailable);
        await context.Repository.DidNotReceive().AddAsync(
            Arg.Any<RuleDefinition>(),
            Arg.Any<CancellationToken>());
        await context.UnitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateVersion_WhenServiceIsAllowed_ProjectsPublishedSubject()
    {
        RuleDefinitionHandlerTestContext context = new();
        SubjectReference service = SubjectReference.Service(Guid.NewGuid());
        context.CurrentSubject.Subject.Returns(service);
        RuleDefinition definition = RuleDefinitionHandlerTestContext.ConfiguredDraft();
        context.Repository.GetByKeyForWorkspaceAsync(
                definition.Key,
                RuleDefinitionHandlerTestContext.WorkspaceId,
                Arg.Any<CancellationToken>())
            .Returns(definition);
        CreateRuleDefinitionVersionHandler sut = new(
            context.CurrentUser,
            context.CurrentSubject,
            context.Authorization,
            context.Repository,
            context.UnitOfWork);

        Result<RuleDefinitionDetailDto> result = await sut.Handle(
            new CreateRuleDefinitionVersionCommand(definition.Key.Value, definition.Revision),
            TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        result.Value.Versions.Should().ContainSingle();
        SubjectReferenceDto publishedBy = result.Value.Versions[0].PublishedBySubject!;
        publishedBy.Should().NotBeNull();
        publishedBy.Kind.Should().Be(SubjectKind.Service);
        publishedBy.SubjectId.Should().Be(service.Id);
    }

    [Fact]
    public async Task GetBinding_WhenAuthorizationDenies_AuthorizesPersistedDefinitionKey()
    {
        RuleDefinitionHandlerTestContext context = new();
        context.Authorization.AuthorizeAsync(Arg.Any<ProductAuthorizationRequest>(), Arg.Any<CancellationToken>())
            .Returns(ProductAuthorizationDecision.Denied);
        RuleBinding binding = RuleBindingHandlerTestData.Binding();
        IRuleBindingRepository repository = Substitute.For<IRuleBindingRepository>();
        repository.GetByIdForWorkspaceAsync(
                binding.Id,
                RuleDefinitionHandlerTestContext.WorkspaceId,
                Arg.Any<CancellationToken>())
            .Returns(binding);
        GetRuleBindingHandler sut = new(context.CurrentUser, context.CurrentSubject, context.Authorization, repository);

        Result<RuleBindingDto> result = await sut.Handle(
            new GetRuleBindingQuery(binding.Id.Value),
            TestContext.Current.CancellationToken);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.Forbidden);
    }

    [Fact]
    public async Task GetBinding_WhenAuthorized_UsesExactBindingKey()
    {
        RuleDefinitionHandlerTestContext context = new();
        RuleBinding binding = RuleBindingHandlerTestData.Binding();
        ProductAuthorizationRequest? observed = null;
        context.Authorization.AuthorizeAsync(
                Arg.Do<ProductAuthorizationRequest>(request => observed = request),
                Arg.Any<CancellationToken>())
            .Returns(new ProductAuthorizationDecision(true, ProductActionScope.None));
        IRuleBindingRepository repository = Substitute.For<IRuleBindingRepository>();
        repository.GetByIdForWorkspaceAsync(binding.Id, RuleDefinitionHandlerTestContext.WorkspaceId, Arg.Any<CancellationToken>())
            .Returns(binding);
        GetRuleBindingHandler sut = new(context.CurrentUser, context.CurrentSubject, context.Authorization, repository);

        Result<RuleBindingDto> result = await sut.Handle(
            new GetRuleBindingQuery(binding.Id.Value),
            TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        observed.Should().NotBeNull();
        observed!.ActionKey.Should().Be(RuleProductActions.BindingRead);
        observed.ResourceType.Should().Be(RuleProductActions.BindingResourceType);
        observed.ResourceKey.Should().Be(binding.DefinitionKey.Value);
    }

    [Fact]
    public async Task CreateBinding_WhenAuthorizationDependencyFails_ReturnsUnavailableWithoutMutation()
    {
        RuleDefinitionHandlerTestContext context = new();
        context.Authorization.AuthorizeAsync(Arg.Any<ProductAuthorizationRequest>(), Arg.Any<CancellationToken>())
            .Returns<Task<ProductAuthorizationDecision>>(_ => throw new InvalidOperationException("authorization unavailable"));
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

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.Unavailable);
        result.ProblemCode.Should().Be(RulesProblemCodes.AuthorizationUnavailable);
        await repository.DidNotReceive().AddAsync(Arg.Any<RuleBinding>(), Arg.Any<CancellationToken>());
        await context.UnitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
