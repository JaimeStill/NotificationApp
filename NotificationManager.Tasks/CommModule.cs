using DiagnosticsHelper;
using Newtonsoft.Json;
using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using Windows.ApplicationModel.Core;
using Windows.Foundation;
using Windows.Networking.Sockets;
using Windows.Storage.Streams;

namespace NotificationManager.Tasks
{
    public sealed class CommModule : IDisposable
    {
        const int MAX_BUFFER_LENGTH = 1024 * 4;

        StreamWebSocket socket;
        DataReader reader;
        public ControlChannelTrigger channel { get; set; }
        bool disconnected = false;
        public string socketUri { get; set; }

        InvocationManager actions = new InvocationManager();

        public CommModule() { }

        public void RegisterAction(string methodName, HandlerEvent action) => actions.On(methodName, action);

        public void Invoke(Message message) => actions.ReceiveMessage(message);

        public bool SetupTransport(string socketUri)
        {
            bool result = false;
            lock (this)
            {
                this.socketUri = socketUri;
                result = RegisterWithControlChannelTrigger(socketUri);

                if (result == false)
                {
                    Diag.DebugPrint("Failed to sign on and connect");

                    if (socket != null)
                    {
                        socket.Close(1001, "Failed to sign on and connect");
                        socket.Dispose();
                        socket = null;
                        reader = null;
                    }

                    if (channel != null)
                    {
                        channel.Dispose();
                        channel = null;
                    }
                }
            }

            return result;
        }

        bool RegisterWithControlChannelTrigger(string socketUri)
        {
            Task<bool> registerTask = RegisterWithCCTHelper(socketUri);
            return registerTask.Result;
        }

        async Task<bool> RegisterWithCCTHelper(string socketUri)
        {
            bool result = false;
            socket = new StreamWebSocket();

            channel = channel.RegisterChannel();

            if (channel == null)
                return result;

            var socketServer = socketUri.CreateSocketServerUri();

            if (socketServer == null)
                return result;

            channel.RegisterBackgroundTasks();

            try
            {
                await ConnectSocketChannel(socketUri: socketServer);
                UpdateCoreApplicationProperties();

                PostSocketRead(MAX_BUFFER_LENGTH);

                result = true;

                Diag.DebugPrint("RegisterWithCCTHelper Completed");
            }
            catch (Exception ex)
            {
                Diag.DebugPrint("RegisterWithCCTHelper Task failed with: " + ex.Message);
                return false;
            }

            return result;
        }

        private async Task ConnectSocketChannel(Uri socketUri)
        {
            Diag.DebugPrint("Calling UsingTransport() ...");
            channel.UsingTransport(socket);
            await socket.ConnectAsync(socketUri);
            Diag.DebugPrint("Connected");
            var status = channel.WaitForPushEnabled();
            Diag.DebugPrint("WaitForPushEnabled() completed with status: " + status);

            if (status != ControlChannelTriggerStatus.HardwareSlotAllocated && status != ControlChannelTriggerStatus.SoftwareSlotAllocated)
                throw new Exception($"Neither hardware nor software slot could be allowcated. ChannelStatus is {status.ToString()}");
        }

        private void UpdateCoreApplicationProperties()
        {
            CoreApplication.Properties.Remove(channel.ControlChannelTriggerId);
            var appContext = new AppContext(this, socket, channel, channel.ControlChannelTriggerId);
            CoreApplication.Properties.Add(channel.ControlChannelTriggerId, appContext);
        }

        public void OnDataReadCompletion(uint bytesRead, DataReader readPacket)
        {
            Diag.DebugPrint("OnDataReadCompletion Entry");
            if (readPacket == null)
            {
                Diag.DebugPrint("DataReader is null");
                return;
            }

            uint buffLen = readPacket.UnconsumedBufferLength;
            Diag.DebugPrint($"bytesRead: {bytesRead}, unconsumedBufferLength: {buffLen}");

            if (buffLen == 0)
            {
                Diag.DebugPrint("Received zero bytes from the socket. Server must have closed the connection.");
                Diag.DebugPrint("Try disconnecting and reconnecting to the server");
                return;
            }

            string serializedMessage = readPacket.ReadString(buffLen);
            var message = JsonConvert.DeserializeObject<Message>(serializedMessage);

            Diag.DebugPrint("Received Buffer: " + serializedMessage);

            actions.ReceiveMessage(message);

            AppContext.Enqueue(message);

            PostSocketRead(MAX_BUFFER_LENGTH);
            Diag.DebugPrint("OnDataReadCompletion Exit");
        }

        bool PostSocketRead(int length)
        {
            Diag.DebugPrint("Entering PostSocketRead");
            var result = true;

            try
            {
                var readBuf = new Windows.Storage.Streams.Buffer((uint)length);
                var readOp = socket.InputStream.ReadAsync(readBuf, (uint)length, InputStreamOptions.Partial);
                readOp.Completed = (IAsyncOperationWithProgress<IBuffer, uint> asyncAction, AsyncStatus asyncStatus) =>
                {
                    switch (asyncStatus)
                    {
                        case AsyncStatus.Completed:
                        case AsyncStatus.Error:
                            try
                            {
                                if (!disconnected)
                                {
                                    IBuffer localBuf = asyncAction.GetResults();
                                    uint bytesRead = localBuf.Length;
                                    reader = DataReader.FromBuffer(localBuf);
                                    OnDataReadCompletion(bytesRead, reader);
                                }
                            }
                            catch (Exception ex)
                            {
                                Diag.DebugPrint("Read operation failed: " + ex.Message);
                                result = false;
                            }
                            break;
                        case AsyncStatus.Canceled:
                            break;
                    }
                };
            }
            catch (Exception ex)
            {
                Diag.DebugPrint("Failed to post a read with error: " + ex.Message);
                return result = false;
            }

            Diag.DebugPrint("Leaving PostSocketRead");
            return result;
        }

        public void Dispose()
        {
            Reset();
            GC.SuppressFinalize(this);
        }

        public void Reset()
        {
            lock (this)
            {
                disconnected = true;
                actions = new InvocationManager();

                if (reader != null)
                {
                    try
                    {
                        reader.DetachStream();
                        reader = null;
                    }
                    catch (Exception ex)
                    {
                        Diag.DebugPrint("Could not detach DataReader: " + ex.Message);
                    }
                }

                if (socket != null)
                {
                    socket.Close(1000, "Socket Reset");
                    socket.Dispose();
                    socket = null;
                }

                if (channel != null)
                {
                    if (CoreApplication.Properties.ContainsKey(channel.ControlChannelTriggerId))
                        CoreApplication.Properties.Remove(channel.ControlChannelTriggerId);

                    channel.Dispose();
                    channel = null;
                }

                Diag.DebugPrint("CommModule has been reset.");
            }
        }
    }

    public sealed class AppContext
    {
        public StreamWebSocket WebSocketHandle { get; set; }
        public ControlChannelTrigger Channel { get; set; }
        public string ChannelId { get; set; }
        public CommModule CommInstance { get; set; }
        static ConcurrentQueue<Message> messageQueue;

        public AppContext(CommModule commInstance, StreamWebSocket webSocket, ControlChannelTrigger channel, string id)
        {
            WebSocketHandle = webSocket;
            Channel = channel;
            ChannelId = id;
            CommInstance = commInstance;
            messageQueue = new ConcurrentQueue<Message>();
        }

        public static void Enqueue(Message message)
        {
            messageQueue.Enqueue(message);
        }

        public static bool Dequeue(out Message message)
        {
            var result = messageQueue.TryDequeue(out message);
            return result;
        }
    }
}
