using TbotUltra.Core.Configuration;
using TbotUltra.Desktop.Services.Orchestration;
using TbotUltra.Worker.Domain;
using TbotUltra.Worker.Services;

namespace TbotUltra.Desktop;

public partial class MainWindow
{
    private sealed class MainWindowContinuousAutomationPassPort(MainWindow owner)
        : IContinuousAutomationPassPort
    {
        public BotOptions LoadOptions() => owner.LoadBotOptions();

        public long BeginPass() => owner._automationPassRuntime.BeginContinuousPass();

        public bool TryScheduleAutomaticProxyRecovery(BotOptions options) =>
            owner.TryScheduleAutomaticProxyRecovery(options);

        public TimeSpan NetworkBackoffRemaining => owner._automationNetworkBackoff.Remaining;

        public DateTimeOffset VillageMembershipVerificationNotBeforeUtc =>
            owner._villageMembershipVerificationNotBeforeUtc;

        public DateTimeOffset NextKeepAliveAtUtc => owner._automationSessionRuntime.NextKeepAliveAtUtc;

        public bool PrioritizeDeadlineWorkOnWake
        {
            get => owner._smartSleepPrioritizeDeadlineWorkOnWake;
            set => owner._smartSleepPrioritizeDeadlineWorkOnWake = value;
        }

        public ValueTask EnsureChromiumInstalledAsync() =>
            new(owner.EnsureChromiumInstalledAsync());

        public ValueTask<bool> EnsureVillageMembershipVerifiedAsync(
            BotOptions options,
            CancellationToken cancellationToken) =>
            new(owner.EnsureVillageMembershipVerifiedBeforeAutomationAsync(options, cancellationToken));

        public bool ConsumeImmediateWorkRequest() =>
            owner._automationPassRuntime.ConsumeImmediateWorkRequest();

        public ValueTask MaybeTakeIdleBreakAsync(
            BotOptions options,
            CancellationToken cancellationToken) =>
            new(owner.MaybeTakeIdleBreakAsync(options, cancellationToken));

        public ValueTask MaybeDoIdleBrowseAsync(
            BotOptions options,
            CancellationToken cancellationToken) =>
            new(owner.MaybeDoIdleBrowseAsync(options, cancellationToken));

        public ValueTask HonorPendingVillageSwitchAsync(
            BotOptions options,
            CancellationToken cancellationToken) =>
            new(owner.HonorPendingVillageSwitchAsync(options, cancellationToken));

        public bool ConsumeForceVillageStatusRoundRequest() =>
            owner._villageStatusRoundRuntime.ConsumeForceRequest();

        public ValueTask MaybeRunVillageStatusRoundAsync(
            BotOptions options,
            CancellationToken cancellationToken,
            bool force) =>
            new(owner.MaybeRunVillageStatusSweepAsync(options, cancellationToken, force));

        public ValueTask EnsureConstructionStatusAsync(
            BotOptions options,
            CancellationToken cancellationToken) =>
            new(owner.EnsureContinuousLoopConstructionStatusAsync(options, cancellationToken));

        public ValueTask MaybeAnalyzeNewVillageAsync(
            BotOptions options,
            CancellationToken cancellationToken) =>
            new(owner.MaybeAnalyzeNewVillageDuringContinuousLoopAsync(options, cancellationToken));

        public ValueTask EnsureRuntimeItemsAsync(
            BotOptions options,
            CancellationToken cancellationToken) =>
            new(owner.EnsureContinuousLoopRuntimeItemsAsync(options, cancellationToken));

        public ValueTask MaybeCheckInboxAsync(CancellationToken cancellationToken) =>
            new(owner.MaybeCheckInboxDuringContinuousLoopAsync(cancellationToken));

        public QueueItem? SelectNextQueueItem() => owner.SelectNextQueueItemForContinuousLoop();

        public void MarkActivePass() => owner._automationSessionRuntime.MarkActivePass();

        public ValueTask MaybeKeepBrowserFreshAsync(
            BotOptions options,
            CancellationToken cancellationToken) =>
            new(owner.MaybeKeepBrowserFreshDuringContinuousLoopAsync(options, cancellationToken));

        public TimeSpan? ResolveWaitDelay(BotOptions options) =>
            owner.ResolveContinuousLoopWaitDelay(options);

        public TimeSpan? ResolveSmartSleepWaitDelay() => owner.ResolveSmartSleepWaitDelay();

        public bool TryRequestSmartSleep(DateTimeOffset? trustedDeadlineUtc) =>
            owner.TryRequestSmartSleep(trustedDeadlineUtc);

        public bool ShouldPublishIdleHeartbeat(TimeSpan interval) =>
            owner._automationSessionRuntime.ShouldPublishIdleHeartbeat(interval);

        public void Log(string message) => owner.AppendLog(message);

        public string FormatException(Exception exception) => FormatExceptionForLog(exception);

        public ValueTask HoldAccountAutomationAsync(AccountAccessException exception) =>
            new(owner.HoldAccountAutomationAsync(exception));

        public ValueTask<AutomationActionOutcome> ExecuteAsync(
            AutomationCandidate action,
            CancellationToken cancellationToken) =>
            owner.ExecuteContinuousAutomationActionAsync(action, cancellationToken);
    }
}
