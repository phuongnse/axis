using System.Net;
using System.Net.Http.Headers;
using Axis.Agent.Models;
using Microsoft.Extensions.Logging.Abstractions;
using static Axis.Agent.Tests.ModelTestData;

namespace Axis.Agent.Tests;

public sealed class ModelAdapterRetryTests
{
    private const string Answered = """{"content":[{"type":"text","text":"ok"}],"stop_reason":"end_turn","usage":{"input_tokens":1,"output_tokens":1}}""";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(429)]
    [InlineData(529)]
    public async Task A_rate_limit_with_retry_after_zero_followed_by_an_answer_returns_it_after_two_requests(int status)
    {
        var handler = new RecordingHandler()
            .Answer((HttpStatusCode)status, "{}", response => response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.Zero))
            .Answer(HttpStatusCode.OK, Answered);

        var answer = await Adapter(handler).CallAsync(Call(), CancellationToken);

        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal("ok", Assert.IsType<ModelText>(Assert.Single(answer.Content)).Text);
    }

    [Fact]
    public async Task Four_rate_limits_in_a_row_fail_with_a_message_that_names_the_binding_and_the_status()
    {
        var handler = new RecordingHandler();
        for (var attempt = 0; attempt < 4; attempt++)
        {
            handler.Answer((HttpStatusCode)429, "{}", response => response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.Zero));
        }

        var exception = await Assert.ThrowsAsync<ModelCallException>(() => Adapter(handler).CallAsync(Call(), CancellationToken));

        Assert.Equal(4, handler.Requests.Count);
        Assert.Contains("'main'", exception.Message, StringComparison.Ordinal);
        Assert.Contains("429", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_rate_limit_with_no_retry_after_waits_one_second_and_then_tries_again()
    {
        using var time = new ManualTimeProvider();
        var handler = new RecordingHandler().Answer((HttpStatusCode)429).Answer(HttpStatusCode.OK, Answered);

        var pending = Adapter(handler, time).CallAsync(Call(), CancellationToken);
        await time.WaitForTimerAsync();
        Assert.Single(handler.Requests);
        time.Advance(TimeSpan.FromSeconds(1));
        await pending;

        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task An_error_that_is_not_a_rate_limit_fails_after_one_request()
    {
        var handler = new RecordingHandler().Answer(HttpStatusCode.Unauthorized, "{}");

        var exception = await Assert.ThrowsAsync<ModelCallException>(() => Adapter(handler).CallAsync(Call(), CancellationToken));

        Assert.Single(handler.Requests);
        Assert.Contains("401", exception.Message, StringComparison.Ordinal);
    }

    private static AnthropicModelAdapter Adapter(RecordingHandler handler, TimeProvider? time = null) =>
        new(Binding(ModelBindingKind.Anthropic), new HttpClient(handler), time ?? TimeProvider.System, NullLogger.Instance);

    /// <summary>A clock that moves only when the test advances it, with one timer at a time.</summary>
    private sealed class ManualTimeProvider : TimeProvider, IDisposable
    {
        private readonly TaskCompletionSource _timerCreated = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private ManualTimer? _timer;

        public void Dispose() => _timer?.Dispose();

        public Task WaitForTimerAsync() => _timerCreated.Task;

        public void Advance(TimeSpan by)
        {
            Assert.NotNull(_timer);
            Assert.True(by >= _timer.Due);
            _timer.Fire();
        }

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            _timer = new ManualTimer(callback, state, dueTime);
            _timerCreated.TrySetResult();
            return _timer;
        }

        private sealed class ManualTimer(TimerCallback callback, object? state, TimeSpan due) : ITimer
        {
            public TimeSpan Due { get; } = due;

            public void Fire() => callback(state);

            public bool Change(TimeSpan dueTime, TimeSpan period) => true;

            public void Dispose()
            {
            }

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}
