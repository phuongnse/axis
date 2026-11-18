using Axis.Tenancy.Realms;

namespace Axis.Tenancy.Tests.Realms;

public sealed class RealmPackageRunnerClientTests
{
    [Fact]
    public void TryReadId_reads_the_id_back_from_the_client_id_of_a_runner()
    {
        var id = Guid.NewGuid();

        Assert.True(RealmPackageRunnerClient.TryReadId(RealmPackageRunnerClient.ClientId(id), out var read));
        Assert.Equal(id, read);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("runner-")]
    [InlineData("runner-not-an-id")]
    [InlineData("axis-activation")]
    [InlineData("RUNNER-0198a1b2c3d47e5f8a9b0c1d2e3f4a5b")]
    [InlineData("runner-0198a1b2-c3d4-7e5f-8a9b-0c1d2e3f4a5b")]
    [InlineData("runner-0198a1b2c3d47e5f8a9b0c1d2e3f4a5b0")]
    public void TryReadId_refuses_a_client_id_that_is_not_a_runners(string? clientId)
    {
        Assert.False(RealmPackageRunnerClient.TryReadId(clientId, out _));
    }
}
