﻿using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ESFA.DC.DateTimeProvider.Interface;
using ESFA.DC.Logging.Interfaces;
using Microsoft.Azure.ServiceBus.Core;

namespace ESFA.DC.Queueing.MessageLocking
{
    public sealed class MessageLockManager : IDisposable
    {
        private readonly ILogger _logger;

        private readonly IDateTimeProvider _dateTimeProvider;

        private readonly IReceiverClient _receiverClient;

        private readonly LockMessage _message;

        private readonly CancellationTokenSource _cancellationTokenSource;

        private readonly CancellationToken _cancellationToken;

        private readonly SemaphoreSlim _locker;

        private bool isMessageActioned;

        private Timer _timer;

        public MessageLockManager(ILogger logger, IDateTimeProvider dateTimeProvider, IReceiverClient receiverClient, LockMessage message, CancellationTokenSource cancellationTokenSource, CancellationToken cancellationToken)
        {
            _logger = logger;
            _dateTimeProvider = dateTimeProvider;
            _receiverClient = receiverClient;
            _message = message;
            _cancellationTokenSource = cancellationTokenSource;
            _cancellationToken = cancellationToken;
            _locker = new SemaphoreSlim(1, 1);
            isMessageActioned = false;
        }

        public void Dispose()
        {
            AbandonAsync().Wait(_cancellationToken);
        }

        public async Task InitializeSession()
        {
            TimeSpan lockedUntil = _message.LockedUntilUtc.Subtract(_dateTimeProvider.GetNowUtc());
            TimeSpan renewInterval = new TimeSpan(
                (long)Math.Round(
                    lockedUntil.Ticks * 0.9,
                    0,
                    MidpointRounding.AwayFromZero));

            if (renewInterval.TotalMilliseconds < 0)
            {
                _logger.LogError($"Invalid message lock renewel value {renewInterval} for message {_message.MessageId}. Rejecting message.");
                await DoActionAsync(MessageAction.Abandon);
                return;
            }

            _logger.LogInfo($"Message {_message.MessageId} will be given {renewInterval.Minutes} minutes to execute before automatic cancellation.");

            _timer = new Timer(Callback, null, renewInterval, TimeSpan.FromMilliseconds(-1));
        }

        public async Task CompleteAsync()
        {
            await DoActionAsync(MessageAction.Complete);
        }

        public async Task AbandonAsync(Exception ex = null)
        {
            await DoActionAsync(MessageAction.Abandon, ex);
        }

        public async Task DeadLetterAsync(Exception ex = null)
        {
            await DoActionAsync(MessageAction.DeadLetter, ex);
        }

        private void Callback(object state)
        {
            _logger.LogWarning($"Message {_message.MessageId} did not process in expected time, it will be abandoned and work cancelled.");
            _timer?.Change(Timeout.Infinite, Timeout.Infinite);
            AbandonAsync().Wait(CancellationToken.None); // Timer will be disposed
            _cancellationTokenSource.Cancel(); // Cancel at the end so that we don't prevent processing of the message
        }

        private async Task DoActionAsync(MessageAction messageAction, Exception ex = null)
        {
            try
            {
                await _locker.WaitAsync(_cancellationToken);

                if (!CanAction())
                {
                    return;
                }

                switch (messageAction)
                {
                    case MessageAction.Complete:
                        await _receiverClient.CompleteAsync(_message.LockToken);
                        break;
                    case MessageAction.Abandon:
                        await _receiverClient.AbandonAsync(_message.LockToken, GetProperties(_message.UserProperties, ex));
                        break;
                    case MessageAction.DeadLetter:
                        await _receiverClient.DeadLetterAsync(_message.LockToken, GetProperties(_message.UserProperties, ex));
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(messageAction), messageAction, null);
                }

                isMessageActioned = true;
            }
            catch (Exception ex2)
            {
                _logger.LogError("Failed to action a message", ex2);
            }
            finally
            {
                _locker.Release();
            }
        }

        private bool CanAction()
        {
            if (_cancellationToken.IsCancellationRequested)
            {
                return false;
            }

            if (isMessageActioned)
            {
                return false;
            }

            _timer?.Dispose();
            _timer = null;

            if (_message == null)
            {
                return false;
            }

            return true;
        }

        private IDictionary<string, object> GetProperties(
            IDictionary<string, object> messageUserProperties,
            Exception ex)
        {
            if (ex == null)
            {
                return new Dictionary<string, object>();
            }

            if (messageUserProperties.TryGetValue("Exceptions", out var exceptions))
            {
                exceptions = $"{exceptions}:{ex.GetType().Name}";
            }
            else
            {
                exceptions = ex.GetType().Name;
            }

            return new Dictionary<string, object>
            {
                { "Exceptions", exceptions }
            };
        }
    }
}
