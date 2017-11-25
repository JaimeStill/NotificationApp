using DiagnosticsHelper;
using Microsoft.QueryStringDotNET;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Schema;
using System;
using System.IO;
using Windows.ApplicationModel.Background;
using Windows.ApplicationModel.Core;
using Windows.Data.Xml.Dom;
using Windows.Networking.Sockets;
using Windows.UI.Notifications;

namespace NotificationManager.Tasks
{
    public sealed class NetworkChangeTask : IBackgroundTask
    {
        public void Run(IBackgroundTaskInstance taskInstance)
        {
            if (taskInstance == null)
            {
                Diag.DebugPrint("NetworkChangeTask: taskInstance was null");
                return;
            }

            string channelId = "notifications";

            if (CoreApplication.Properties.ContainsKey(channelId))
            {
                try
                {
                    var appContext = CoreApplication.Properties[channelId] as AppContext;

                    if (appContext != null && appContext.CommInstance != null)
                    {
                        CommModule commInstance = appContext.CommInstance;

                        commInstance.Reset();
                        commInstance.SetupTransport(commInstance.socketUri);
                    }
                }
                catch (Exception ex)
                {
                    Diag.DebugPrint("Registering with the RTC broker failed with: " + ex.Message);
                }
            }
            else
            {
                Diag.DebugPrint("Cannot find AppContext key for Notifications");
            }

            Diag.DebugPrint("System Task - " + taskInstance.Task.Name + " finished");
        }
    }

    public sealed class PushNotifyTask : IBackgroundTask
    {
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

        public void Run(IBackgroundTaskInstance taskInstance)
        {
            if (taskInstance == null)
            {
                Diag.DebugPrint("PushNotifyTask: taskInstance was null");
                return;
            }

            Diag.DebugPrint("PushNotifyTask " + taskInstance.Task.Name + " Starting...");

            var channelEventArgs = taskInstance.TriggerDetails as IControlChannelTriggerEventDetails;

            ControlChannelTrigger channel = channelEventArgs.ControlChannelTrigger;

            if (channel == null)
            {
                Diag.DebugPrint("Channel object may have been deleted");
                return;
            }

            string channelId = channel.ControlChannelTriggerId;

            if (CoreApplication.Properties.ContainsKey(channelId))
            {
                try
                {
                    var appContext = CoreApplication.Properties[channelId] as AppContext;

                    // TODO: Update logic for processing messages after Dequque
                    bool result = AppContext.Dequeue(out Message messageReceived);

                    if (result)
                    {
                        Diag.DebugPrint("Message: " + messageReceived.data);
                        InvokeSimpleToast(messageReceived.data);
                    }
                    else
                    {
                        Diag.DebugPrint("There was no message for this push notification.");
                    }
                }
                catch (Exception ex)
                {
                    Diag.DebugPrint("PushNotifyTask failed with: " + ex.Message);
                }

                Diag.DebugPrint("PushNotifyTask " + taskInstance.Task.Name + " finished.");
            }
        }
    }

    public sealed class ToastTask : IBackgroundTask
    {
        BackgroundTaskDeferral deferral = null;

        public async void Run(IBackgroundTaskInstance taskInstance)
        {
            if (taskInstance == null)
            {
                Diag.DebugPrint("ToastTask: taskInstance was null");
                return;
            }

            try
            {
                Diag.DebugPrint("ToastTask " + taskInstance.Task.Name + " Starting...");

                taskInstance.Canceled += new BackgroundTaskCanceledEventHandler((sender, reason) =>
                {
                    deferral.Complete();
                });

                var details = taskInstance.TriggerDetails as ToastNotificationActionTriggerDetail;

                if (details != null)
                {
                    var args = QueryString.Parse(details.Argument);

                    if (args["uri"] != null)
                    {
                        Diag.DebugPrint("ToastTask: Launching URI - " + args["uri"]);
                        await Windows.System.Launcher.LaunchUriAsync(new Uri(args["uri"]));
                    }
                    else
                    {
                        Diag.DebugPrint("ToastTask: No launch URI found for this toast");
                    }
                }
                else
                {
                    Diag.DebugPrint("ToastTask: No Trigger Details found for this task");
                }
            }
            catch (Exception ex)
            {
                Diag.DebugPrint("ToastTask failed with: " + ex.Message);
            }

            Diag.DebugPrint("ToastTask " + taskInstance.Task.Name + " finished.");
        }
    }
}
