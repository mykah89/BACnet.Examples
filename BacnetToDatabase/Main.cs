using BacnetToDatabase.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.IO;
using System.IO.BACnet;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace BacnetToDatabase
{
    public partial class Main : Form
    {
        private BacnetClient _bacnet_client;
        private DbContextOptionsBuilder _contextOptionsBuilder = new DbContextOptionsBuilder<B2DBDBContext>();
        private string _dbName = "SampleDatabase.db";

        public Main()
        {
            InitializeComponent();

            // Bacnet on UDP/IP/Ethernet
            _bacnet_client = new BacnetClient(new BacnetIpUdpProtocolTransport(0xBAC0, loggerFactory: NullLoggerFactory.Instance, useExclusivePort: false), NullLoggerFactory.Instance);
            _bacnet_client.OnIam += new BacnetClient.IamHandler(bacnet_client_OnIam);
            _bacnet_client.Start();

            _contextOptionsBuilder.UseSqlite($"Data Source={Path.Combine(Environment.CurrentDirectory, _dbName)}");
        }

        private void bacnet_client_OnIam(BacnetClient sender, BacnetAddress adr, uint device_id, uint max_apdu, BacnetSegmentations segmentation, ushort vendor_id)
        {
            this.Invoke((MethodInvoker)delegate
            {
                lock (m_list)
                {
                    string adrKey = adr.ToString();
                    
                    if (m_list.FindItemWithText(adrKey) == null)
                    {
                        ListViewItem itm = m_list.Items.Add(adrKey);
                        itm.Tag = new KeyValuePair<BacnetAddress, uint>(adr, device_id);
                        itm.SubItems.Add("");

                        //read name
                        IList<BacnetValue> values = _bacnet_client.ReadPropertyRequest(adr, new BacnetObjectId(BacnetObjectTypes.OBJECT_DEVICE, device_id), BacnetPropertyIds.PROP_OBJECT_NAME);
                        if (values.Count > 0)
                            itm.SubItems[1].Text = (string)values[0].Value;
                    }
                }
            }, null);
        }

        private void m_delayedStart_Tick(object sender, EventArgs e)
        {
            m_delayedStart.Enabled = false;
            SendSearch();
        }

        private void SendSearch()
        {
            _bacnet_client.WhoIs();
        }

        private void m_SearchButton_Click(object sender, EventArgs e)
        {
            SendSearch();
        }

        private void m_TransferButton_Click(object sender, EventArgs e)
        {
            using (B2DBDBContext context = new B2DBDBContext(_contextOptionsBuilder.Options))
            {
                if (context.Database.EnsureCreated())
                {
                    context.Database.ExecuteSqlRaw("CREATE TABLE SampleTable(ObjectName NVARCHAR(255), PropertyId NVARCHAR(255),Value NVARCHAR(255));");
                }
            }

            //get Bacnet selection
            if (m_list.SelectedItems.Count <= 0)
            {
                MessageBox.Show(this, "Please select a device", "No device selected", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            KeyValuePair<BacnetAddress, uint> device = (KeyValuePair<BacnetAddress, uint>)m_list.SelectedItems[0].Tag;

            //retrieve list of 'properties'
            IList<BacnetValue> value_list = _bacnet_client.ReadPropertyRequest(device.Key, new BacnetObjectId(BacnetObjectTypes.OBJECT_DEVICE, device.Value), BacnetPropertyIds.PROP_OBJECT_LIST);
            LinkedList<BacnetObjectId> object_list = new LinkedList<BacnetObjectId>();
            foreach (BacnetValue value in value_list)
            {
                if (Enum.IsDefined(typeof(BacnetObjectTypes), ((BacnetObjectId)value.Value).Type))
                    object_list.AddLast((BacnetObjectId)value.Value);
            }

            //go through all 'properties' and store their 'present data' into a SQL database
            foreach (BacnetObjectId object_id in object_list)
            {
                //read all properties
                IList<BacnetValue> values = null;
                try
                {
                    values = _bacnet_client.ReadPropertyRequest(device.Key, object_id, BacnetPropertyIds.PROP_PRESENT_VALUE);
                    if (values.Count == 0)
                    {
                        MessageBox.Show(this, "Couldn't fetch 'present value' for object: " + object_id.ToString());
                        continue;
                    }
                }
                catch (Exception ex)
                {
                    //perhaps the 'present value' is non existing - ignore
                    continue;
                }

                using (B2DBDBContext context = new B2DBDBContext(_contextOptionsBuilder.Options))
                {
                    string sqlCommand = "INSERT INTO SampleTable VALUES(@ObjectName,@PropertyId,@Value)";

                    IEnumerable<Microsoft.Data.Sqlite.SqliteParameter> parameters = new Microsoft.Data.Sqlite.SqliteParameter[] {
                        new Microsoft.Data.Sqlite.SqliteParameter("@ObjectName",   object_id.ToString()),
                        new Microsoft.Data.Sqlite.SqliteParameter("@PropertyId",   values[0].Tag.ToString()),
                        new Microsoft.Data.Sqlite.SqliteParameter("@Value",   values[0].Value.ToString()),
                    };

                    context.Database.ExecuteSqlRaw(sqlCommand, parameters);
                }
            }

            //done
            MessageBox.Show(this, "Done!", "Done", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }
}
