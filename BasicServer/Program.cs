/**************************************************************************
*                           MIT License
* 
* Copyright (C) 2015 Morten Kvistgaard <mk@pch-engineering.dk>
*                    Frederic Chaxel <fchaxel@free.fr> 
*
* Permission is hereby granted, free of charge, to any person obtaining
* a copy of this software and associated documentation files (the
* "Software"), to deal in the Software without restriction, including
* without limitation the rights to use, copy, modify, merge, publish,
* distribute, sublicense, and/or sell copies of the Software, and to
* permit persons to whom the Software is furnished to do so, subject to
* the following conditions:
*
* The above copyright notice and this permission notice shall be included
* in all copies or substantial portions of the Software.
*
* THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
* EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
* MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT.
* IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY
* CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT,
* TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE
* SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
*
*********************************************************************/
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO.BACnet;
using System.Threading;
using System.IO.BACnet.Storage;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
<<<<<<< HEAD
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
=======
>>>>>>> tmp

namespace BasicServer
{
    //
    // The quite minimal server based on Yabe code
    // OBJECT_ANALOG_INPUT_0.PRESENT_VALUE = OBJECT_ANALOG_VALUE_0.PRESENT_VALUE * Sin (w.t);
    //
    public class Program
    {
        public static void Main(string[] args)
        {
<<<<<<< HEAD
            CreateHostBuilder(args).Build().Run();
        }
        public static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
            .UseSystemd()
            .UseWindowsService()
            .ConfigureServices((hostContext, services) =>
=======
            try
            {
                StartActivity();
                Console.WriteLine("Started");

                BacnetObjectId OBJECT_ANALOG_VALUE_0 = new BacnetObjectId(BacnetObjectTypes.OBJECT_ANALOG_VALUE, 0);
                BacnetObjectId OBJECT_ANALOG_INPUT_0 = new BacnetObjectId(BacnetObjectTypes.OBJECT_ANALOG_INPUT, 0);

                double count = 0;

                for (; ; )
                {
                    lock (m_storage)         // read and write callback are fired in a separated thread, so multiple access needs protection
                    {
                        // Read the Present Value
                        IList<BacnetValue> valtoread;
                        // index 0 : number of values in the array
                        // index 1 : first value
                        m_storage.ReadProperty(OBJECT_ANALOG_VALUE_0, BacnetPropertyIds.PROP_PRESENT_VALUE, 1, out valtoread);
                        // Get the first ... and here the only element
                        double coef = Convert.ToDouble(valtoread[0].Value);

                        float sin = (float)(coef * Math.Sin(count));
                        // Write the Present Value
                        IList<BacnetValue> valtowrite = new BacnetValue[1] { new BacnetValue(sin) };
                        m_storage.WriteProperty(OBJECT_ANALOG_INPUT_0, BacnetPropertyIds.PROP_PRESENT_VALUE, 1, valtowrite, true);
                    }
                    Thread.Sleep(1000);
                    count += 0.1;
                }
            }
            catch { }
        }

        /*****************************************************************************************************/
        static void StartActivity()
        {
            // Load the device descriptor from the embedded resource file
            // Get myId as own device id
            m_storage = DeviceStorage.Load("BasicServer.DeviceDescriptor.xml");

            using (var loggerFactory = LoggerFactory.Create(b =>
            {
                b.AddConsole(c =>
                {
                    c.TimestampFormat = "[yyyy-MM-dd HH:mm:ss] ";
                });
            }))
            {
                // Bacnet on UDP/IP/Ethernet
                bacnet_client = new BacnetClient(new BacnetIpUdpProtocolTransport(port: 0xBAC0, loggerFactory: loggerFactory, useExclusivePort: false), loggerFactory: loggerFactory);
            }


            // or Bacnet Mstp on COM4 à 38400 bps, own master id 8
            // m_bacnet_client = new BacnetClient(new BacnetMstpProtocolTransport("COM4", 38400, 8);
            // Or Bacnet Ethernet
            // bacnet_client = new BacnetClient(new BacnetEthernetProtocolTransport("Connexion au réseau local"));    
            // Or Bacnet on IPV6
            // bacnet_client = new BacnetClient(new BacnetIpV6UdpProtocolTransport(0xBAC0));

            bacnet_client.OnWhoIs += new BacnetClient.WhoIsHandler(handler_OnWhoIs);
            bacnet_client.OnIam += new BacnetClient.IamHandler(bacnet_client_OnIam);
            bacnet_client.OnReadPropertyRequest += new BacnetClient.ReadPropertyRequestHandler(handler_OnReadPropertyRequest);
            bacnet_client.OnReadPropertyMultipleRequest += new BacnetClient.ReadPropertyMultipleRequestHandler(handler_OnReadPropertyMultipleRequest);
            bacnet_client.OnWritePropertyRequest += new BacnetClient.WritePropertyRequestHandler(handler_OnWritePropertyRequest);

            bacnet_client.Start();    // go
            // Send Iam
            bacnet_client.Iam(m_storage.DeviceId, new BacnetSegmentations());

        }

        static void bacnet_client_OnIam(BacnetClient sender, BacnetAddress adr, uint device_id, uint max_apdu, BacnetSegmentations segmentation, ushort vendor_id)
        {
            //ignore Iams from other devices. (Also loopbacks)
        }

        /*****************************************************************************************************/
        static void handler_OnWritePropertyRequest(BacnetClient sender, BacnetAddress adr, byte invoke_id, BacnetObjectId object_id, BacnetPropertyValue value, BacnetMaxSegments max_segments)
        {
            // only OBJECT_ANALOG_VALUE:0.PROP_PRESENT_VALUE could be write in this sample code
            if ((object_id.Type != BacnetObjectTypes.OBJECT_ANALOG_VALUE) || (object_id.Instance != 0) || ((BacnetPropertyIds)value.property.propertyIdentifier != BacnetPropertyIds.PROP_PRESENT_VALUE))
>>>>>>> tmp
            {
                services.AddHostedService<Worker>();
            })
            .ConfigureLogging((hostingContext, logging) =>
            {
                logging.AddConfiguration(hostingContext.Configuration.GetSection("Logging"));
                logging.AddConsole(c =>
                {
                    c.TimestampFormat = "[yyyy-MM-dd HH:mm:ss] ";
                });
                logging.AddDebug();
            });
    }
}
