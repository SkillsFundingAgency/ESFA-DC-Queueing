﻿using System;
using ESFA.DC.Queueing.Interface.Configuration;

namespace ESFA.DC.Queueing
{
    public class TopicConfiguration : BaseConfiguration, ITopicConfiguration
    {
        public TopicConfiguration(string connectionString, string topicName, string subscriptionName, int maxConcurrentCalls, int minimumBackoffSeconds = 5, int maximumBackoffSeconds = 50, int maximumRetryCount = 10, TimeSpan? maximumCallbackTimeSpan = null)
            : base(connectionString, maxConcurrentCalls, minimumBackoffSeconds, maximumBackoffSeconds, maximumRetryCount, maximumCallbackTimeSpan)
        {
            TopicName = topicName;
            SubscriptionName = subscriptionName;
        }

        public string TopicName { get; }

        public string SubscriptionName { get; }
    }
}
