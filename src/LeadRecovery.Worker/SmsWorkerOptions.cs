namespace LeadRecovery.Worker;

public sealed record SmsWorkerOptions(
    Uri StatusCallbackUri,
    TimeSpan DispatchInterval,
    TimeSpan RunningLease);
