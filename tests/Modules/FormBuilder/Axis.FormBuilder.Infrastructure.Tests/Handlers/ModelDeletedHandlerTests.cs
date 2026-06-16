using axis.datamodeling.events;
using Axis.FormBuilder.Application.Services;
using Axis.FormBuilder.Domain.Aggregates;
using Axis.FormBuilder.Domain.Entities;
using Axis.FormBuilder.Domain.Enums;
using Axis.FormBuilder.Domain.ReadModels;
using Axis.FormBuilder.Domain.ValueObjects;
using Axis.FormBuilder.Infrastructure.Handlers;
using Axis.FormBuilder.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Axis.FormBuilder.Infrastructure.Tests.Handlers;

[Collection("FormBuilderDb")]
public sealed class ModelDeletedHandlerTests(FormBuilderDatabaseFixture fixture)
{
    private static readonly Guid TenantId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private static ModelDeletedHandler CreateHandler(FormBuilderDbContext ctx)
    {
        IUnitOfWork uow = Substitute.For<IUnitOfWork>();
        uow.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(call => ctx.SaveChangesAsync(call.Arg<CancellationToken>()));
        ILogger<ModelDeletedHandler> logger = Substitute.For<ILogger<ModelDeletedHandler>>();
        return new ModelDeletedHandler(ctx, uow, logger);
    }

    private static ModelDeletedEvent BuildEvent(Guid modelId) =>
        new()
        {
            modelId = modelId.ToString(),
            tenantId = TenantId.ToString(),
        };

    [Fact]
    public async Task Handle_WhenRelationPickerTargetsDeletedModel_CreatesBrokenReference()
    {
        Guid targetModelId = Guid.NewGuid();
        FormDefinition form = FormDefinition.Create($"Intake-{Guid.NewGuid():N}", null, TenantId, "user");
        FormField field = form.AddField(
            "company",
            "Company",
            FormFieldType.RelationPicker,
            false,
            new RelationPickerFieldConfig(targetModelId));

        await using (FormBuilderDbContext writeCtx = fixture.CreateContext())
        {
            writeCtx.FormDefinitions.Add(form);
            await writeCtx.SaveChangesAsync();
        }

        await using FormBuilderDbContext handlerCtx = fixture.CreateContext();
        await CreateHandler(handlerCtx).Handle(BuildEvent(targetModelId), CancellationToken.None);

        await using FormBuilderDbContext readCtx = fixture.CreateContext();
        FormModelReference? reference = await readCtx.FormModelReferences
            .SingleOrDefaultAsync(r => r.FormId == form.Id && r.FormFieldId == field.Id);

        reference.Should().NotBeNull();
        reference!.IsBroken.Should().BeTrue();
        reference.ModelId.Should().Be(targetModelId);
    }

    [Fact]
    public async Task Handle_WhenHealthyReferenceExists_MarksItBroken()
    {
        Guid targetModelId = Guid.NewGuid();
        FormDefinition form = FormDefinition.Create($"Intake-{Guid.NewGuid():N}", null, TenantId, "user");
        FormField field = form.AddField(
            "company",
            "Company",
            FormFieldType.RelationPicker,
            false,
            new RelationPickerFieldConfig(targetModelId));

        await using (FormBuilderDbContext writeCtx = fixture.CreateContext())
        {
            writeCtx.FormDefinitions.Add(form);
            writeCtx.FormModelReferences.Add(
                FormModelReference.Create(form.Id, field.Id, targetModelId, TenantId, isBroken: false));
            await writeCtx.SaveChangesAsync();
        }

        await using FormBuilderDbContext handlerCtx = fixture.CreateContext();
        await CreateHandler(handlerCtx).Handle(BuildEvent(targetModelId), CancellationToken.None);

        await using FormBuilderDbContext readCtx = fixture.CreateContext();
        FormModelReference reference = await readCtx.FormModelReferences
            .SingleAsync(r => r.FormId == form.Id && r.FormFieldId == field.Id);

        reference.IsBroken.Should().BeTrue();
    }
}
