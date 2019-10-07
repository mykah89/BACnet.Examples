using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO.BACnet;

namespace ObjectBrowseSample
{
    internal class Program
    {
        private static void Main()
        {
            using (var loggerFactory = LoggerFactory.Create(b =>
            {
                b.AddConsole(c =>
                {
                    c.TimestampFormat = "[yyyy-MM-dd HH:mm:ss] ";
                });
            }))
            {
                var transport = new BacnetIpUdpProtocolTransport(port: 0xBAC0, loggerFactory: loggerFactory, useExclusivePort: true);
                var client = new BacnetClient(transport, loggerFactory: loggerFactory);

                client.OnIam += OnIAm;
                client.Start();
                client.WhoIs();
                Console.ReadLine();
            }
        }

        private static async void OnIAm(BacnetClient sender, BacnetAddress adr,
            uint deviceid, uint maxapdu, BacnetSegmentations segmentation, ushort vendorid)
        {
            Console.WriteLine($"Detected device {deviceid} at {adr}");

            // In theory each bacnet device should have object of type OBJECT_DEVICE with property PROP_OBJECT_LIST
            // This property is a list of all bacnet objects (ids) of that device

            var deviceObjId = new BacnetObjectId(BacnetObjectTypes.OBJECT_DEVICE, deviceid);
            IList<BacnetValue> objectIdList = await sender.ReadPropertyAsync(adr, deviceObjId, BacnetPropertyIds.PROP_OBJECT_LIST);

            foreach (var objId in objectIdList)
                Console.WriteLine($"{objId}");
        }
    }
}
