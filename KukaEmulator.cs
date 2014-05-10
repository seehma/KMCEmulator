using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Net;
using System.Net.Sockets;

/* ============================================================================================================================== */
/**
 *  Namespace KukaMatlabConnector 
 *
 */
/* ============================================================================================================================== */
namespace KukaMatlabConnector
{
    /* -------------------------------------------------------------------------------------------------------------------------------------- */
    /**
     *  Class KukaEmulator
     * 
     *  Description: Emulates a Kuka Robot Controller to thes matlab scripts ...
     */
    /* -------------------------------------------------------------------------------------------------------------------------------------- */
    class KukaEmulator
    {
        static IPAddress wrapperIPAddress_;
        static uint robotCommunicationPort_ = 6008;
        static String pathToXMLSendDocument_ = "robotinfo.xml";

        static System.Diagnostics.Stopwatch stopWatch_;

        static byte[] comBuffer_ { get; set; }
        static System.Threading.Thread kukaRobotDummyThread_;  // creating thread instance

        static bool doTheRobotLoop_;                                // do the server loop until this variable is false
        static uint interpolationCounter_;

        static void Main(string[] args)
        {
            String protocolAnswer;
            bool protocolFound = false;

            // --------------------------------------------------------------------------------
            // initialize variables
            // --------------------------------------------------------------------------------
            doTheRobotLoop_ = true;
            interpolationCounter_ = 0;
            
            // initialize stopwatch for diagnostics
            stopWatch_ = new System.Diagnostics.Stopwatch();

            // starting kuka communication thread
            Console.WriteLine("starting KUKA Dummy Robot for testing purposes!");
            Console.ReadKey();

            while( protocolFound == false)
            {
                Console.Clear();
                Console.WriteLine("which protocol do you want me to emulate? (UDP, TCP)");
                protocolAnswer = Console.ReadLine();

                if( String.Equals(protocolAnswer, "UDP") )
                {
                    kukaRobotDummyThread_ = new System.Threading.Thread(new System.Threading.ThreadStart(kukaRobotDummyThreadUDP));
                    kukaRobotDummyThread_.Start();
                    protocolFound = true;
                }
                else if( String.Equals(protocolAnswer, "TCP") )
                {
                    kukaRobotDummyThread_ = new System.Threading.Thread(new System.Threading.ThreadStart(kukaRobotDummyThreadTCP));
                    kukaRobotDummyThread_.Start();
                    protocolFound = true;
                }
                else
                {
                    protocolFound = false;
                }
            }

            System.GC.Collect();
            System.Runtime.GCSettings.LatencyMode = System.Runtime.GCLatencyMode.LowLatency;
        }

        private static void kukaRobotDummyThreadUDP()
        {
            byte[] bytes;                           // data buffer for incoming data
            bool clientReturn;

            System.Xml.XmlDocument sendXML;
            System.Net.Sockets.Socket comHandler;   // create system socket
            System.Net.IPEndPoint extSysEndPoint;

            sendXML = new System.Xml.XmlDocument();   // load variable with xmlDocument instance
            bytes = new Byte[1024];                 // load byte buffer with instance

            while (doTheRobotLoop_)
            {
                wrapperIPAddress_ = getUserEnteredIPAddressToConnect();
                if (wrapperIPAddress_ != null)
                {
                    sendXML = getXMLSendDocument(pathToXMLSendDocument_);
                    if (sendXML != null)
                    {
                        while (true)
                        {
                            Console.WriteLine("press a key to connect to matlab server app...");
                            
                            Console.Read();

                            extSysEndPoint = new System.Net.IPEndPoint(wrapperIPAddress_, (int)robotCommunicationPort_);

                            comHandler = new System.Net.Sockets.Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);

                            clientReturn = emulatorLoopUDP(comHandler, sendXML, extSysEndPoint);

                            // when the loop is finished then close the socket
                            comHandler.Close();
                            Console.Clear();
                            Console.WriteLine("connection closed by matlab server app...");
                            Console.Read();
                            break;
                        }
                    }
                    else
                    {
                        Console.WriteLine("could not find xml document to send to the robot! check the path!");
                        Console.Read();
                    }
                }
                else
                {
                    doTheRobotLoop_ = false;
                }
            }
        }

        /* -------------------------------------------------------------------------------------------------------------------------------------- */
        /**
         *  @brief   starts the thread which handles the communication
         * 
         * 
         * 
         */
        /* -------------------------------------------------------------------------------------------------------------------------------------- */
        private static void kukaRobotDummyThreadTCP()
        {
            byte[] bytes;                           // data buffer for incoming data
            bool clientReturn;

            System.Xml.XmlDocument sendXML;
            System.Net.Sockets.Socket comHandler;   // create system socket

            sendXML = new System.Xml.XmlDocument();   // load variable with xmlDocument instance
            bytes = new Byte[1024];                 // load byte buffer with instance

            while (doTheRobotLoop_)
            {
                wrapperIPAddress_ = getUserEnteredIPAddressToConnect();
                if (wrapperIPAddress_ != null)
                {
                    sendXML = getXMLSendDocument(pathToXMLSendDocument_);
                    if (sendXML != null)
                    {
                        while (true)
                        {
                            Console.WriteLine("press a key to connect to matlab server app...");
                            Console.Read();

                            comHandler = startServerClientConnection(wrapperIPAddress_, robotCommunicationPort_);
                            if (comHandler != null)
                            {
                                clientReturn = emulatorLoopTCP(comHandler, sendXML);

                                // when the loop is finished then close the socket
                                comHandler.Close();

                                Console.Clear();
                                Console.WriteLine("connection closed by matlab server app...");
                                Console.Read();
                                break;
                            }
                            else
                            {
                                Console.WriteLine("could not establish the connection to the server app! check the network connection!");
                                Console.Read();
                                break;
                            }
                        }
                    }
                    else
                    {
                        Console.WriteLine("could not find xml document to send to the robot! check the path!");
                        Console.Read();
                    }
                }
                else
                {
                    doTheRobotLoop_ = false;
                }
            }
        }

        /* -------------------------------------------------------------------------------------------------------------------------------------- */
        /**
         *  @brief:   There is a specific counting value coming from the kuka controller inside the receive string.
         *            This value has to be sent back so separate it and insert it into the xml send string
         * 
         *  @param    receive ... the string which comes from the kuka controller
         *  @param    send ... the string which we will send to the controller
         *  
         *  @retval   string ... the modified string for the controller
         */
        /* -------------------------------------------------------------------------------------------------------------------------------------- */
        private static String mirrorInterpolationCounter(string sendString, uint ipocCounter)
        {
            int startIpocSendIndex = sendString.IndexOf("<IPOC>") + 6;
            int endIpocSendIndex = sendString.IndexOf("</IPOC>");

            sendString = sendString.Remove(startIpocSendIndex, endIpocSendIndex - startIpocSendIndex);
            sendString = sendString.Insert(startIpocSendIndex, ipocCounter.ToString());

            return sendString;
        }

        /* -------------------------------------------------------------------------------------------------------------------------------------- */
        /**  
         *  @brief    loads the xml document which has to be send to the C# emulator
         *  
         *  @retval   none
         */
        /* -------------------------------------------------------------------------------------------------------------------------------------- */
        private static System.Xml.XmlDocument getXMLSendDocument(String pathToXMLDocument)
        {
            System.Xml.XmlDocument localXMLDocument;

            localXMLDocument = null;

            // try to load the xml document
            try
            {
                // load variable with xmlDocument instance
                localXMLDocument = new System.Xml.XmlDocument();

                // prepare xml answer to the kuka robot
                localXMLDocument.PreserveWhitespace = true;
                localXMLDocument.Load(pathToXMLDocument);
            }
            catch
            {
                localXMLDocument = null;
            }

            return localXMLDocument;
        }

        /* -------------------------------------------------------------------------------------------------------------------------------------- */
        /**  
         *  @brief    tries to establish a connection to the remote host
         *  
         *  @retval   none
         */
        /* -------------------------------------------------------------------------------------------------------------------------------------- */
        private static System.Net.Sockets.Socket startServerClientConnection(System.Net.IPAddress ipAddress, uint communicationPort)
        {
            System.Net.Sockets.Socket localComHandler;
            System.Net.IPEndPoint localEndPoint;

            localComHandler = null;
            localEndPoint = null;

            // now lets start => get the localhost socket as endpoint for the connection
            localComHandler = new System.Net.Sockets.Socket(System.Net.Sockets.AddressFamily.InterNetwork, System.Net.Sockets.SocketType.Stream, System.Net.Sockets.ProtocolType.Tcp);
            localEndPoint = new System.Net.IPEndPoint(ipAddress, (int)communicationPort);

            try
            {
                Console.Write("trying to connect to matlab server app!\n\r");
                localComHandler.Connect(localEndPoint);
            }
            catch
            {
                Console.Write("unable to connect to remote endpoint!\n\r");
                Console.Read();
            }

            return localComHandler;
        }

        /* -------------------------------------------------------------------------------------------------------------------------------------- */
        /**  
         *  @brief    emulator thread which handles the connection to the matlab c# server app
         *  
         *  @retval   none
         */
        /* -------------------------------------------------------------------------------------------------------------------------------------- */
        private static bool emulatorLoopTCP(System.Net.Sockets.Socket comHandler, System.Xml.XmlDocument sendXML)
        {
            // variable declarations
            bool localReturn;
            int loopCount;

            long toobigcounter;
            byte[] incomingDataByteBuffer; 

            incomingDataByteBuffer = new Byte[1024];

            // variable initialization
            localReturn = false;
            loopCount = 0;
            toobigcounter = 0;

            // --------------------------------------------------
            // now lets start the endles loop
            // --------------------------------------------------            
            while (true)
            {
                byte[] message;

                try
                {
                    String strSend;
                    System.Text.StringBuilder strSendBuilder = new System.Text.StringBuilder();

                    // starting stopwatch
                    stopWatch_.Reset();
                    stopWatch_.Start();

                    // adding 0x0a two times to have the same string as the robot sends
                    strSend = sendXML.InnerXml;
                    strSend = mirrorInterpolationCounter(strSend, interpolationCounter_);
                    strSendBuilder.Append(strSend).Append((char)10).Append((char)10);
                    strSend = strSendBuilder.ToString();

                    // convert into byte array
                    message = System.Text.Encoding.ASCII.GetBytes(strSend);

                    // send the message to the external system
                    comHandler.Send(message, 0, message.Length, System.Net.Sockets.SocketFlags.None);

                    // increment interpolation counter
                    interpolationCounter_++;

                    // increment loopcounter
                    loopCount++;

                    // start receiving
                    comHandler.Receive(incomingDataByteBuffer);

                    // make garbage collector to collect now
                    System.GC.Collect();

                    // wait some time
                    System.Threading.Thread.Sleep(5);

                    Console.Clear();

                    stopWatch_.Stop();

                    if (stopWatch_.ElapsedMilliseconds > 13) toobigcounter++;
                    Console.Write("time:" + Convert.ToString(stopWatch_.ElapsedMilliseconds));
                    Console.Write(" toobigcounter:" + Convert.ToString(toobigcounter));
                    Console.Write(" loopcount:" + Convert.ToString(loopCount));
                }
                catch
                {
                    break;
                }
            }

            return localReturn;
        }

        private static bool emulatorLoopUDP(System.Net.Sockets.Socket comHandler, System.Xml.XmlDocument sendXML, System.Net.IPEndPoint endPoint)
        {
            // variable declarations
            bool localReturn;
            int loopCount;

            long toobigcounter;
            byte[] incomingDataByteBuffer;

            // create end point from which the packets are received
            System.Net.IPEndPoint testEndPoint = null;

            // create udp listener
            System.Net.IPAddress localIPAddress = System.Net.IPAddress.Parse("192.168.2.3");
            System.Net.IPEndPoint localEndPoint = new System.Net.IPEndPoint(localIPAddress, (int)robotCommunicationPort_);
            System.Net.Sockets.UdpClient listener = new System.Net.Sockets.UdpClient(localEndPoint);

            listener.Client.ReceiveTimeout = 1000;

            // variable initialization
            localReturn = false;
            loopCount = 0;
            toobigcounter = 0;

            // --------------------------------------------------
            // now lets start the endles loop
            // --------------------------------------------------            
            while (true)
            {
                byte[] message;

                try
                {
                    String strSend;
                    System.Text.StringBuilder strSendBuilder = new System.Text.StringBuilder();

                    incomingDataByteBuffer = null;

                    // starting stopwatch
                    stopWatch_.Reset();
                    stopWatch_.Start();

                    // adding 0x0a two times to have the same string as the robot sends
                    strSend = sendXML.InnerXml;
                    strSend = mirrorInterpolationCounter(strSend, interpolationCounter_);
                    strSendBuilder.Append(strSend).Append((char)10).Append((char)10);
                    strSend = strSendBuilder.ToString();

                    // convert into byte array
                    message = System.Text.Encoding.ASCII.GetBytes(strSend);

                    // send the message to the external system
                    comHandler.SendTo(message, endPoint);

                    // increment interpolation counter
                    interpolationCounter_++;

                    // increment loopcounter
                    loopCount++;

                    // start receiving
                    incomingDataByteBuffer = listener.Receive(ref testEndPoint);

                    // make garbage collector to collect now
                    System.GC.Collect();

                    // wait some time
                    System.Threading.Thread.Sleep(5);

                    Console.Clear();

                    stopWatch_.Stop();

                    if (stopWatch_.ElapsedMilliseconds > 13) toobigcounter++;
                    Console.Write("time:" + Convert.ToString(stopWatch_.ElapsedMilliseconds));
                    Console.Write(" toobigcounter:" + Convert.ToString(toobigcounter));
                    Console.Write(" loopcount:" + Convert.ToString(loopCount));
                }
                catch
                {
                    Console.Clear();
                    Console.Write("Timeout happened");
                    break;
                }
            }

            return localReturn;
        }

        /* -------------------------------------------------------------------------------------------------------------------------------------- */
        /**  
         *  @brief    waits till the user has entered a correct ip address to communicate with
         *  
         *  @retval   returns an IP-Address
         */
        /* -------------------------------------------------------------------------------------------------------------------------------------- */
        private static System.Net.IPAddress getUserEnteredIPAddressToConnect()
        {
            System.Net.IPAddress localIPAddress;
            String localIPAddressString;

            localIPAddress = null;

            Console.WriteLine("enter a correct IP-Address:");
            localIPAddressString = Console.ReadLine();
            if (localIPAddressString.Length != 0)
            {
                try
                {
                    localIPAddress = System.Net.IPAddress.Parse(localIPAddressString);
                }
                catch
                {
                    Console.WriteLine("invalid ip address entered!");
                }
            }
            return localIPAddress;
        }
    }
}
