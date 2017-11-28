using DiagnosticsHelper;
using System;
using Windows.ApplicationModel.Background;
using Windows.Networking.Sockets;

namespace NotificationManager.Tasks
{
    public static class RegistrationExtensions
    {
        public static ControlChannelTrigger RegisterChannel(this ControlChannelTrigger channel)
        {
            const int serverKeepAliveInterval = 30;
            const string channelId = "notifications";
            Diag.DebugPrint("Create ControlChannelTrigger ...");

            try
            {
                channel = new ControlChannelTrigger(channelId, serverKeepAliveInterval, ControlChannelTriggerResourceType.RequestHardwareSlot);
            }
            catch (UnauthorizedAccessException ex)
            {
                Diag.DebugPrint("Plesase add the app to the lock screen. " + ex.Message);
                return null;
            }

            return channel;
        }

        public static Uri CreateSocketServerUri(this string socketUri)
        {
            try
            {
                var serverSocket = new Uri(socketUri);
                return serverSocket;
            }
            catch (Exception ex)
            {
                Diag.DebugPrint("Error creating URI: " + ex.Message);
                return null;
            }
        }

        public static void RegisterBackgroundTasks(this ControlChannelTrigger channel)
        {
            const string WebSocketKeepAliveTask = "Windows.Networking.Sockets.WebSocketKeepAlive";

            var keepAliveBuilder = new BackgroundTaskBuilder();
            keepAliveBuilder.Name = "KeepaliveTaskForNtofications";
            keepAliveBuilder.TaskEntryPoint = WebSocketKeepAliveTask;
            keepAliveBuilder.SetTrigger(channel.KeepAliveTrigger);
            keepAliveBuilder.Register();

            var pushNotifyBuilder = new BackgroundTaskBuilder();
            pushNotifyBuilder.Name = "PushNotificationTask";
            pushNotifyBuilder.TaskEntryPoint = "NotificationManager.Tasks.PushNotifyTask";
            pushNotifyBuilder.SetTrigger(channel.PushNotificationTrigger);
            pushNotifyBuilder.Register();
        }
    }
}