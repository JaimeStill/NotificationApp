using DiagnosticsHelper;
using Newtonsoft.Json;
using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using Windows.ApplicationModel.Background;
using Windows.ApplicationModel.Core;
using Windows.Data.Xml.Dom;
using Windows.Foundation;
using Windows.Networking.Sockets;
using Windows.Storage.Streams;
using Windows.UI.Notifications;

namespace NotificationManager.Tasks
{
    public sealed class CommModule : IDisposable
    {
        const int TIMEOUT = 30000;
        const int MAX_BUFFER_LENGTH = 1000;

        StreamWebSocket socket;
        public ControlChannelTrigger channel { get; set; }
        public string socketUri { get; set; }
        DataReader reader;
        DataWriter writer;
        public bool disconnected { get; set; }

        public CommModule()
        {
            disconnected = false;
        }

        public void Dispose()
        {
            Reset();
            GC.SuppressFinalize(this);
        }

        public void Reset()
        {
            lock(this)
            {
                disconnected = true;

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

                if (writer != null)
                {
                    try
                    {
                        writer.DetachStream();
                        writer = null;
                    }
                    catch (Exception ex)
                    {
                        Diag.DebugPrint("Could not detach DataWriter: " + ex.Message);
                    }
                }

                if (socket != null)
                {
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

        public bool SetupTransport(string serviceUri)
        {
            bool result = false;
            lock (this)
            {
                socketUri = serviceUri;

                result = RegisterWithControlChannelTrigger(socketUri);

                if (result == false)
                {
                    Diag.DebugPrint("Failed to sign on and connect");

                    if (socket != null)
                    {
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

        public bool RegisterWithControlChannelTrigger(string socketUri)
        {
            Task<bool> registerTask = RegisterWithCCTHelper(socketUri);
            return registerTask.Result;
        }

        async Task<bool> RegisterWithCCTHelper(string socketUri)
        {
            bool result = false;
            socket = new StreamWebSocket();

            const int serverKeepAliveInterval = 30;
            const string channelId = "notifications";
            const string WebSocketKeepAliveTask = "Windows.Networking.Sockets.WebSocketKeepAlive";

            Diag.DebugPrint("RegisterWithCCTHelper Starting...");
            ControlChannelTriggerStatus status;
            Diag.DebugPrint("Create ControlChannelTrigger ...");

            try
            {
                channel = new ControlChannelTrigger(channelId, serverKeepAliveInterval, ControlChannelTriggerResourceType.RequestHardwareSlot);
            }
            catch (UnauthorizedAccessException ex)
            {
                Diag.DebugPrint("Please add the app to the lock screen. " + ex.Message);
                return result;
            }

            Uri serverSocket;

            try
            {
                serverSocket = new Uri(socketUri);
            }
            catch (Exception ex)
            {
                Diag.DebugPrint("Error creating URI: " + ex.Message);
                return result;
            }

            var keepAliveBuilder = new BackgroundTaskBuilder();
            keepAliveBuilder.Name = "KeepaliveTaskForNotifications";
            keepAliveBuilder.TaskEntryPoint = WebSocketKeepAliveTask;
            keepAliveBuilder.SetTrigger(channel.KeepAliveTrigger);
            keepAliveBuilder.Register();

            var pushNotifyBuilder = new BackgroundTaskBuilder();
            pushNotifyBuilder.Name = "PushNotificationTask";
            pushNotifyBuilder.TaskEntryPoint = "NotificationManager.Tasks.PushNotifyTask";
            pushNotifyBuilder.SetTrigger(channel.PushNotificationTrigger);
            pushNotifyBuilder.Register();

            Diag.DebugPrint("Calling UsingTransport() ...");

            try
            {
                channel.UsingTransport(socket);

                await socket.ConnectAsync(serverSocket);

                Diag.DebugPrint("Connected");

                status = channel.WaitForPushEnabled();

                Diag.DebugPrint("WaitForPushEnabled() completed with status: " + status);

                if (status != ControlChannelTriggerStatus.HardwareSlotAllocated && status != ControlChannelTriggerStatus.SoftwareSlotAllocated)
                    throw new Exception($"Neither hardware nor software slot could be allocated. ChannelStatus is {status.ToString()}");

                CoreApplication.Properties.Remove(channel.ControlChannelTriggerId);

                var appContext = new AppContext(this, socket, channel, channel.ControlChannelTriggerId);
                CoreApplication.Properties.Add(channel.ControlChannelTriggerId, appContext);

                result = PostSocketRead(MAX_BUFFER_LENGTH);

                Diag.DebugPrint("RegisterWithCCTHelper Completed");
            }
            catch (Exception ex)
            {
                Diag.DebugPrint("RegisterWithCCTHelper Task failed with: " + ex.Message);
            }

            return result;
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

            string message = readPacket.ReadString(buffLen);
            Diag.DebugPrint("Received Buffer: " + message);

            InvokeSimpleToast(message);

            AppContext.Enqueue(message);

            PostSocketRead(MAX_BUFFER_LENGTH);
            Diag.DebugPrint("OnDataReadCompletion Exit");
        }

        void InvokeSimpleToast(string message)
        {
            if (message.StartsWith("{"))
            {
                try
                {
                    var notification = JsonConvert.DeserializeObject<Notification>(message);
                    notification.SendToast();
                }
                catch
                {
                    return;
                }
            }
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

        public async void SendMessage(string message)
        {
            if (socket == null)
            {
                Diag.DebugPrint("Please setup connection with the server first.");
                return;
            }
            try
            {
                if (writer == null)
                {
                    writer = new DataWriter(socket.OutputStream);
                }
                Diag.DebugPrint("Sending message to server: " + message);

                writer.UnicodeEncoding = UnicodeEncoding.Utf8;
                writer.WriteString(message);

                await writer.StoreAsync();
            }
            catch (Exception ex)
            {
                Diag.DebugPrint("Failed to write into the streamwebsocket: " + ex.Message);
            }
        }

        public string GetSocketUri()
        {
            return socketUri;
        }

        public ControlChannelTrigger GetChannel()
        {
            return channel;
        }
    }

    public sealed class AppContext
    {
        public StreamWebSocket WebSocketHandle { get; set; }
        public ControlChannelTrigger Channel { get; set; }
        public string ChannelId { get; set; }
        public CommModule CommInstance { get; set; }
        static ConcurrentQueue<string> messageQueue;

        public AppContext(CommModule commInstance, StreamWebSocket webSocket, ControlChannelTrigger channel, string id)
        {
            WebSocketHandle = webSocket;
            Channel = channel;
            ChannelId = id;
            CommInstance = commInstance;
            messageQueue = new ConcurrentQueue<string>();
        }

        public static void Enqueue(string message)
        {
            messageQueue.Enqueue(message);
        }

        public static bool Dequeue(out string message)
        {
            var result = messageQueue.TryDequeue(out message);
            return result;
        }
    }
}
