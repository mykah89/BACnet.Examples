using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Bacnet.Room.Simulator
{
    static class Program
    {
        public static int Count;

        /// <summary>
        ///  The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main()
        {
            // Le semaphore sert a donner un id unqiue au noeud Bacnet
            Semaphore s = new Semaphore(63, 63, "Bacnet.Room{FAED-FAED}");
            if (s.WaitOne() == true)
            {
                Count = 64 - s.Release();
                s.WaitOne();
            }

            try
            {
                Application.SetHighDpiMode(HighDpiMode.SystemAware);
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new Form1());
            }
            catch
            {
                MessageBox.Show("Fatal Error", "Bacnet.Room.Simulator", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }

            s.Release();
        }
    }
}
