using System;
using System.Collections.Generic;
using System.IO.BACnet;
using System.IO.BACnet.Storage.Objective;
using System.Text;

namespace Bacnet.Server.Interface
{
    public interface IWriteNotificationManager
    {
        void NotifyWrite(BaCSharpObject sender, BacnetPropertyIds propId);
    }
}
