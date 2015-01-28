using System;
using System.Collections.Generic;
using System.Text;

using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Diagnostics;
using System.IO;

namespace PMOS
{
    class Server
    {
        public void create()
        {
            AsynchronousSocketListener.StartListening();
        }

        // State object for reading client data asynchronously
        public class StateObject
        {
            // Client  socket.
            public Socket workSocket = null;
            // Size of receive buffer.
            public const int BufferSize = 1024;
            // Receive buffer.
            public byte[] buffer = new byte[BufferSize];
            // Received data string.
            public StringBuilder sb = new StringBuilder();
        }

        public class AsynchronousSocketListener
        {
            // Thread signal.
            public static ManualResetEvent allDone = new ManualResetEvent(false);

            public AsynchronousSocketListener()
            {
            }

            public static void StartListening()
            {
                // Data buffer for incoming data.
                byte[] bytes = new Byte[1024];

                // Establish the local endpoint for the socket.
                // The DNS name of the computer
                // running the listener is "host.contoso.com".
                IPHostEntry ipHostInfo = Dns.Resolve(Dns.GetHostName());
                IPAddress ipAddress = ipHostInfo.AddressList[0];
                IPEndPoint localEndPoint = new IPEndPoint(ipAddress, 11000);

                // Create a TCP/IP socket.
                Socket listener = new Socket(AddressFamily.InterNetwork,
                    SocketType.Stream, ProtocolType.Tcp);

                // Bind the socket to the local endpoint and listen for incoming connections.
                try
                {
                    listener.Bind(localEndPoint);
                    listener.Listen(100);

                    while (true)
                    {
                        // Set the event to nonsignaled state.
                        allDone.Reset();

                        // Start an asynchronous socket to listen for connections.
                        //Console.WriteLine("Waiting for a connection...");
                        listener.BeginAccept(
                            new AsyncCallback(AcceptCallback),
                            listener);

                        // Wait until a connection is made before continuing.
                        allDone.WaitOne();
                    }

                }
                catch (Exception e)
                {
                    //Console.WriteLine(e.ToString());
                }

                //Console.WriteLine("\nPress ENTER to continue...");
                //Console.Read();

            }

            public static void AcceptCallback(IAsyncResult ar)
            {
                // Signal the main thread to continue.
                allDone.Set();

                // Get the socket that handles the client request.
                Socket listener = (Socket)ar.AsyncState;
                Socket handler = listener.EndAccept(ar);

                // Create the state object.
                StateObject state = new StateObject();
                state.workSocket = handler;
                handler.BeginReceive(state.buffer, 0, StateObject.BufferSize, 0,
                    new AsyncCallback(ReadCallback), state);
            }

            public static void ReadCallback(IAsyncResult ar)
            {
                String content = String.Empty;

                // Retrieve the state object and the handler socket
                // from the asynchronous state object.
                StateObject state = (StateObject)ar.AsyncState;
                Socket handler = state.workSocket;

                // Read data from the client socket. 
                int bytesRead = handler.EndReceive(ar);

                if (bytesRead > 0)
                {
                    // There  might be more data, so store the data received so far.
                    state.sb.Append(Encoding.ASCII.GetString(
                        state.buffer, 0, bytesRead));

                    // Check for end-of-file tag. If it is not there, read 
                    // more data.
                    content = state.sb.ToString();
                    //if (content.IndexOf("<EOF>") > -1) {
                    // All the data has been read from the 
                    // client. Display it on the console.
                    //Console.WriteLine("Read {0} bytes from socket. \n Data : {1}",
                        //content.Length, content);
                    // Echo the data back to the client.
                    //Send(handler, content);
                    string phpFileName = @"C:\V0.17\example.html";
                    string cssFileName = @"C:\V0.17\example.css";
                    System.IO.FileInfo file = new System.IO.FileInfo(phpFileName);
                    System.IO.FileInfo css = new System.IO.FileInfo(cssFileName);

                    string webPageContents = executePhp(phpFileName);
                    long size = webPageContents.Length;
                    string webPagePreamble = string.Format("HTTP/1.0 200 OK \nContent-Type: text/html\nContent-Length:{0}\r\n\r\n", size);

                    Send(handler, webPagePreamble + webPageContents);

                    //} else {
                    // Not all data received. Get more.
                    //  handler.BeginReceive(state.buffer, 0, StateObject.BufferSize, 0,
                    //new AsyncCallback(ReadCallback), state);
                    //}
                }
            }

            private static string executePhp(string phpFileName)
            {
                //prepare input
                //string input = @"some stringy input";

                //NOTE: change path according to your own PHP.exe file, if you have the proper environment variables setup, then you can just call PHP.exe directly without the path
                string call = @"C:\php\php.exe";

                //the PHP wrapper class file location. NOTE: remember to enclose in " (quotes) if there is a space in the directory structure.
                string param1 = phpFileName;

                Process myProcess = new Process();

                // Start a new instance of this program but specify the 'spawned' version. using the PHP.exe file location as the first argument.
                ProcessStartInfo myProcessStartInfo = new ProcessStartInfo(call, "spawn");
                myProcessStartInfo.UseShellExecute = false;
                myProcessStartInfo.RedirectStandardOutput = true;

                //Provide the other arguments.
                myProcessStartInfo.Arguments = string.Format("{0}", param1);
                myProcess.StartInfo = myProcessStartInfo;

                //Execute the process
                myProcess.Start();
                StreamReader myStreamReader = myProcess.StandardOutput;

                // Read the standard output of the spawned process.
                string myString = myStreamReader.ReadToEnd();

                //Console.WriteLine(myString);
                return myString;
            }

            private static void Send(Socket handler, String data)
            {
                // Convert the string data to byte data using ASCII encoding.
                byte[] byteData = Encoding.ASCII.GetBytes(data);

                // Begin sending the data to the remote device.
                handler.BeginSend(byteData, 0, byteData.Length, 0,
                    new AsyncCallback(SendCallback), handler);
            }

            private static void SendCallback(IAsyncResult ar)
            {
                try
                {
                    // Retrieve the socket from the state object.
                    Socket handler = (Socket)ar.AsyncState;

                    // Complete sending the data to the remote device.
                    int bytesSent = handler.EndSend(ar);
                    //Console.WriteLine("Sent {0} bytes to client.", bytesSent);

                    handler.Shutdown(SocketShutdown.Both);
                    handler.Close();

                }
                catch (Exception e)
                {
                    Console.WriteLine(e.ToString());
                }
            }
        }
    }
}
