using System;
using System.Diagnostics;
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

                //reset libUSB device using the tool usb-reset (on Linux only)
                //https://github.com/ralight/usb-reset

                PlatformID pid = Environment.OSVersion.Platform;
                if (pid == PlatformID.Unix)
                { 
                    string ret = ExecuteBashCommand("sudo usb-reset 10c4:8468");
                    if (ret != String.Empty)
                    {
                        Console.WriteLine("Unable to reset CS108 USB device: " + ret);
                        return;
                    }
                }

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
                    GetSilabVersion(m_hid);
                    GetBTVersion(m_hid);
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

                //disable battery reporting notifications
                command = NotifyCommands.SetBatteryReport(false);
                if (!USBSocket.TransmitData(m_hid, command, command.Length))
                {
                    Console.WriteLine("Device failed to disable battery reporting.");
                    return;
                }

                //Stop RFID data processing
                reader.close();
                Thread.Sleep(1000);

                //close USB connection
                HID.Close(m_hid);

                Console.WriteLine("All operations completed.  Press any key to exit program...");
                Console.ReadKey();


            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
                return;
            }

        }

        private static void GetSilabVersion(IntPtr m_hid)
        {
            byte[] buffer = new byte[128];
            int bytesRead = 0;

            byte[] command = RFIDCommands.PowerOn(false);

            if (!USBSocket.TransmitData(m_hid, command, command.Length))
            {
                Console.WriteLine("Device failed to transmit data.");
                return;
            }

            if (HID.IsOpened(m_hid))
            {
                if (USBSocket.ReceiveData(m_hid, ref buffer, buffer.Length, ref bytesRead, 1000))
                {
                }
            }
            else
            {
                Console.WriteLine("Device is not connected.");
            }

            command = SiliconLabCommands.GetVersion();

            if (!USBSocket.TransmitData(m_hid, command, command.Length))
            {
                Console.WriteLine("Device failed to transmit data.");
                return;
            }

            // Make sure that we are connected to a device
            if (HID.IsOpened(m_hid))
            {
                if (USBSocket.ReceiveData(m_hid, ref buffer, buffer.Length, ref bytesRead, 1000))
                {
                    if ((buffer[0] == Constants.PREFIX) && (bytesRead >= 13) &&
                        (buffer[8] == 0xB0) && (buffer[9] == 0x00))
                    {
                        string slabVersion = buffer[10].ToString() + "." +
                                                buffer[11].ToString() + "." +
                                                buffer[12].ToString();
                        Console.WriteLine("Silicon lab firmware version: " + slabVersion);
                    }
                    else
                        Console.WriteLine("Cannot get silicon lab firmware version.");
                }
            }
            else
            {
                Console.WriteLine("Device is not connected.");
            }
        }

        private static void GetBTVersion(IntPtr m_hid)
        {
            byte[] command = BluetoothCommands.GetVersion();

            if (!USBSocket.TransmitData(m_hid, command, command.Length))
            {
                Console.WriteLine("Device failed to transmit data.");
                return;
            }

            byte[] buffer = new byte[128];
            int bytesRead = 0;

            // Make sure that we are connected to a device
            if (HID.IsOpened(m_hid))
            {
                if (USBSocket.ReceiveData(m_hid, ref buffer, buffer.Length, ref bytesRead, 2000))
                {
                    if ((buffer[0] == Constants.PREFIX) && (bytesRead >= 13) &&
                        (buffer[8] == 0xC0) && (buffer[9] == 0x00))
                    {
                        uint crc = ((uint)buffer[6] << 8) | (uint)buffer[7];

                        if (crc != 0 && !CRC.CheckCRC(buffer, 0, 13, crc))
                        {
                            Console.WriteLine("Wrong CRC received.");
                            return;
                        }

                        string btVersion = buffer[10].ToString() + "." +
                                                buffer[11].ToString() + "." +
                                                buffer[12].ToString();
                        Console.WriteLine("Bbluetooth firmware version: " + btVersion);
                    }
                    else
                        Console.WriteLine("Cannot get bluetooth firmware version.");
                }
            }
            else
            {
                Console.WriteLine("Device is not connected.");
            }
        }


        static string ExecuteBashCommand(string command)
        {
            // according to: https://stackoverflow.com/a/15262019/637142
            // thans to this we will pass everything as one command
            command = command.Replace("\"", "\"\"");

            var proc = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "/bin/bash",
                    Arguments = "-c \"" + command + "\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                }
            };

            proc.Start();
            proc.WaitForExit();

            return proc.StandardOutput.ReadToEnd();
        }



    }
}
