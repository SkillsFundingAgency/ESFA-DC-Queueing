﻿namespace ESFA.DC.Queueing.Interface
{
    public interface IQueueConfiguration
    {
        string ConnectionString { get; }

        string QueueName { get; }

        int MaxConcurrentCalls { get; }

        int MinimumBackoffSeconds { get; }

        int MaximumBackoffSeconds { get; }

        int MaximumRetryCount { get; }
    }
}
