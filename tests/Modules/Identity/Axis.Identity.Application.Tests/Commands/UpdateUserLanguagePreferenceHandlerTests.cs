using Axis.Identity.Application.Commands.UpdateUserLanguagePreference;
using Axis.Identity.Application.Repositories;
using Axis.Identity.Application.Services;
using Axis.Identity.Domain.Aggregates;
using Axis.Identity.Domain.ValueObjects;
using Axis.Shared.Domain.Primitives;
using FluentAssertions;
using NSubstitute;
using NSubstitute.ReturnsExtensions;

namespace Axis.Identity.Application.Tests.Commands;

public sealed class UpdateUserLanguagePreferenceHandlerTests
{
    private readonly IUserRepository _userRepository = Substitute.For<IUserRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();

    private UpdateUserLanguagePreferenceHandler CreateHandler() =>
        new(_userRepository, _unitOfWork);

    [Fact]
    public async Task Handle_WhenUserExists_StoresLanguagePreference()
    {
        User user = User.Create("Ada Lovelace", Email.Create("ada@example.com").Value);
        _userRepository.GetByIdPlatformWideAsync(user.Id, Arg.Any<CancellationToken>())
            .Returns(user);

        Result<LanguagePreferenceDto> result = await CreateHandler().Handle(
            new UpdateUserLanguagePreferenceCommand(user.Id, "vi"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Language.Should().Be("vi");
        user.LanguagePreference!.Value.Should().Be("vi");
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WhenUserDoesNotExist_ReturnsNotFoundWithoutSaving()
    {
        Guid userId = Guid.NewGuid();
        _userRepository.GetByIdPlatformWideAsync(userId, Arg.Any<CancellationToken>())
            .ReturnsNull();

        Result<LanguagePreferenceDto> result = await CreateHandler().Handle(
            new UpdateUserLanguagePreferenceCommand(userId, "vi"),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WhenLanguageIsUnsupported_ReturnsInvalidInputWithoutLookup()
    {
        Result<LanguagePreferenceDto> result = await CreateHandler().Handle(
            new UpdateUserLanguagePreferenceCommand(Guid.NewGuid(), "fr"),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.InvalidInput);
        await _userRepository.DidNotReceive().GetByIdPlatformWideAsync(
            Arg.Any<Guid>(),
            Arg.Any<CancellationToken>());
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
