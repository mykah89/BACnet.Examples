using Bacnet.Server.Core;
using Bacnet.Server.Interface;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO.BACnet;
using System.IO.BACnet.Storage.Objective;
using System.IO.BACnet.Storage.Subscription;
using System.Text;
using System.Threading.Tasks;

namespace Bacnet.Server
{
    public class ObjectiveStorageBacnetActivity
    {
        public DeviceObject DeviceObject { get { return _device; } }
        public Dictionary<BacnetObjectId, Subscription[]> Subscriptions { get { return _subscriptionManager.Subscriptions; } }

        private readonly BacnetClient _client;
        private readonly DeviceObject _device;
        private readonly uint _deviceId;
        private readonly ILogger<ObjectiveStorageBacnetActivity> _logger;
        private readonly ISubscriptionManager _subscriptionManager;
        private readonly IWriteNotificationManager _writeNotificationManager;

        public ObjectiveStorageBacnetActivity(DeviceObject deviceObject, BacnetClient client, ILoggerFactory loggerFactory)
        {
            _client = client;
            _device = deviceObject;
            _deviceId = deviceObject.PROP_OBJECT_IDENTIFIER.Instance;
            _logger = loggerFactory.CreateLogger<ObjectiveStorageBacnetActivity>();

            _subscriptionManager = new SubscriptionManager(loggerFactory);
            _writeNotificationManager = new WriteNotificationManager(_subscriptionManager, deviceObject.PROP_OBJECT_IDENTIFIER.Instance, loggerFactory);

            foreach (BaCSharpObject obj in DeviceObject.ObjectsList)
            {
                obj.OnWriteNotify += _writeNotificationManager.NotifyWrite;
            }

            deviceObject.SetSupportedServiceBit((byte)BacnetServicesSupported.SERVICE_SUPPORTED_I_AM, true);
            deviceObject.SetSupportedServiceBit((byte)BacnetServicesSupported.SERVICE_SUPPORTED_WHO_IS, true);
            deviceObject.SetSupportedServiceBit((byte)BacnetServicesSupported.SERVICE_SUPPORTED_READ_PROP_MULTIPLE, true);
            deviceObject.SetSupportedServiceBit((byte)BacnetServicesSupported.SERVICE_SUPPORTED_READ_PROPERTY, true);
            deviceObject.SetSupportedServiceBit((byte)BacnetServicesSupported.SERVICE_SUPPORTED_WRITE_PROPERTY, true);
            deviceObject.SetSupportedServiceBit((byte)BacnetServicesSupported.SERVICE_SUPPORTED_SUBSCRIBE_COV, true);
            deviceObject.SetSupportedServiceBit((byte)BacnetServicesSupported.SERVICE_SUPPORTED_SUBSCRIBE_COV_PROPERTY, true);
            deviceObject.SetSupportedServiceBit((byte)BacnetServicesSupported.SERVICE_SUPPORTED_CONFIRMED_COV_NOTIFICATION, true);
            deviceObject.SetSupportedServiceBit((byte)BacnetServicesSupported.SERVICE_SUPPORTED_UNCONFIRMED_COV_NOTIFICATION, true);
            deviceObject.SetSupportedServiceBit((byte)BacnetServicesSupported.SERVICE_SUPPORTED_READ_RANGE, true);

            _client.OnIam += handler_OnIam;
            _client.OnWhoIs += handler_OnWhoIs;
            _client.OnReadPropertyRequest += handler_OnReadPropertyRequest;
            _client.OnReadPropertyMultipleRequest += handler_OnReadPropertyMultipleRequest;
            _client.OnWritePropertyRequest += handler_OnWritePropertyRequest;
            _client.OnSubscribeCOV += handler_OnSubscribeCOV;
            _client.OnSubscribeCOVProperty += handler_OnSubscribeCOVProperty;
            _client.OnReadRange += handler_OnReadRange;

            // todo: future implementation

            // deviceObject.SetSupportedServiceBit((byte)BacnetServicesSupported.SERVICE_SUPPORTED_ATOMIC_READ_FILE, true);
            // deviceObject.SetSupportedServiceBit((byte)BacnetServicesSupported.SERVICE_SUPPORTED_ATOMIC_WRITE_FILE, true);
            // _client.OnAtomicWriteFileRequest += handler_OnAtomicWriteFileRequest;
            // _client.OnAtomicReadFileRequest += handler_OnAtomicReadFileRequest;

            // A sample to show CreateObject & DeleteObject
            // Create & Delete Object by C. Gunter
            //  OBJECT_ANALOG_INPUT sample
            // _client.OnCreateObjectRequest += handler_OnCreateObjectRequest;
            // _client.OnDeleteObjectRequest += handler_OnDeleteObjectRequest;
            // _device.SetSupportedServiceBit((byte)BacnetServicesSupported.SERVICE_SUPPORTED_CREATE_OBJECT, true);
            // _device.SetSupportedServiceBit((byte)BacnetServicesSupported.SERVICE_SUPPORTED_DELETE_OBJECT, true);
        }

        private void handler_OnIam(BacnetClient sender, BacnetAddress adr, uint device_id, uint max_apdu, BacnetSegmentations segmentation, ushort vendor_id)
        {
            _device.ReceivedIam(sender, adr, device_id);
        }
        private void handler_OnReadPropertyMultipleRequest(BacnetClient sender, BacnetAddress adr, byte invoke_id, IList<BacnetReadAccessSpecification> properties, BacnetMaxSegments max_segments)
        {
            Task.Factory.StartNew(async () =>
            {
                try
                {
                    IList<BacnetPropertyValue> value;
                    List<BacnetReadAccessResult> values = new List<BacnetReadAccessResult>();
                    foreach (BacnetReadAccessSpecification p in properties)
                    {
                        BaCSharpObject bacobj = _device.FindBacnetObject(p.objectIdentifier);

                        if (bacobj != null)
                        {
                            lock (bacobj)
                            {
                                if (p.propertyReferences.Count == 1 && p.propertyReferences[0].propertyIdentifier == (uint)BacnetPropertyIds.PROP_ALL)
                                {
                                    if (!bacobj.ReadPropertyAll(sender, adr, out value))
                                    {
                                        sender.ErrorResponse(adr, BacnetConfirmedServices.SERVICE_CONFIRMED_READ_PROP_MULTIPLE, invoke_id, BacnetErrorClasses.ERROR_CLASS_OBJECT, BacnetErrorCodes.ERROR_CODE_UNKNOWN_OBJECT);
                                        return;
                                    }
                                }
                                else
                                {
                                    bacobj.ReadPropertyMultiple(sender, adr, p.propertyReferences, out value);
                                }

                                values.Add(new BacnetReadAccessResult(p.objectIdentifier, value));
                            }
                        }
                        else
                        {
                            await Task.Run(() => sender.ErrorResponse(adr, BacnetConfirmedServices.SERVICE_CONFIRMED_READ_PROP_MULTIPLE, invoke_id, BacnetErrorClasses.ERROR_CLASS_OBJECT, BacnetErrorCodes.ERROR_CODE_UNKNOWN_OBJECT));
                        }
                    }

                    await Task.Run(() => sender.ReadPropertyMultipleResponse(adr, invoke_id, sender.GetSegmentBuffer(max_segments), values));
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"DeviceId: {_deviceId} - Exception during read property multiple, address: {adr.ToString()}.");

                    await Task.Run(() => sender.ErrorResponse(adr, BacnetConfirmedServices.SERVICE_CONFIRMED_READ_PROP_MULTIPLE, invoke_id, BacnetErrorClasses.ERROR_CLASS_DEVICE, BacnetErrorCodes.ERROR_CODE_OTHER));
                }
            }, TaskCreationOptions.PreferFairness);
        }
        private void handler_OnReadPropertyRequest(BacnetClient sender, BacnetAddress adr, byte invoke_id, BacnetObjectId object_id, BacnetPropertyReference property, BacnetMaxSegments max_segments)
        {
            Task.Factory.StartNew(async () =>
            {
                BaCSharpObject bacobj = _device.FindBacnetObject(object_id);

                if (bacobj != null)
                {
                    lock (bacobj)
                    {
                        IList<BacnetValue> value;
                        ErrorCodes error = bacobj.ReadPropertyValue(sender, adr, property, out value);
                        if (error == ErrorCodes.Good)
                        {
                            sender.ReadPropertyResponse(adr, invoke_id, sender.GetSegmentBuffer(max_segments), object_id, property, value);
                        }
                        else
                        {
                            _logger.LogDebug($"Request for unknown property on object {object_id.ToString()}, property: {property.ToString()} from {adr.ToString()}");

                            sender.ErrorResponse(adr, BacnetConfirmedServices.SERVICE_CONFIRMED_READ_PROPERTY, invoke_id, BacnetErrorClasses.ERROR_CLASS_DEVICE, BacnetErrorCodes.ERROR_CODE_UNKNOWN_PROPERTY);
                        }
                    }
                }
                else
                {
                    _logger.LogError($"DeviceId: {_deviceId} - Failed to locate expected object {object_id.ToString()} for {adr.ToString()}.");

                    await Task.Run(() => sender.ErrorResponse(adr, BacnetConfirmedServices.SERVICE_CONFIRMED_READ_PROPERTY, invoke_id, BacnetErrorClasses.ERROR_CLASS_DEVICE, BacnetErrorCodes.ERROR_CODE_UNKNOWN_OBJECT));
                }
            }, TaskCreationOptions.PreferFairness);
        }
        private void handler_OnReadRange(BacnetClient sender, BacnetAddress adr, byte invoke_id, BacnetObjectId objectId, BacnetPropertyReference property, System.IO.BACnet.Serialize.BacnetReadRangeRequestTypes requestType, uint position, DateTime time, int count, BacnetMaxSegments max_segments)
        {
            throw new NotImplementedException();

            //lock (_device)
            //{
            //    BaCSharpObject trend = _device.FindBacnetObject(objectId);

            //    if (trend is TrendLog)
            //    {
            //        BacnetResultFlags status;
            //        byte[] application_data = (trend as TrendLog).GetEncodedTrends(position, count, out status);

            //        if (application_data != null)
            //        {
            //            //send
            //            sender.ReadRangeResponse(adr, invoke_id, sender.GetSegmentBuffer(max_segments), objectId, property, status, (uint)count, application_data, requestType, position);
            //        }
            //    }
            //    else
            //    {
            //        sender.ErrorResponse(adr, BacnetConfirmedServices.SERVICE_CONFIRMED_READ_RANGE, invoke_id, BacnetErrorClasses.ERROR_CLASS_DEVICE, BacnetErrorCodes.ERROR_CODE_OTHER);
            //    }
            //}
        }
        private void handler_OnSubscribeCOV(BacnetClient sender, BacnetAddress adr, byte invoke_id, uint subscriberProcessIdentifier, BacnetObjectId monitoredObjectIdentifier, bool cancellationRequest, bool issueConfirmedNotifications, uint lifetime, BacnetMaxSegments max_segments)
        {
            Task.Factory.StartNew(async () =>
            {
                _logger.LogDebug($"Received subscription request for {monitoredObjectIdentifier.ToString()}, issue confirmed notifications: {issueConfirmedNotifications}.");

                BaCSharpObject bacobj = _device.FindBacnetObject(monitoredObjectIdentifier);
                if (bacobj != null)
                {
                    Subscription sub = _subscriptionManager.HandleSubscriptionRequest(sender, adr, invoke_id, subscriberProcessIdentifier, monitoredObjectIdentifier, (uint)BacnetPropertyIds.PROP_ALL, cancellationRequest, issueConfirmedNotifications, lifetime, 0);

                    //send confirm
                    sender.SimpleAckResponse(adr, BacnetConfirmedServices.SERVICE_CONFIRMED_SUBSCRIBE_COV, invoke_id);

                    if (!cancellationRequest && sub != null)
                    {
                        lock (sub)
                        {
                            //also send first values

                            IList<BacnetPropertyValue> values;
                            if (bacobj.ReadPropertyAll(sender, adr, out values))
                            {
                                try
                                {
                                    sender.Notify(adr, sub.SubscriberProcessIdentifier, _deviceId, sub.MonitoredObjectIdentifier, (uint)sub.GetTimeRemaining(), sub.IssueConfirmedNotifications, values);
                                }
                                catch (Exception ex)
                                {
                                    bool isTimeout = ex is BacnetApduTimeoutException;

                                    _logger.LogError(ex, $"DeviceId: {_deviceId} - Subscribe COV property{(isTimeout ? " timeout " : "  ")}exception, monitored property: {BacnetPropertyIds.PROP_ALL.ToString()}, issue confirmed notifications: {sub.IssueConfirmedNotifications}, receiver: {sub.Reciever_address.ToString()}.");
                                }
                            }
                        }
                    }
                }
                else
                {
                    await Task.Run(() => sender.ErrorResponse(adr, BacnetConfirmedServices.SERVICE_CONFIRMED_SUBSCRIBE_COV, invoke_id, BacnetErrorClasses.ERROR_CLASS_SERVICES, BacnetErrorCodes.ERROR_CODE_COV_SUBSCRIPTION_FAILED));
                }
            }, TaskCreationOptions.PreferFairness);
        }
        private void handler_OnSubscribeCOVProperty(BacnetClient sender, BacnetAddress adr, byte invoke_id, uint subscriberProcessIdentifier, BacnetObjectId monitoredObjectIdentifier, BacnetPropertyReference monitoredProperty, bool cancellationRequest, bool issueConfirmedNotifications, uint lifetime, float covIncrement, BacnetMaxSegments max_segments)
        {
            Task.Factory.StartNew(async () =>
            {
                BaCSharpObject bacobj = _device.FindBacnetObject(monitoredObjectIdentifier);
                if (bacobj != null)
                {
                    Subscription sub = _subscriptionManager.HandleSubscriptionRequest(sender, adr, invoke_id, subscriberProcessIdentifier, monitoredObjectIdentifier, monitoredProperty.propertyIdentifier, cancellationRequest, issueConfirmedNotifications, lifetime, covIncrement);

                    //send confirm
                    sender.SimpleAckResponse(adr, BacnetConfirmedServices.SERVICE_CONFIRMED_SUBSCRIBE_COV_PROPERTY, invoke_id);

                    if (!cancellationRequest && sub != null)
                    {
                        lock (sub)
                        {
                            IList<BacnetValue> _values;
                            bacobj.ReadPropertyValue(sender, adr, monitoredProperty, out _values);

                            List<BacnetPropertyValue> values = new List<BacnetPropertyValue>();
                            BacnetPropertyValue tmp = new BacnetPropertyValue();
                            tmp.property = sub.MonitoredProperty;
                            tmp.value = _values;
                            values.Add(tmp);

                            try
                            {
                                sender.Notify(adr, sub.SubscriberProcessIdentifier, _deviceId, sub.MonitoredObjectIdentifier, (uint)sub.GetTimeRemaining(), sub.IssueConfirmedNotifications, values);
                            }
                            catch (Exception ex)
                            {
                                bool isTimeout = ex is BacnetApduTimeoutException;

                                _logger.LogError(ex, $"DeviceId: {_deviceId} - Subscribe COV property{(isTimeout ? " timeout " : "  ")}exception, subMonitoredObject: {sub.MonitoredObjectIdentifier.ToString()}, monitored property: {monitoredProperty.ToString()}, issue confirmed notifications: {sub.IssueConfirmedNotifications}.");
                            }
                        }
                    }
                }
                else
                {
                    await Task.Run(() => sender.ErrorResponse(adr, BacnetConfirmedServices.SERVICE_CONFIRMED_SUBSCRIBE_COV_PROPERTY, invoke_id, BacnetErrorClasses.ERROR_CLASS_DEVICE, BacnetErrorCodes.ERROR_CODE_OTHER));
                }
            }, TaskCreationOptions.PreferFairness);
        }
        private void handler_OnWhoIs(BacnetClient sender, BacnetAddress adr, int low_limit, int high_limit)
        {
            if (low_limit != -1 && _deviceId < low_limit) return;
            else if (high_limit != -1 && _deviceId > high_limit) return;

            BacnetSegmentations supportedSegmentation = (BacnetSegmentations)_device.PROP_SEGMENTATION_SUPPORTED;

            if (sender?.Transport?.GetBroadcastAddress() == adr)
            {
                sender.Iam(_deviceId, supportedSegmentation);
            }
            else
            {
                _client.Iam(_deviceId, supportedSegmentation, adr);
            }
        }
        private void handler_OnWritePropertyRequest(BacnetClient sender, BacnetAddress adr, byte invoke_id, BacnetObjectId object_id, BacnetPropertyValue value, BacnetMaxSegments max_segments)
        {
            BaCSharpObject bacobj = _device.FindBacnetObject(object_id);

            if (bacobj != null)
            {
                lock (bacobj)
                {
                    ErrorCodes error = bacobj.WritePropertyValue(sender, adr, value, true);

                    Task.Factory.StartNew(async () =>
                    {
                        if (error == ErrorCodes.Good)
                        {
                            await Task.Run(() => sender.SimpleAckResponse(adr, BacnetConfirmedServices.SERVICE_CONFIRMED_WRITE_PROPERTY, invoke_id));
                        }
                        else
                        {
                            BacnetErrorCodes bacEr = BacnetErrorCodes.ERROR_CODE_OTHER;
                            if (error == ErrorCodes.WriteAccessDenied)
                                bacEr = BacnetErrorCodes.ERROR_CODE_WRITE_ACCESS_DENIED;
                            if (error == ErrorCodes.OutOfRange)
                                bacEr = BacnetErrorCodes.ERROR_CODE_VALUE_OUT_OF_RANGE;

                            await Task.Run(() => sender.ErrorResponse(adr, BacnetConfirmedServices.SERVICE_CONFIRMED_WRITE_PROPERTY, invoke_id, BacnetErrorClasses.ERROR_CLASS_DEVICE, bacEr));
                        }
                    }, TaskCreationOptions.None);
                }
            }
            else
            {
                sender.ErrorResponse(adr, BacnetConfirmedServices.SERVICE_CONFIRMED_READ_PROPERTY, invoke_id, BacnetErrorClasses.ERROR_CLASS_DEVICE, BacnetErrorCodes.ERROR_CODE_UNKNOWN_OBJECT);
            }
        }

        // todo: future implementation
        private void handler_OnAtomicReadFileRequest(BacnetClient sender, BacnetAddress adr, byte invoke_id, bool is_stream, BacnetObjectId object_id, int position, uint count, BacnetMaxSegments max_segments)
        {
            throw new NotImplementedException();

            //lock (_device)
            //{
            //    BaCSharpObject File = _device.FindBacnetObject(object_id);
            //    if (File is BacnetFile)
            //    {
            //        try
            //        {
            //            BacnetFile f = (BacnetFile)File;

            //            int filesize = (int)f.PROP_FILE_SIZE;
            //            bool end_of_file = (position + count) >= filesize;
            //            count = (uint)Math.Min(count, filesize - position);
            //            int max_filebuffer_size = sender.GetFileBufferMaxSize();
            //            count = (uint)Math.Min(count, max_filebuffer_size);     //trim

            //            byte[] file_buffer = f.ReadFileBlock(position, (int)count);
            //            sender.ReadFileResponse(adr, invoke_id, sender.GetSegmentBuffer(max_segments), position, count, end_of_file, file_buffer);
            //        }
            //        catch (Exception)
            //        {
            //            sender.ErrorResponse(adr, BacnetConfirmedServices.SERVICE_CONFIRMED_ATOMIC_READ_FILE, invoke_id, BacnetErrorClasses.ERROR_CLASS_DEVICE, BacnetErrorCodes.ERROR_CODE_OTHER);
            //        }
            //    }
            //    else
            //        sender.ErrorResponse(adr, BacnetConfirmedServices.SERVICE_CONFIRMED_ATOMIC_READ_FILE, invoke_id, BacnetErrorClasses.ERROR_CLASS_DEVICE, BacnetErrorCodes.ERROR_CODE_OTHER);
            //}
        }
        //todo: here something could be done to avoid a to big fill to be written on the disk
        private void handler_OnAtomicWriteFileRequest(BacnetClient sender, BacnetAddress adr, byte invoke_id, bool is_stream, BacnetObjectId object_id, int position, uint block_count, byte[][] blocks, int[] counts, BacnetMaxSegments max_segments)
        {
            throw new NotImplementedException();

            //lock (_device)
            //{
            //    BaCSharpObject File = _device.FindBacnetObject(object_id);
            //    if (File is BacnetFile)
            //    {
            //        try
            //        {
            //            BacnetFile f = (BacnetFile)File;

            //            if (f.PROP_READ_ONLY == false)
            //            {
            //                int currentposition = position;
            //                for (int i = 0; i < block_count; i++)
            //                {
            //                    f.WriteFileBlock(blocks[i], currentposition, counts[i]);
            //                    currentposition += counts[i];
            //                }

            //                sender.WriteFileResponse(adr, invoke_id, sender.GetSegmentBuffer(max_segments), position);
            //            }
            //            else
            //                sender.ErrorResponse(adr, BacnetConfirmedServices.SERVICE_CONFIRMED_ATOMIC_WRITE_FILE, invoke_id, BacnetErrorClasses.ERROR_CLASS_DEVICE, BacnetErrorCodes.ERROR_CODE_WRITE_ACCESS_DENIED);
            //        }
            //        catch (Exception)
            //        {
            //            sender.ErrorResponse(adr, BacnetConfirmedServices.SERVICE_CONFIRMED_ATOMIC_WRITE_FILE, invoke_id, BacnetErrorClasses.ERROR_CLASS_DEVICE, BacnetErrorCodes.ERROR_CODE_OTHER);
            //        }
            //    }
            //    else
            //        sender.ErrorResponse(adr, BacnetConfirmedServices.SERVICE_CONFIRMED_ATOMIC_WRITE_FILE, invoke_id, BacnetErrorClasses.ERROR_CLASS_DEVICE, BacnetErrorCodes.ERROR_CODE_OTHER);
            //}
        }
        private void handler_OnCreateObjectRequest(BacnetClient sender, BacnetAddress adr, byte invoke_id, BacnetObjectId object_id, ICollection<BacnetPropertyValue> values, BacnetMaxSegments max_segments)
        {
            throw new NotImplementedException();

            //// simple not all errortypes!!!!!!!! and for now only Analog inputs
            //if (_device.FindBacnetObject(object_id) != null)
            //{
            //    sender.ErrorResponse(adr, BacnetConfirmedServices.SERVICE_CONFIRMED_CREATE_OBJECT, invoke_id, BacnetErrorClasses.ERROR_CLASS_OBJECT, BacnetErrorCodes.ERROR_CODE_OBJECT_IDENTIFIER_ALREADY_EXISTS);
            //    return;
            //}

            //// some default values
            //string obj_name = object_id.Type.ToString() + object_id.Instance.ToString();
            //string obj_description = "Sample for you by C. Günter";
            //BacnetUnitsId obj_unit = BacnetUnitsId.UNITS_NO_UNITS;
            //double obj_value = 0;

            //// normally only needs objid, these properties values are sent or not by the client
            //foreach (BacnetPropertyValue value in values)
            //{
            //    switch (value.property.propertyIdentifier)
            //    {
            //        case (uint)BacnetPropertyIds.PROP_DESCRIPTION:
            //            obj_description = (string)value.value[0].Value;
            //            break;
            //        case (uint)BacnetPropertyIds.PROP_OBJECT_NAME:
            //            obj_name = (string)value.value[0].Value;
            //            break;
            //        case (uint)BacnetPropertyIds.PROP_UNITS:
            //            obj_unit = (BacnetUnitsId)value.value[0].Value;
            //            break;
            //        case (uint)BacnetPropertyIds.PROP_PRESENT_VALUE:
            //            try
            //            {
            //                obj_value = Convert.ToDouble(value.value[0].Value); // double is the simplest, quite all values convertible to it
            //            }
            //            catch { }
            //            break;
            //    }
            //}
            ////add to device
            //switch (object_id.Type)
            //{
            //    case BacnetObjectTypes.OBJECT_ANALOG_INPUT:
            //        AnalogInput<double> newAI = new AnalogInput<double>(object_id, obj_name, obj_description, obj_value, obj_unit);
            //        _device.AddBacnetObject(newAI);
            //        break;
            //    /* to be added by yourself according to your project requirement
            //    */
            //    default:
            //        sender.ErrorResponse(adr, BacnetConfirmedServices.SERVICE_CONFIRMED_CREATE_OBJECT, invoke_id, BacnetErrorClasses.ERROR_CLASS_OBJECT, BacnetErrorCodes.ERROR_CODE_UNSUPPORTED_OBJECT_TYPE);
            //        return;
            //}
            ////send ack that has been created
            //sender.CreateObjectResponse(adr, invoke_id, sender.GetSegmentBuffer(max_segments), object_id);
        }
        private void handler_OnDeleteObjectRequest(BacnetClient sender, BacnetAddress adr, byte invoke_id, BacnetObjectId object_id, BacnetMaxSegments max_segments)
        {
            throw new NotImplementedException();

            ////check if exists; if doesn't send error Unknown_Object
            //if (_device.FindBacnetObject(object_id) == null)
            //{
            //    sender.ErrorResponse(adr, BacnetConfirmedServices.SERVICE_CONFIRMED_CREATE_OBJECT, invoke_id, BacnetErrorClasses.ERROR_CLASS_OBJECT, BacnetErrorCodes.ERROR_CODE_UNKNOWN_OBJECT);
            //    return;
            //}

            //// check if objecttype is allowed to be deleted, like for example Device switch() for adding more types which cant be deleted
            //// Device not removable, no need to check
            //switch (object_id.Type)
            //{
            //    case BacnetObjectTypes.OBJECT_ACCESS_DOOR: // just to shows how to do
            //        sender.ErrorResponse(adr, BacnetConfirmedServices.SERVICE_CONFIRMED_CREATE_OBJECT, invoke_id, BacnetErrorClasses.ERROR_CLASS_OBJECT, BacnetErrorCodes.ERROR_CODE_OBJECT_DELETION_NOT_PERMITTED);
            //        return;
            //    default:
            //        break;
            //}
            ////remove from device and send ACK normally there should be no error!!!!!!!
            //if (_device.RemoveBacnetObject(object_id) == true)
            //    sender.SimpleAckResponse(adr, BacnetConfirmedServices.SERVICE_CONFIRMED_DELETE_OBJECT, invoke_id);
            //else
            //    _logger.LogDebug($"Unknown error while deleting object: {object_id.ToString()}");
            //return;
        }
    }
}
