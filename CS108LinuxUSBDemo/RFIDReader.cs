using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;


namespace CS108LinuxUSBDemo
{

    public class RFIDReader
    {
        static IntPtr m_hid = IntPtr.Zero;

        byte[] m_read_buffer = new byte[200000]; // read ring buffer
        ushort m_read_buffer_size = 0;
        ushort m_read_buffer_head = 0;
        ushort m_read_buffer_tail = 0;

        byte[] m_rfid_buffer = new byte[200000]; // rfid ring buffer
        ushort m_rfid_buffer_size = 0;
        ushort m_rfid_buffer_head = 0;
        ushort m_rfid_buffer_tail = 0;

        bool m_stopRfidDecode = false;
        bool m_stopReceive = false;
        bool m_startInventory = false;

        Thread thread1;
        Thread thread2;

        public bool isReading
        {
            get { return m_startInventory; }
        }

        int m_totaltag = 0;
        readonly object locker = new object();

        //reader settings - parameters to be exposed
        readonly Int32 power = 300;
        readonly Int32 profile = 1;
        readonly int region = 0; //0 - using reader default, other - refer to frequency table
        readonly int channel = 0; //0 - hopping, 1-50 - fixed frequency
        readonly uint session = 0;     //0-3
        readonly uint target = 0;      //A=0, B=1 
        readonly uint toggle = 0;      //0=toogle, 1=no toggle
        readonly uint algorithm = 3;   //0=fixedq, 3=dynamic1
        readonly uint startq = 4;
        readonly uint minq = 0;
        readonly uint maxq = 15;
        readonly uint tmult = 4;
        readonly uint retry = 0;
        readonly uint compact = 1;     //0=normal, 1=conpact
        readonly uint brandid = 0;     //for ucode8 only
        readonly uint delay = 0;
        readonly uint cycle_delay = 0;
        readonly int tx_on_time = 400;

        public RFIDReader(IntPtr _m_hid)
        {
            m_hid = _m_hid;

            //Start two background threads for receiving and decoding data from the reader
            thread1 = new Thread(new ThreadStart(RecvThread));
            thread1.Start();
            m_stopRfidDecode = false;
            thread2 = new Thread(new ThreadStart(DecodeRfidCommands));
            thread2.Start();
            Thread.Sleep(500);

        }

        public void getRFIDFirmwareVersion()
        {
            RFIDCommands.GetVersion(m_hid);
            Thread.Sleep(1);
        }

        private void RecvThread()
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
                                Console.WriteLine("[RFIDReader] Wrong prefix is received");
                                ClearReadBuffer();
                            }
                        }
                    }
                }
            }
        }

        private bool DecodeCommands()
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
                                Console.WriteLine("[RFIDReader] Power on successed.");
                                //EnableCtrls(true);
                                break;
                            case 0xFF:
                                Console.WriteLine("[RFIDReader] Power on failed with unknown reason.");
                                break;
                            default:
                                Console.WriteLine("[RFIDReader] Unknown status for rfid.");
                                break;
                        }
                        break;
                    case 0x8001:
                        status = m_read_buffer[index++];
                        switch (status)
                        {
                            case 0x00:
                                Console.WriteLine("[RFIDReader] Power off successed.");
                                //EnableCtrls(false);
                                ClearRfidBuffer();
                                Console.WriteLine("[RFIDReader] Power off successed");
                                break;
                            case 0xFF:
                                Console.WriteLine("[RFIDReader] Power off failed with unknown reason.");
                                break;
                            default:
                                Console.WriteLine("[RFIDReader] Unknown status for rfid.");
                                break;
                        }
                        break;
                    case 0x8002:
                        status = m_read_buffer[index++];
                        switch (status)
                        {
                            case 0x00:
                                //Console.WriteLine("[RFIDReader] Send data successed.");
                                break;
                            case 0xFF:
                                Console.WriteLine("[RFIDReader] Send data failed with unknown reason.");
                                break;
                            default:
                                Console.WriteLine("[RFIDReader] Unknown status for rfid.");
                                break;
                        }
                        break;
                    case 0x8100:
                        ushort len = (ushort)(payload_len - 2);
                        index = m_read_buffer_tail;
                        index += 10;
                        lock (locker)
                        {
                            for (int i = 0; i < len; i++)
                            {
                                m_rfid_buffer[m_rfid_buffer_head++] = m_read_buffer[index++];
                            }
                            m_rfid_buffer_size += len;
                        }
                        break;
                    default:
                        Console.WriteLine("[RFIDReader] Unknown event for rfid.");
                        break;
                }
            }
            else if (source == Constants.TYPE_NOTIFY)
            {
                switch (event_code)
                {
                    case 0xA000:
                        Console.WriteLine("[RFIDReader] Battery level: " + (m_read_buffer[index++] << 8 | m_read_buffer[index++]).ToString());
                        break;
                    case 0xA002:
                        Console.WriteLine("[RFIDReader] Start battery report.");
                        break;
                    case 0xA003:
                        Console.WriteLine("[RFIDReader] Stop battery report.");
                        break;
                    case 0xA100:
                        // battery fail
                        Console.WriteLine("[RFIDReader] Battery fail.");
                        break;
                    case 0xA101:
                        // error code
                        uint err_code = ((uint)m_read_buffer[index++] << 8) | (uint)m_read_buffer[index++];
                        Console.WriteLine("[RFIDReader] Error code : " + err_code.ToString());
                        break;
                    default:
                        Console.WriteLine("[RFIDReader] Unknown event for notification.");
                        break;
                }
            }
            else
            {
                Console.WriteLine("[RFIDReader] Wrong source for rfid.");
            }

            m_read_buffer_size -= (byte)total_len;
            m_read_buffer_tail += (byte)total_len;
            //Console.WriteLine("Read Size:" + m_read_buffer_size);

            return true;
        }

        private void DecodeRfidCommands()
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
                lock (locker)
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
                                    Console.WriteLine("[RFIDReader] Reset command response received.");
                                    break;
                                case 0x03:
                                    Console.WriteLine("[RFIDReader] Abort command response received.");
                                    m_startInventory = false;
                                    break;
                                default:
                                    Console.WriteLine("[RFIDReader] other control command response received.");
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
                                    Console.WriteLine("[RFIDReader] RFID Firmware Version: " + major.ToString() + "." + minor.ToString() + "." + build.ToString());
                                    break;
                                default:
                                    Console.WriteLine("[RFIDReader] MAC Address: " + address.ToString("X4") + "; Data: " + data.ToString("X8"));
                                    break;
                            }
                            continue;
                        }
                        else if (pkt_ver != 0x01 && pkt_ver != 0x02 && pkt_ver != 0x03)
                        {
                            Console.WriteLine("[RFIDReader] Unrecognized packet header: " + pkt_ver.ToString("X2"));
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
                        Console.WriteLine("[RFIDReader] Incomplete packet returned.");
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

                lock (locker)
                {
                    //finish reading
                    index = m_rfid_buffer_tail;
                    m_rfid_buffer_size -= (ushort)datalen;
                    m_rfid_buffer_tail += (ushort)datalen;
                    //Console.WriteLine("+Rfid Size:" + m_rfid_buffer_size);
                    if (pkt_type == 0x8000 || pkt_type == 0x0000)
                    {
                        Console.WriteLine("[RFIDReader] Command Begin Packet.");
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

                        Console.WriteLine(String.Format("[RFIDReader] Command End Packet. Status: {0:X4}", status));
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
                                Console.WriteLine("[RFIDReader] PC=" + ByteArrayToHexString(PC, 2) + " EPC=" + ByteArrayToHexString(EPC, epclen) + " RSSI=" + rssi.ToString("0.00"));
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
                            Console.WriteLine("[RFIDReader] PC=" + ByteArrayToHexString(PC, 2) + " EPC=" + ByteArrayToHexString(EPC, epclen) + " RSSI=" + rssi.ToString("0.00"));
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

                        Console.WriteLine(String.Format("[RFIDReader] Inventory Cycle End Diag Packet. EPC_RX: {0:X4}", EPC_RX));
                        continue;
                    }
                }
                Thread.Sleep(1); // save CPU usage
            }
            stopwatch1.Stop();
            stopwatch2.Stop();
        }

        public bool setPowerAndChannel()
        {

            try
            {
                //reader configurations
                // Set power
                RFIDCommands.SendData(m_hid, HexStringToByteArray(String.Format("70010607{0:X2}{1:X2}0000", power & 0xFF, (power >> 8) & 0xFF)), 8);
                Thread.Sleep(1);


                // Set channel region
                SetRegion(region);


                // Set channel
                if (channel > 0)
                {
                    //disable all channels
                    for (int i = 0; i <= 49; i++)
                    {
                        RFIDCommands.SendData(m_hid, HexStringToByteArray(String.Format("7001010C{0:X2}000000", i)), 8);
                        Thread.Sleep(1);
                        RFIDCommands.SendData(m_hid, HexStringToByteArray("7001020C00000000"), 8);
                        Thread.Sleep(1);
                    }
                    //enable channel
                    RFIDCommands.SendData(m_hid, HexStringToByteArray(String.Format("7001010C{0:X2}000000", channel - 1)), 8);
                    Thread.Sleep(1);
                    RFIDCommands.SendData(m_hid, HexStringToByteArray("7001020C01000000"), 8);
                    Thread.Sleep(1);
                }

                //Set Link Profile
                RFIDCommands.SendData(m_hid, HexStringToByteArray(String.Format("7001600b{0:X2}000000", profile)), 8);
                Thread.Sleep(1);
                //HST Command
                RFIDCommands.SendData(m_hid, HexStringToByteArray("700100f019000000"), 8);
                Thread.Sleep(10);

                return true;

            }
            catch (Exception ex)
            {
                Console.WriteLine("[RFIDReader] SetReaderPowerAndChannel Error - {0}", ex.Message);
                return false;
            }

        }

        private void SetRegion(int region)
        {
            int i;
            uint[] FreqTable = null;

            if (region == 0) return;

            //enable channel
            /*RFIDCommands.SendData(m_hid, HexStringToByteArray(String.Format("7001010C{0:X2}000000", channel - 1)), 8);
            Thread.Sleep(1);
            RFIDCommands.SendData(m_hid, HexStringToByteArray("7001020C01000000"), 8);
            Thread.Sleep(1);*/

            //disable all channels
            for (i = 0; i <= 49; i++)
            {
                RFIDCommands.SendData(m_hid, HexStringToByteArray(String.Format("7001010C{0:X2}000000", i)), 8);
                Thread.Sleep(1);
                RFIDCommands.SendData(m_hid, HexStringToByteArray("7001020C00000000"), 8);
                Thread.Sleep(1);
            }

            switch (region)
            {
                case 0:
                    break;
                case 1:
                    FreqTable = RFIDFreqTable.hkFreqTable;
                    break;
                case 2:
                    FreqTable = RFIDFreqTable.zaFreqTable;
                    break;
                case 3:
                    FreqTable = RFIDFreqTable.thFreqTable;
                    break;
                case 4:
                    FreqTable = RFIDFreqTable.LH1FreqTable;
                    break;
                case 5:
                    FreqTable = RFIDFreqTable.LH2FreqTable;
                    break;
                case 6:
                    FreqTable = RFIDFreqTable.veFreqTable;
                    break;
                case 7:
                    FreqTable = RFIDFreqTable.fccFreqTable;
                    break;
                case 8:
                    FreqTable = RFIDFreqTable.indonesiaFreqTable;
                    break;
                case 9:
                    FreqTable = RFIDFreqTable.UH2FreqTable;
                    break;
                case 10:
                    FreqTable = RFIDFreqTable.fccFreqTable;
                    break;
                case 11:
                    FreqTable = RFIDFreqTable.fccFreqTable;
                    break;
                case 12:
                    FreqTable = RFIDFreqTable.UH1FreqTable;
                    break;
                case 13:
                    FreqTable = RFIDFreqTable.fccFreqTable;
                    break;
                case 14:
                    FreqTable = RFIDFreqTable.mysFreqTable;
                    break;
                case 15:
                    FreqTable = RFIDFreqTable.sgFreqTable;
                    break;
                case 16:
                    FreqTable = RFIDFreqTable.AusFreqTable;
                    break;
                case 17:
                    FreqTable = RFIDFreqTable.br1FreqTable;
                    break;
                case 18:
                    FreqTable = RFIDFreqTable.br2FreqTable;
                    break;
                case 19:
                    FreqTable = RFIDFreqTable.br3FreqTable;
                    break;
                case 20:
                    FreqTable = RFIDFreqTable.br4FreqTable;
                    break;
                case 21:
                    FreqTable = RFIDFreqTable.br5FreqTable;
                    break;
                case 22:
                    FreqTable = RFIDFreqTable.phiFreqTable;
                    break;
                case 23:
                    FreqTable = RFIDFreqTable.fccFreqTable;
                    break;
                case 24:
                    FreqTable = RFIDFreqTable.fccFreqTable;
                    break;
                case 25:
                    FreqTable = RFIDFreqTable.fccFreqTable;
                    break;
                case 26:
                    FreqTable = RFIDFreqTable.isFreqTable;
                    break;
                case 27:
                    FreqTable = RFIDFreqTable.fccFreqTable;
                    break;
                case 28:
                    FreqTable = RFIDFreqTable.fccFreqTable;
                    break;
                case 29:
                    FreqTable = RFIDFreqTable.etsiFreqTable;
                    break;
                case 30:
                    FreqTable = RFIDFreqTable.indiaFreqTable;
                    break;
                default:
                    FreqTable = RFIDFreqTable.fccFreqTable;
                    break;
            }

            for (i = 0; i < FreqTable.Length; i++)
            {
                RFIDCommands.SendData(m_hid, HexStringToByteArray(String.Format("7001010C{0:X2}000000", i)), 8);
                Thread.Sleep(1);

                //Console.WriteLine(String.Format("freqTable {0}, {1:X8}", i, FreqTable[i]));
                uint t_freqVal = swapMSBLSB32bit(FreqTable[i]);
                //Console.WriteLine(String.Format("t_freqVal {0:X8}", t_freqVal));

                RFIDCommands.SendData(m_hid, HexStringToByteArray(String.Format("7001030C{0:X8}", t_freqVal)), 8);
                Thread.Sleep(1);

                RFIDCommands.SendData(m_hid, HexStringToByteArray("7001020C01000000"), 8);
                Thread.Sleep(1);
            }
        }

        private uint swapMSBLSB32bit(uint in_32bit)
        {
            int[] t_shift = new int[] { 0, 8, 16, 24 };
            uint[] t_tmpVal = new uint[4];
            uint out_32bit;
            int j;

            out_32bit = 0;
            for (j = 0; j < 4; j++)
            {
                t_tmpVal[j] = (in_32bit >> t_shift[j]) & 0xff;
                out_32bit |= t_tmpVal[j] << t_shift[3 - j];
            }

            return out_32bit;
        }

        public bool setInventoryConfig()
        {
            try
            {
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

                return true;


            }
            catch (Exception ex)
            {
                Console.WriteLine("[RFIDReader] setInventoryConfig Error - {0}", ex.Message);

                return false;
            }
        }

        public void setTxOnTime()
        {
            //Set TX on time
            RFIDCommands.SendData(m_hid, HexStringToByteArray(String.Format("70010603{0:X2}{1:X2}0000", tx_on_time & 0xFF, (tx_on_time >> 8) & 0xFF)), 8);
            Thread.Sleep(1);
        }


        public void StartInventory()
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

        public void StopInventory()
        {
            //Send Abort command
            RFIDCommands.SendData(m_hid, HexStringToByteArray("4003000000000000"), 8);

            m_startInventory = false;
            //timer_reset.Stop();
        }

        public void close()
        {
            //close
            //thread2.Abort();
            //thread1.Abort();
            m_stopRfidDecode = true;
            m_stopReceive = true;


        }

        private byte[] HexStringToByteArray(String HexString)
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

        private string ByteArrayToHexString(byte[] bytes, int length)
        {
            return BitConverter.ToString(bytes, 0, length).Replace("-", string.Empty);
        }

        private float ConvertNBRSSI(int rssi)
        {
            float Mantissa = rssi & 0x07;
            int Exponent = (rssi >> 3) & 0x1F;

            return (float)(20 * Math.Log10((1 << Exponent) * (1 + Mantissa / 8)));
        }

        private ushort GetRfidBufferSize()
        {
            lock (locker)
            {
                return m_rfid_buffer_size;
            }
        }

        private void ClearReadBuffer()
        {
            m_read_buffer_size = 0;
            m_read_buffer_head = 0;
            m_read_buffer_tail = 0;
        }

        private void ClearRfidBuffer()
        {
            m_rfid_buffer_size = 0;
            m_rfid_buffer_head = 0;
            m_rfid_buffer_tail = 0;
        }

        public byte[] StringToByteArray(string hex)
        {
            return Enumerable.Range(0, hex.Length)
                             .Where(x => x % 2 == 0)
                             .Select(x => Convert.ToByte(hex.Substring(x, 2), 16))
                             .ToArray();
        }

    }

    
}
