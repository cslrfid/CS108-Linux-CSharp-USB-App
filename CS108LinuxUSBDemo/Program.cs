using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;

namespace CS108LinuxUSBDemo
{
    class MainClass
    {
        static IntPtr m_hid = IntPtr.Zero;

        static byte[] m_read_buffer = new byte[65536]; // read ring buffer
        static ushort m_read_buffer_size = 0;
        static ushort m_read_buffer_head = 0;
        static ushort m_read_buffer_tail = 0;

        static byte[] m_rfid_buffer = new byte[65536]; // rfid ring buffer
        static ushort m_rfid_buffer_size = 0;
        static ushort m_rfid_buffer_head = 0;
        static ushort m_rfid_buffer_tail = 0;

        static bool m_stopRfidDecode = false;
        static bool m_stopReceive = false;
        static bool m_startInventory = false;

        static int m_totaltag = 0;
        static readonly object locker = new object();

        //reader settings
        static Int32 power = 300;
        static Int32 profile = 1;
        static uint session = 0;     //0-3
        static uint target = 0;      //A=0, B=1 
        static uint toggle = 0;      //0=toogle, 1=no toggle
        static uint algorithm = 3;   //0=fixedq, 3=dynamic1
        static uint startq = 4;
        static uint minq = 0;
        static uint maxq = 15;
        static uint tmult = 4;
        static uint retry = 0;
        static uint compact = 0;     //0=normal, 1=conpact
        static uint brandid = 0;     //for ucode8 only
        static uint delay = 0;
        static uint cycle_delay = 0;
        static int tx_on_time = 400;

        public static void Main(string[] args)
        {
            string deviceString = "";
            int deviceNumber = 0;

            try
            {
                bool found = false;
                // Iterate through each HID device with matching VID/PID
                for (int i = 0; i < HID.GetNumHidDevices(); i++)
                {
                    // Add path strings to the combobox
                    if (HID.GetHidString(i, ref deviceString) == HID.HID_DEVICE_SUCCESS)
                    {
                        Console.WriteLine(deviceString);
                        deviceNumber = i;
                        found = true;
                        
                    }
                    if (found) break;
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
                    GetSilabVersion();
                    GetBTVersion();
                }
                else
                {
                    Console.WriteLine("Error connecting to CS108 through HID.  Exit program.");
                    return;
                }

                //Start two background threads for receiving and decoding data from the reader
                Thread thread1 = new Thread(new ThreadStart(RecvThread));
                thread1.Start();
                m_stopRfidDecode = false;
                Thread thread2 = new Thread(new ThreadStart(DecodeRfidCommands));
                thread2.Start();

                //reader configurations
                // Set power
                RFIDCommands.SendData(m_hid, HexStringToByteArray(String.Format("70010607{0:X2}{1:X2}0000", power & 0xFF, (power >> 8) & 0xFF)), 8);
                Thread.Sleep(1);
                //Set Link Profile
                RFIDCommands.SendData(m_hid, HexStringToByteArray(String.Format("7001600b{0:X2}000000", profile)), 8);
                Thread.Sleep(1);
                //HST Command
                RFIDCommands.SendData(m_hid, HexStringToByteArray("700100f019000000"), 8);
                Thread.Sleep(10);

                //inventory algorithm
                uint buf;

                //Send EPC Gen2 target and session
                buf = (target << 4 & 0x10) | (session << 5 & 0x60);
                if (brandid == 1)
                {
                    buf |= (3 << 7);
                }
                RFIDCommands.SendData(m_hid, HexStringToByteArray(String.Format("70010009{0:X2}{1:X2}{2:X2}{3:X2}", (uint)(buf & 0xff), (uint)(buf >> 8 & 0xff), (uint)(buf >> 16 & 0xff), (uint)(buf >> 24 & 0xff))), 8);
                Thread.Sleep(1);

                //Inventory algorithm
                buf = (algorithm & 0x1F) | ((delay & 0x3F) << 20) | (compact << 26) | (brandid << 27);
                if (brandid == 1)
                {
                    buf |= (1 << 14);
                }
                RFIDCommands.SendData(m_hid, HexStringToByteArray(String.Format("70010109{0:X2}{1:X2}{2:X2}{3:X2}", (uint)(buf & 0xff), (uint)(buf >> 8 & 0xff), (uint)(buf >> 16 & 0xff), (uint)(buf >> 24 & 0xff))), 8);
                Thread.Sleep(1);

                //Ucode8 brand indentifier
                if (brandid == 1)
                {
                    //Ucode8 brand indentifier
                    RFIDCommands.SendData(m_hid, HexStringToByteArray("7001000800000000"), 8);
                    Thread.Sleep(10);
                    RFIDCommands.SendData(m_hid, HexStringToByteArray("7001010809000000"), 8);
                    Thread.Sleep(10);
                    RFIDCommands.SendData(m_hid, HexStringToByteArray("7001020801000000"), 8);
                    Thread.Sleep(10);
                    RFIDCommands.SendData(m_hid, HexStringToByteArray("7001030804020000"), 8);
                    Thread.Sleep(10);
                    RFIDCommands.SendData(m_hid, HexStringToByteArray("7001040801000000"), 8);
                    Thread.Sleep(10);
                    RFIDCommands.SendData(m_hid, HexStringToByteArray("7001050880000000"), 8);
                    Thread.Sleep(10);
                }

                RFIDCommands.SendData(m_hid, HexStringToByteArray(String.Format("70010209{0:X2}000000", algorithm)), 8);
                Thread.Sleep(1);

                buf = (startq & 0xF) | (maxq << 4 & 0xF0) | (minq << 8 & 0xF00) | (tmult << 12 & 0xF000);
                RFIDCommands.SendData(m_hid, HexStringToByteArray(String.Format("70010309{0:X2}{1:X2}{2:X2}{3:X2}", (uint)(buf & 0xff), (uint)(buf >> 8 & 0xff), (uint)(buf >> 16 & 0xff), (uint)(buf >> 24 & 0xff))), 8);
                Thread.Sleep(1);

                RFIDCommands.SendData(m_hid, HexStringToByteArray(String.Format("70010409{0:X2}000000", retry)), 8);
                Thread.Sleep(1);

                RFIDCommands.SendData(m_hid, HexStringToByteArray(String.Format("70010509{0:X2}000000", toggle)), 8);
                Thread.Sleep(1);

                buf = cycle_delay;
                RFIDCommands.SendData(m_hid, HexStringToByteArray(String.Format("70010F0F{0:X2}{1:X2}{2:X2}{3:X2}", (uint)(buf & 0xff), (uint)(buf >> 8 & 0xff), (uint)(buf >> 16 & 0xff), (uint)(buf >> 24 & 0xff))), 8);
                Thread.Sleep(1);

                //Set TX on time
                RFIDCommands.SendData(m_hid, HexStringToByteArray(String.Format("70010603{0:X2}{1:X2}0000", tx_on_time & 0xFF, (tx_on_time >> 8) & 0xFF)), 8);
                Thread.Sleep(1);


                byte[] command = RFIDCommands.PowerOn(true);
                if (!USBSocket.TransmitData(m_hid, command, command.Length))
                {
                    Console.WriteLine("Device failed to transmit data.");
                }

                command = NotifyCommands.SetBatteryReport(true);

                if (!USBSocket.TransmitData(m_hid, command, command.Length))
                {
                    Console.WriteLine("Device failed to transmit data.");
                }

                Thread.Sleep(500);

                ConsoleKeyInfo KeyPress;
                Console.WriteLine("Press any key to start inventory and press ESC to stop...");
                Console.ReadKey();

                StartInventory();

                while (true)
                {
                    KeyPress = Console.ReadKey();
                    if (KeyPress.Key == ConsoleKey.Escape)
                        break;

                }

                //stop inventory
                StopInventory();
                while (m_startInventory);

                //power off RFID module
                command = RFIDCommands.PowerOn(false);

                if (!USBSocket.TransmitData(m_hid, command, command.Length))
                {
                    Console.WriteLine("Device failed to transmit data.");
                }

                Thread.Sleep(100);
                //close USB connection
                int r=HID.Close(m_hid);


                //end threads
                thread2.Abort();
                thread1.Abort();

                Console.WriteLine("All operations completed.  Press any key to exit program...");
                Console.ReadKey();


            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
                return;
            }

        }

        private static void GetSilabVersion()
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

        private static void GetBTVersion()
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

        private static void RecvThread()
        {
            while (!m_stopReceive)
            {
                // Make sure that we are connected to a device
                if (HID.IsOpened(m_hid))
                {
                    int bufferSize = HID.GetMaxReportRequest(m_hid) * HID.SIZE_MAX_READ;
                    byte[] buffer = new byte[bufferSize];
                    int bytesRead = 0;

                    if (USBSocket.ReceiveData(m_hid, ref buffer, buffer.Length, ref bytesRead, 1000))
                    {
                        if (bytesRead > 0)
                        {
                            //ResetTimer();
                            for (int i = 0; i < bytesRead; i++)
                            {
                                m_read_buffer[m_read_buffer_head++] = buffer[i];
                            }
                            m_read_buffer_size += (ushort)bytesRead;
                        }
                        while (m_read_buffer_size >= 8)
                        {
                            if (m_read_buffer[m_read_buffer_tail] == Constants.PREFIX)
                            {
                                if (!DecodeCommands())
                                {
                                    break;
                                }
                            }
                            else
                            {
                                Console.WriteLine("Wrong prefix is received");
                                ClearReadBuffer();
                            }
                        }
                    }
                }
            }
        }

        private static bool DecodeCommands()
        {
            ushort index = m_read_buffer_tail;
            uint prefix = m_read_buffer[index++];
            uint connection = m_read_buffer[index++];
            uint payload_len = m_read_buffer[index++];
            uint total_len = payload_len + 8;
            uint source = m_read_buffer[index++];
            uint reserve = m_read_buffer[index++];
            uint direction = m_read_buffer[index++];

            uint crc = ((uint)m_read_buffer[index++] << 8) | (uint)m_read_buffer[index++];

            if (m_read_buffer_size < total_len) return false; //not enough data

            if (crc != 0 && !CRC.CheckCRC(m_read_buffer, m_read_buffer_tail, total_len, crc))
                Console.WriteLine("Wrong CRC received.");

            uint event_code = ((uint)m_read_buffer[index++] << 8) | (uint)m_read_buffer[index++];
            if (source == Constants.TYPE_RFID)
            {
                switch (event_code)
                {
                    case 0x8000:
                        byte status = m_read_buffer[index++];
                        switch (status)
                        {
                            case 0x00:
                                Console.WriteLine("Power on successed.");
                                //EnableCtrls(true);
                                break;
                            case 0xFF:
                                Console.WriteLine("Power on failed with unknown reason.");
                                break;
                            default:
                                Console.WriteLine("Unknown status for rfid.");
                                break;
                        }
                        break;
                    case 0x8001:
                        status = m_read_buffer[index++];
                        switch (status)
                        {
                            case 0x00:
                                Console.WriteLine("Power off successed.");
                                //EnableCtrls(false);
                                ClearRfidBuffer();
                                Console.WriteLine("Power off successed");
                                break;
                            case 0xFF:
                                Console.WriteLine("Power off failed with unknown reason.");
                                break;
                            default:
                                Console.WriteLine("Unknown status for rfid.");
                                break;
                        }
                        break;
                    case 0x8002:
                        status = m_read_buffer[index++];
                        switch (status)
                        {
                            case 0x00:
                                Console.WriteLine("Send data successed.");
                                break;
                            case 0xFF:
                                Console.WriteLine("Send data failed with unknown reason.");
                                break;
                            default:
                                Console.WriteLine("Unknown status for rfid.");
                                break;
                        }
                        break;
                    case 0x8100:
                        ushort len = (ushort)(payload_len - 2);
                        index = m_read_buffer_tail;
                        index += 10;
                        //lock (locker)
                        {
                            for (int i = 0; i < len; i++)
                            {
                                m_rfid_buffer[m_rfid_buffer_head++] = m_read_buffer[index++];
                            }
                            m_rfid_buffer_size += len;
                        }
                        break;
                    default:
                        Console.WriteLine("Unknown event for rfid.");
                        break;
                }
            }
            else if (source == Constants.TYPE_NOTIFY)
            {
                switch (event_code)
                {
                    case 0xA000:
                        Console.WriteLine("Battery level: " + (m_read_buffer[index++] << 8 | m_read_buffer[index++]).ToString());
                        break;
                    case 0xA002:
                        Console.WriteLine("Start battery report.");
                        break;
                    case 0xA003:
                        Console.WriteLine("Stop battery report.");
                        break;
                    case 0xA100:
                        // battery fail
                        Console.WriteLine("Battery fail.");
                        break;
                    case 0xA101:
                        // error code
                        uint err_code = ((uint)m_read_buffer[index++] << 8) | (uint)m_read_buffer[index++];
                        Console.WriteLine("Error code : " + err_code.ToString());
                        break;
                    default:
                        Console.WriteLine("Unknown event for notification.");
                        break;
                }
            }
            else
            {
                Console.WriteLine("Wrong source for rfid.");
            }

            m_read_buffer_size -= (byte)total_len;
            m_read_buffer_tail += (byte)total_len;
            //Console.WriteLine("Read Size:" + m_read_buffer_size);

            return true;
        }

        private static void DecodeRfidCommands()
        {
            ushort index = 0;
            int pkt_ver = 0;
            int flags = 0;
            int pkt_type = 0;
            int pkt_len = 0;
            int datalen = 0;
            int reserve = 0;
            Stopwatch stopwatch1 = new Stopwatch();
            Stopwatch stopwatch2 = new Stopwatch();

            stopwatch1.Start();
            stopwatch2.Start();
            while (!m_stopRfidDecode)
            {
                //lock (locker)
                {
                    if (m_rfid_buffer_size >= 8)
                    {
                        stopwatch2.Reset();
                        stopwatch2.Start();
                        //get packet header
                        index = m_rfid_buffer_tail;
                        pkt_ver = m_rfid_buffer[index++];
                        flags = m_rfid_buffer[index++];
                        pkt_type = (int)(m_rfid_buffer[index++]) + ((int)(m_rfid_buffer[index++]) << 8);
                        pkt_len = (int)(m_rfid_buffer[index++]) + ((int)(m_rfid_buffer[index++]) << 8);
                        reserve = (int)(m_rfid_buffer[index++]) + ((int)(m_rfid_buffer[index++]) << 8);
                        datalen = pkt_len * 4;
                        m_rfid_buffer_size -= 8;
                        m_rfid_buffer_tail += 8;
                        if (pkt_ver == 0x04)
                        {
                            datalen = pkt_len;
                        }
                        else if (pkt_ver == 0x40)
                        {
                            switch (flags)
                            {
                                case 0x02:
                                    Console.WriteLine("Reset command response received.");
                                    break;
                                case 0x03:
                                    Console.WriteLine("Abort command response received.");
                                    m_startInventory = false;
                                    break;
                                default:
                                    Console.WriteLine("other control command response received.");
                                    break;
                            }
                            continue;
                        }
                        else if ((pkt_ver == 0x70 || pkt_ver == 0x00) && flags == 0x00)
                        {
                            uint address = (uint)(pkt_type);
                            uint data = (uint)((reserve << 16) + pkt_len);
                            switch (address)
                            {
                                case 0x0000:
                                    uint major = (data >> 24);
                                    uint minor = (data >> 12) & 0x7FF;
                                    uint build = data & 0x7FF;
                                    Console.WriteLine("Version: " + major.ToString() + "." + minor.ToString() + "." + build.ToString());
                                    break;
                                default:
                                    Console.WriteLine("MAC Address: " + address.ToString("X4") + "; Data: " + data.ToString("X8"));
                                    break;
                            }
                            continue;
                        }
                        else if (pkt_ver != 0x01 && pkt_ver != 0x02 && pkt_ver != 0x03)
                        {
                            Console.WriteLine("Unrecognized packet header: " + pkt_ver.ToString("X2"));
                            ClearReadBuffer();
                            ClearRfidBuffer();
                            /*if (m_startInventory)
                                StopInventory();*/
                            continue;
                        }
                    }
                    else
                    {
                        /*if (stopwatch2.ElapsedMilliseconds >= 4000) {
                            UpdateInfo("No data received within 4 seconds.");
                            stopwatch2.Reset();
                            stopwatch2.Start();
                        }*/
                        Thread.Sleep(1); // save CPU usage
                        continue;
                    }
                }

                //wait until the full packet data has come in
                bool inCompletePacket = false;
                stopwatch1.Reset();
                stopwatch1.Start();
                while (GetRfidBufferSize() < datalen)
                {
                    if (stopwatch1.ElapsedMilliseconds >= 3000)
                    {
                        Console.WriteLine("Incomplete packet returned.");
                        inCompletePacket = true;
                        break;
                    }
                    Thread.Sleep(1);
                }
                if (inCompletePacket)
                {
                    ClearRfidBuffer();
                    StopInventory();
                    continue;
                }

                //lock (locker)
                {
                    //finish reading
                    index = m_rfid_buffer_tail;
                    m_rfid_buffer_size -= (ushort)datalen;
                    m_rfid_buffer_tail += (ushort)datalen;
                    //Console.WriteLine("+Rfid Size:" + m_rfid_buffer_size);
                    if (pkt_type == 0x8000 || pkt_type == 0x0000)
                    {
                        Console.WriteLine("Command Begin Packet.");
                        continue;
                    }
                    if (pkt_type == 0x8001 || pkt_type == 0x0001)
                    {
                        byte[] data = new byte[datalen];
                        byte[] PC = new byte[2];
                        byte[] EPC = new byte[128];
                        for (int cnt = 0; cnt < datalen; cnt++)
                        {
                            data[cnt] = m_rfid_buffer[index++];
                        }
                        uint status = (uint)(data[4]) + ((uint)(data[5]) << 8);

                        Console.WriteLine(String.Format("Command End Packet. Status: {0:X4}", status));
                        continue;
                    }
                    if (pkt_type == 0x8007 || pkt_type == 0x0007)
                    {
                        Console.WriteLine("Antenna Cycle End Packet.");
                        continue;
                    }
                    if (pkt_type == 0x8005 || pkt_type == 0x0005)
                    {
                        byte[] data = new byte[datalen];
                        byte[] PC = new byte[2];
                        byte[] EPC = new byte[128];
                        float rssi;
                        float phase;
                        int epclen;
                        for (int cnt = 0; cnt < datalen; cnt++)
                        {
                            data[cnt] = m_rfid_buffer[index++];
                        }
                        if (pkt_ver == 0x04)
                        {
                            int cnt = 0;
                            while (cnt < datalen)
                            {
                                PC[0] = data[cnt++];
                                PC[1] = data[cnt++];
                                epclen = ((PC[0] >> 3) & 0x1f) * 2;
                                for (int i = 0; i < epclen; i++)
                                {
                                    EPC[i] = data[cnt++];
                                }
                                rssi = ConvertNBRSSI(data[cnt++]);
                                phase = 0;
                                Console.WriteLine("PC=" + ByteArrayToHexString(PC, 2) + " EPC=" + ByteArrayToHexString(EPC, epclen) + " RSSI=" + rssi.ToString("0.00"));
                                m_totaltag++;
                            }
                        }
                        else
                        {
                            PC[0] = data[12];
                            PC[1] = data[13];
                            epclen = ((PC[0] >> 3) & 0x1f) * 2;
                            for (int cnt = 0; cnt < epclen; cnt++)
                            {
                                EPC[cnt] = data[14 + cnt];
                            }
                            rssi = ConvertNBRSSI(data[5]);
                            phase = ((float)(data[6] & 0x3F)) * 360 / 128;
                            Console.WriteLine("PC=" + ByteArrayToHexString(PC, 2) +" EPC=" + ByteArrayToHexString(EPC, epclen) + " RSSI=" + rssi.ToString("0.00"));
                            m_totaltag++;
                        }
                        continue;
                    }
                    if (pkt_type == 0x1008)
                    {
                        byte[] data = new byte[datalen];
                        for (int cnt = 0; cnt < datalen; cnt++)
                        {
                            data[cnt] = m_rfid_buffer[index++];
                        }
                        uint Querys = (uint)(data[0]) + ((uint)(data[1]) << 8) + ((uint)(data[2]) << 16) + ((uint)(data[3]) << 24);
                        uint RN16_RX = (uint)(data[4]) + ((uint)(data[5]) << 8) + ((uint)(data[6]) << 16) + ((uint)(data[7]) << 24);
                        uint RN16_TO = (uint)(data[8]) + ((uint)(data[9]) << 8) + ((uint)(data[10]) << 16) + ((uint)(data[11]) << 24);
                        uint EPC_TO = (uint)(data[12]) + ((uint)(data[13]) << 8) + ((uint)(data[14]) << 16) + ((uint)(data[15]) << 24);
                        uint EPC_RX = (uint)(data[16]) + ((uint)(data[17]) << 8) + ((uint)(data[18]) << 16) + ((uint)(data[19]) << 24);
                        uint CRC = (uint)(data[20]) + ((uint)(data[21]) << 8) + ((uint)(data[22]) << 16) + ((uint)(data[23]) << 24);

                        Console.WriteLine(String.Format("Inventory Cycle End Diag Packet. EPC_RX: {0:X4}", EPC_RX));
                        continue;
                    }
                }
                Thread.Sleep(1); // save CPU usage
            }
            stopwatch1.Stop();
            stopwatch2.Stop();
        }

        private static void StartInventory()
        {
            //Send Abort command
            RFIDCommands.SendData(m_hid, HexStringToByteArray("4003000000000000"), 8);
            Thread.Sleep(1000);

            //QUERY_CFG Command for continuous inventory
            RFIDCommands.SendData(m_hid, HexStringToByteArray("70010007ffff0000"), 8);
            Thread.Sleep(10);

            RFIDCommands.SendData(m_hid, HexStringToByteArray("700100f00f000000"), 8);

            m_startInventory = true;
            m_totaltag = 0;
        }

        private static void StopInventory()
        {
            //Send Abort command
            RFIDCommands.SendData(m_hid, HexStringToByteArray("4003000000000000"), 8);

            m_startInventory = false;
            //timer_reset.Stop();
        }

        private static byte[] HexStringToByteArray(String HexString)
        {
            if (HexString.Length % 2 == 1)
            {
                throw new Exception("Lenght of hex string cannot be odd.");
            }
            int NumberChars = HexString.Length;
            byte[] bytes = new byte[NumberChars / 2];
            for (int i = 0; i < NumberChars; i += 2)
            {
                bytes[i / 2] = Convert.ToByte(HexString.Substring(i, 2), 16);
            }

            return bytes;
        }

        private static string ByteArrayToHexString(byte[] bytes, int length)
        {
            return BitConverter.ToString(bytes, 0, length).Replace("-", string.Empty);
        }

        private static float ConvertNBRSSI(int rssi)
        {
            float Mantissa = rssi & 0x07;
            int Exponent = (rssi >> 3) & 0x1F;

            return (float)(20 * Math.Log10((1 << Exponent) * (1 + Mantissa / 8)));
        }

        private static ushort GetRfidBufferSize()
        {
            //lock (locker)
            {
                return m_rfid_buffer_size;
            }
        }

        private static void ClearReadBuffer()
        {
            m_read_buffer_size = 0;
            m_read_buffer_head = 0;
            m_read_buffer_tail = 0;
        }

        private static void ClearRfidBuffer()
        {
            m_rfid_buffer_size = 0;
            m_rfid_buffer_head = 0;
            m_rfid_buffer_tail = 0;
        }

        public static byte[] StringToByteArray(string hex)
        {
            return Enumerable.Range(0, hex.Length)
                             .Where(x => x % 2 == 0)
                             .Select(x => Convert.ToByte(hex.Substring(x, 2), 16))
                             .ToArray();
        }

    }
}
