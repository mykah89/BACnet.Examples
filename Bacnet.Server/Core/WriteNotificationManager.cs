using Bacnet.Server.Interface;
using Bacnet.Server.Utility;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO.BACnet;
using System.IO.BACnet.Storage.Objective;
using System.IO.BACnet.Storage.Subscription;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Bacnet.Server.Core
{
    public class WriteNotificationManager : IWriteNotificationManager
    {
        private class CovManagementNotifyKey
        {
            public BacnetPropertyIds PropertyId { get; set; }
            public BaCSharpObject Sender { get; set; }

            public CovManagementNotifyKey(BaCSharpObject sender, BacnetPropertyIds propertyId)
            {
                Sender = sender;
                PropertyId = propertyId;
            }

            public override bool Equals(object obj)
            {
                CovManagementNotifyKey item = obj as CovManagementNotifyKey;

                if (item == null)
                {
                    return false;
                }

                bool b1 = this.Sender.Equals(item.Sender.PROP_OBJECT_IDENTIFIER);
                bool b2 = this.PropertyId.Equals(item.PropertyId);
                bool b3 = this.PropertyId == item.PropertyId;

                return this.Sender.Equals(item.Sender.PROP_OBJECT_IDENTIFIER) && this.PropertyId.Equals(item.PropertyId);
            }
            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = 17;
                    hash = hash * 23 + Sender.GetHashCode();
                    hash = hash * 23 + PropertyId.GetHashCode();
                    return hash;
                }
            }
        }
        private class CovManagementNotifyValue
        {
            public CovManagementNotifyValue(Task task, CancellationTokenSource cancellationTokenSource, DateTime utcNow)
            {
                Task = task;
                CancellationTokenSource = cancellationTokenSource;
                Timestamp = utcNow;
            }

            public CancellationTokenSource CancellationTokenSource { get; set; }
            public Task Task { get; set; }
            public DateTime Timestamp { get; set; }
        }

        private readonly uint _deviceId;
        private readonly ILogger<WriteNotificationManager> _logger;
        private readonly ISubscriptionManager _subscriptionManager;
        private readonly ConcurrentDictionary<CovManagementNotifyKey, CovManagementNotifyValue> _writeTasks = new ConcurrentDictionary<CovManagementNotifyKey, CovManagementNotifyValue>();

        public WriteNotificationManager(ISubscriptionManager subscriptionManager, uint deviceId, ILoggerFactory loggerFactory)
        {
            Ensure.ArgumentNotNull(subscriptionManager, nameof(subscriptionManager));
            Ensure.ArgumentNotNull(loggerFactory, nameof(loggerFactory));

            _deviceId = deviceId;
            _logger = loggerFactory.CreateLogger<WriteNotificationManager>();
            _subscriptionManager = subscriptionManager;
        }

        public void NotifyWrite(BaCSharpObject sender, BacnetPropertyIds propId)
        {
            lock (sender)
            {
                CancellationTokenSource cts = new CancellationTokenSource();

                Func<Task> writeFunc = async () =>
                {
                    if (cts.IsCancellationRequested)
                    {
                        return;
                    }

                    _subscriptionManager.RemoveOldSubscriptions();

                    IEnumerable<Subscription> subs = _subscriptionManager.GetSubscriptionsForObject(sender.PROP_OBJECT_IDENTIFIER);

                    if (subs == null)
                    {
                        return; // nobody
                    }

                    IList<BacnetValue> value;
                    BacnetPropertyReference br = new BacnetPropertyReference((uint)propId, (uint)System.IO.BACnet.Serialize.ASN1.BACNET_ARRAY_ALL);
                    ErrorCodes error = sender.ReadPropertyValue(br, out value);

                    // this should never not be good
                    if (error == ErrorCodes.Good)
                    {
                        BacnetPropertyValue tmp = new BacnetPropertyValue();
                        tmp.value = value;
                        tmp.property = br;

                        BacnetPropertyValue[] values = new BacnetPropertyValue[] { tmp };

                        //send to all
                        foreach (Subscription sub in subs)
                        {
                            if (sub.MonitoredProperty.propertyIdentifier == (uint)BacnetPropertyIds.PROP_ALL || sub.MonitoredProperty.propertyIdentifier == (uint)propId)
                            {
                                tmp.property = sub.MonitoredProperty;

                                try
                                {
                                    bool notifySuccess = await Task.Run(() => sub.Reciever.Notify(sub.Reciever_address, sub.SubscriberProcessIdentifier, _deviceId, sub.MonitoredObjectIdentifier, (uint)sub.GetTimeRemaining(), sub.IssueConfirmedNotifications, values));

                                    if (!notifySuccess)
                                    {
                                        _logger.LogWarning("Notify failed receiver will be removed from subscriptions.");

                                        _subscriptionManager.RemoveReceiver(sub.Reciever_address);
                                    }
                                }
                                catch (Exception ex)
                                {
                                    bool isTimeout = ex is BacnetApduTimeoutException;

                                    _logger.LogError(ex, $"DeviceId: {_deviceId} - COV management notify{(isTimeout ? " timeout " : " ")}exception, propId: {propId}, subMonitoredObject: {sub.MonitoredObjectIdentifier.ToString()}, issue confirmed notifications: {sub.IssueConfirmedNotifications}, receiver: {sub.Reciever_address.ToString()} will be removed from subscription");

                                    _subscriptionManager.RemoveReceiver(sub.Reciever_address);
                                }
                            }
                        }
                    }
                    else
                    {
                        _logger.LogError($"Error reading property value from change notify. Object: {sender.ToString()}, Property: {propId.ToString()}, Code: {error.ToString()}.");
                    }
                };

                CovManagementNotifyKey mnk = new CovManagementNotifyKey(sender, propId);

                _writeTasks.TryGetValue(mnk, out CovManagementNotifyValue existing);

                if (existing == null)
                {
                    Task writeTask = Task.Factory.StartNew(writeFunc, TaskCreationOptions.PreferFairness);

                    CovManagementNotifyValue mnv = new CovManagementNotifyValue(writeTask, cts, DateTime.UtcNow);

                    _writeTasks.TryAdd(mnk, mnv);
                }
                else
                {
                    // cancel the previous task if possible
                    existing.CancellationTokenSource.Cancel();

                    existing.Timestamp = DateTime.UtcNow;
                    existing.Task = writeFunc.Invoke();
                    existing.CancellationTokenSource = cts;

                    // continue with the new value
                    existing.Task.ContinueWith((t) =>
                    {
                        t.Dispose();

                        return writeFunc;
                    });
                }
            }
        }
    }
}
