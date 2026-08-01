using Axis.Rules.Application.Commands.CreateRuleBinding;
using Axis.Rules.Application.Commands.DeleteRuleBinding;
using Axis.Rules.Application.Commands.UpdateRuleBinding;
using Axis.Rules.Application.Queries.ListRuleBindingUsage;
using Axis.Rules.Application.Repositories;
using Axis.Rules.Contracts;
using Axis.Rules.Domain;
using FluentAssertions;
using NSubstitute;
using ContractRuleInputMappingKind = Axis.Rules.Contracts.RuleInputMappingKind;

namespace Axis.Rules.Application.Tests.Commands;

public sealed class RuleBindingHandlerTests
{
    [Fact]
    public async Task RuleBindingLifecycle_CreatesUpdatesAndDeletes_WithoutChangingDefinition()
    {
        RuleDefinition definition = SystemRuleCatalog.Find("field.required", 1)!;
        int definitionRevision = definition.Revision;
        IRuleBindingRepository bindings = Substitute.For<IRuleBindingRepository>();
        RuleDefinitionHandlerTestContext context = new();
        RuleBinding? persisted = null;
        bindings.AddAsync(
                Arg.Do<RuleBinding>(binding => persisted = binding),
                Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        CreateRuleBindingHandler create = new(context.CurrentUser, context.Repository, bindings, context.UnitOfWork);

        CreateRuleBindingRequest createRequest = Request("field-1");
        Shared.Domain.Primitives.Result<RuleBindingDto> created = await create.Handle(
            new CreateRuleBindingCommand(createRequest),
            CancellationToken.None);

        created.IsSuccess.Should().BeTrue();
        await bindings.Received(1).AddAsync(Arg.Any<RuleBinding>(), Arg.Any<CancellationToken>());
        persisted.Should().NotBeNull();
        RuleBinding binding = persisted!;
        binding.DefinitionVersion.Should().Be(1);

        bindings.GetByIdForWorkspaceAsync(
                Arg.Any<RuleBindingId>(),
                RuleDefinitionHandlerTestContext.WorkspaceId,
                Arg.Any<CancellationToken>())
            .Returns(binding);
        UpdateRuleBindingHandler update = new(context.CurrentUser, context.Repository, bindings, context.UnitOfWork);
        Shared.Domain.Primitives.Result<RuleBindingDto> updated = await update.Handle(
            new UpdateRuleBindingCommand(
                binding.Id.Value,
                new UpdateRuleBindingRequest(
                    binding.Revision,
                    "field.required",
                    1,
                    "invoice-field",
                    "field-2",
                    "record.validate",
                    new Dictionary<string, RuleInputMappingDto>
                    {
                        ["value"] = new(ContractRuleInputMappingKind.Literal, null, ["Approved"]),
                    },
                    Priority: 10,
                    Enabled: false)),
            CancellationToken.None);

        updated.IsSuccess.Should().BeTrue();
        binding.TargetId.Should().Be("field-2");
        binding.Enabled.Should().BeFalse();
        binding.Revision.Should().Be(2);

        bindings.ListByDefinitionAsync(
                Arg.Any<RuleDefinitionKey>(),
                1,
                RuleDefinitionHandlerTestContext.WorkspaceId,
                Arg.Any<CancellationToken>())
            .Returns([binding]);
        ListRuleBindingUsageHandler usage = new(context.CurrentUser, bindings);
        Shared.Domain.Primitives.Result<IReadOnlyList<RuleBindingUsageDto>> usages = await usage.Handle(
            new ListRuleBindingUsageQuery("field.required", 1),
            CancellationToken.None);

        usages.IsSuccess.Should().BeTrue();
        usages.Value.Should().ContainSingle(item => item.BindingId == binding.Id.Value && item.TargetId == "field-2");

        DeleteRuleBindingHandler delete = new(context.CurrentUser, bindings, context.UnitOfWork);
        Shared.Domain.Primitives.Result deleted = await delete.Handle(
            new DeleteRuleBindingCommand(binding.Id.Value),
            CancellationToken.None);

        deleted.IsSuccess.Should().BeTrue();
        bindings.Received(1).Remove(binding);
        definition.Revision.Should().Be(definitionRevision);
    }

    private static CreateRuleBindingRequest Request(string targetId) =>
        new(
            "field.required",
            1,
            "invoice-field",
            targetId,
            "record.validate",
            new Dictionary<string, RuleInputMappingDto>
            {
                ["value"] = new(ContractRuleInputMappingKind.Literal, null, ["Approved"]),
            });

}
