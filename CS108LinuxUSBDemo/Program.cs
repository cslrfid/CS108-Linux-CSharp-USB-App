using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;

namespace CS108LinuxUSBDemo
{
    class MainClass
    {
        public static void Main(string[] args)
        {
            string deviceString = "";
            int deviceNumber = 0;

            IntPtr m_hid = IntPtr.Zero;
            RFIDReader reader;

            try
            {
                bool found = false;
                // Iterate through each HID device with matching VID/PID
                for (int i = 0; i < HID.GetNumHidDevices(); i++)
                {
                    // Add path strings to the combobox
                    if (HID.GetHidString(i, ref deviceString) == HID.HID_DEVICE_SUCCESS)
                    {
                        Console.WriteLine("CS108 Found.  Device ID = " + deviceString);
                        deviceNumber = i;
                        found = true;
                        
                    }
                    if (found) break;       //get the first device being detected and break
                }

                if (!found)
                {
                    Console.WriteLine("No CS108 device detected over USB.  Exit program.");
                    return;
                }

                int status = HID.Open(ref m_hid, deviceNumber);

                // Attempt to open the device
                if (status == HID.HID_DEVICE_SUCCESS)
                {
                    reader = new RFIDReader(m_hid);
                }
                else
                {
                    Console.WriteLine("Error connecting to CS108 through HID.  Exit program.");
                    return;
                }

                ConsoleKeyInfo KeyPress;
                Console.WriteLine("Press any key to start inventory and press ESC to stop...");
                Console.ReadKey();

                byte[] command = RFIDCommands.PowerOn(true);
                if (!USBSocket.TransmitData(m_hid, command, command.Length))
                {
                    Console.WriteLine("Device failed to power on");
                    return;
                }

                //enable battery reporting notifications
                command = NotifyCommands.SetBatteryReport(true);
                if (!USBSocket.TransmitData(m_hid, command, command.Length))
                {
                    Console.WriteLine("Device failed to enable battery reporting.");
                    return;
                }

                Thread.Sleep(100);

                if (!reader.setPowerAndChannel())
                {
                    Console.WriteLine("Error settign power and frequency channel.  Exit program.");
                    return;
                }

                if (!reader.setInventoryConfig())
                {
                    Console.WriteLine("Error setting inventory configurations.  Exit program.");
                    return;
                }

                Thread.Sleep(500);

                reader.StartInventory();

                while (true)
                {
                    KeyPress = Console.ReadKey();
                    if (KeyPress.Key == ConsoleKey.Escape)
                        break;

                }

                //stop inventory
                reader.StopInventory();
                while (reader.isReading);

                //power off RFID module
                command = RFIDCommands.PowerOn(false);
                if (!USBSocket.TransmitData(m_hid, command, command.Length))
                {
                    Console.WriteLine("Device failed to power off");
                }

                Thread.Sleep(2000);
                //close USB connection
                HID.Close(m_hid);

                //close reader
                reader.close();

                Console.WriteLine("All operations completed.  Press any key to exit program...");
                Console.ReadKey();


            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
                return;
            }

        }

        

    }
}
