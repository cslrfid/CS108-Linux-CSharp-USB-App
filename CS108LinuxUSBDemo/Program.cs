using System;

namespace CS108LinuxUSBDemo
{
    class MainClass
    {
        public static void Main(string[] args)
        {
            string deviceString = "";

            try
            {
                // Iterate through each HID device with matching VID/PID
                for (int i = 0; i < HID.GetNumHidDevices(); i++)
                {
                    // Add path strings to the combobox
                    if (HID.GetHidString(i, ref deviceString) == HID.HID_DEVICE_SUCCESS)
                    {
                        Console.WriteLine(deviceString);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }

        }
    }
}
