using Axis.Identity.Application.Commands.UpdateUserThemePreference;
using Axis.Identity.Application.Repositories;
using Axis.Identity.Application.Services;
using Axis.Identity.Domain.Aggregates;
using Axis.Identity.Domain.ValueObjects;
using Axis.Shared.Domain.Primitives;
using FluentAssertions;
using NSubstitute;
using NSubstitute.ReturnsExtensions;

namespace Axis.Identity.Application.Tests.Commands;

public sealed class UpdateUserThemePreferenceHandlerTests
{
    private readonly IUserRepository _userRepository = Substitute.For<IUserRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();

    private UpdateUserThemePreferenceHandler CreateHandler() =>
        new(_userRepository, _unitOfWork);

    [Fact]
    public async Task Handle_WhenUserExists_StoresThemePreference()
    {
        User user = User.Create("Ada Lovelace", Email.Create("ada@example.com").Value);
        _userRepository.GetByIdPlatformWideAsync(user.Id, Arg.Any<CancellationToken>())
            .Returns(user);

        Result<ThemePreferenceDto> result = await CreateHandler().Handle(
            new UpdateUserThemePreferenceCommand(user.Id, "dark"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Theme.Should().Be("dark");
        user.ThemePreference!.Value.Should().Be("dark");
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WhenUserDoesNotExist_ReturnsNotFoundWithoutSaving()
    {
        Guid userId = Guid.NewGuid();
        _userRepository.GetByIdPlatformWideAsync(userId, Arg.Any<CancellationToken>())
            .ReturnsNull();

        Result<ThemePreferenceDto> result = await CreateHandler().Handle(
            new UpdateUserThemePreferenceCommand(userId, "dark"),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WhenThemeIsUnsupported_ReturnsInvalidInputWithoutLookup()
    {
        Result<ThemePreferenceDto> result = await CreateHandler().Handle(
            new UpdateUserThemePreferenceCommand(Guid.NewGuid(), "contrast"),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.InvalidInput);
        await _userRepository.DidNotReceive().GetByIdPlatformWideAsync(
            Arg.Any<Guid>(),
            Arg.Any<CancellationToken>());
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
