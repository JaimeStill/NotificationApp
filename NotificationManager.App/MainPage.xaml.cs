using DiagnosticsHelper;
using Newtonsoft.Json;
using NotificationManager.Tasks;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Windows.ApplicationModel.Background;
using Windows.Storage.Streams;
using Windows.UI;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;
using Windows.Web.Http;
using Windows.Web.Http.Headers;

// The Blank Page item template is documented at https://go.microsoft.com/fwlink/?LinkId=402352&clcid=0x409

namespace NotificationManager.App
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainPage : Page
    {
        CoreDispatcher coreDispatcher;
        CommModule commModule;
        bool lockScreenAdded = false;
        TextBlock debugOutput;

        enum connectionStates
        {
            notConnected,
            connecting,
            connected
        }

        static connectionStates connectionState = connectionStates.notConnected;

        const string rootUri = "notificationsocketsapi.azurewebsites.net";
        string socketUri = $"ws://{rootUri}/notifications";
        string triggerUri = $"http://{rootUri}/api/notifications/triggerNotification";
        string sendUri = $"http://{rootUri}/api/notifications/send";

        #region Constructor and Page Events

        public MainPage()
        {
            InitializeComponent();
            coreDispatcher = Diag.coreDispatcher = Window.Current.Dispatcher;
            btnConnection.Visibility = Visibility.Collapsed;
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            debugOutput = Diag.debug = txtDebug;
            ClientInit();
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            Dispose();
        }

        #endregion

        #region Helper Functions

        public void Dispose()
        {
            if (commModule != null)
            {
                commModule.Dispose();
                commModule = null;
            }

            UnregisterBackgroundTasks();
        }

        void SetConnected()
        {
            connectionState = connectionStates.connected;
            btnConnection.Label = "Disconnect";
            btnConnection.IsEnabled = true;
            btnConnection.Icon = new SymbolIcon(Symbol.FourBars);
            btnConnection.Icon.Foreground = new SolidColorBrush(Colors.LimeGreen);
        }

        void SetDisconnected()
        {
            connectionState = connectionStates.notConnected;
            btnConnection.Label = "Connect";
            btnConnection.IsEnabled = true;
            btnConnection.Icon = new SymbolIcon(Symbol.ZeroBars);
            btnConnection.Icon.Foreground = new SolidColorBrush(Colors.Red);
        }

        void SetConnecting()
        {
            connectionState = connectionStates.connecting;
            btnConnection.Label = "Connecting";
            btnConnection.IsEnabled = false;
            btnConnection.Icon = new SymbolIcon(Symbol.Refresh);
            btnConnection.Icon.Foreground = new SolidColorBrush(Colors.DodgerBlue);
        }

        void UnregisterBackgroundTasks()
        {
            foreach (var cur in BackgroundTaskRegistration.AllTasks)
            {
                Diag.DebugPrint("Deleting Background Task " + cur.Value.Name);
                cur.Value.Unregister(true);
            }
        }

        async void SendMessage(string message)
        {
            var notification = new Notification
            {
                title = "Test Socket Toast",
                content = message,
                image = "http://unsplash.it/300/180?random",
                logo = "http://unsplash.it/200/200?random",
                tag = new Random().Next(1001, 1500).ToString(),
                group = "reminders",
                launch = new KeyValuePair<string, string>("uri", $"http://{rootUri}")
            };

            await SendNotification(notification);
        }

        async Task SendNotification(Notification notification)
        {
            using (var client = new HttpClient())
            {
                var message = new HttpRequestMessage(HttpMethod.Post, new Uri(sendUri));
                message.Headers.Accept.Add(new HttpMediaTypeWithQualityHeaderValue("application/json"));
                var content = new HttpStringContent(JsonConvert.SerializeObject(notification), UnicodeEncoding.Utf8, "application/json");
                message.Content = content;
                var result = await client.SendRequestAsync(message);

                Diag.DebugPrint($"SendNotification: Completed with status code {result.StatusCode}: {result.ReasonPhrase}");
            }
        }

        #endregion

        #region Background Task Management

        async void ClientInit()
        {
            commModule = new CommModule();

            if (lockScreenAdded == false)
            {
                BackgroundAccessStatus status = await BackgroundExecutionManager.RequestAccessAsync();
                Diag.DebugPrint("Lock screen status " + status);

                switch (status)
                {
                    case BackgroundAccessStatus.AlwaysAllowed:
                    case BackgroundAccessStatus.AllowedSubjectToSystemPolicy:
                        lockScreenAdded = true;
                        break;
                    case BackgroundAccessStatus.DeniedBySystemPolicy:
                    case BackgroundAccessStatus.DeniedByUser:
                        Diag.DebugPrint("As lockscreen status was Denied, app should switch to polling mode such as email based on time triggers");
                        break;
                }
            }

            btnConnection.Visibility = Visibility.Visible;
            return;
        }

        async void ConfigurePushNotifications()
        {
            try
            {
                if (connectionState == connectionStates.connected)
                {

                    await Task.Factory.StartNew(() =>
                    {
                        commModule.Dispose();
                    });

                    commModule = new CommModule();

                    SetDisconnected();
                }
                else
                {
                    SetConnecting();

                    RegisterNetworkChangeTask();
                    ConfigureToastActions();

                    bool result = await Task.Factory.StartNew(() =>
                    {
                        return commModule.SetupTransport(socketUri);
                    });

                    Diag.DebugPrint("CommModule setup result: " + result);

                    if (result)
                    {
                        ConfigureInvocations();
                        SetConnected();
                    }
                    else
                    {
                        SetDisconnected();
                    }
                }
            }
            catch (Exception ex)
            {
                Diag.DebugPrint("ConfigurePushNotifications failed: " + ex.Message);
                SetDisconnected();
            }
        }

        void ConfigureInvocations()
        {
            commModule.RegisterAction("Send", PushNotification);
        }

        void PushNotification(IList<object> args)
        {
            if (args.Count > 0)
            {
                var notification = JsonConvert.DeserializeObject<Notification>(args[0] as string);
                notification.SendToast();
            }
        }

        void RegisterNetworkChangeTask()
        {
            try
            {
                UnregisterBackgroundTasks();

                var builder = new BackgroundTaskBuilder();
                var trigger = new SystemTrigger(SystemTriggerType.NetworkStateChange, false);
                builder.SetTrigger(trigger);
                builder.TaskEntryPoint = "NotificationManager.Tasks.NetworkChangeTask";
                builder.Name = "NetworkChangeTask";
                var task = builder.Register();
            }
            catch (Exception ex)
            {
                Diag.DebugPrint("Exception caught while setting up system event " + ex.ToString());
            }
        }

        void ConfigureToastActions()
        {
            try
            {
                var trigger = new ToastNotificationActionTrigger();
                var condition = new SystemCondition(SystemConditionType.InternetAvailable);

                var builder = new BackgroundTaskBuilder();
                builder.Name = "ToastTask";
                builder.SetTrigger(trigger);
                builder.AddCondition(condition);

                var task = builder.Register();
            }
            catch (Exception ex)
            {
                Diag.DebugPrint("Exception caught while setting up toast actions " + ex.Message);
            }
        }

        #endregion

        #region App Event Handlers

        private void ConfigureSocket(object sender, RoutedEventArgs e)
        {
            ConfigurePushNotifications();
        }

        private async void TriggerToast(object sender, RoutedEventArgs e)
        {
            try
            {
                Diag.DebugPrint("TriggerToast: Triggering a notification by calling Web API");
                using (var client = new HttpClient())
                {
                    var message = new HttpRequestMessage(HttpMethod.Get, new Uri(triggerUri));
                    var result = await client.SendRequestAsync(message);

                    Diag.DebugPrint($"SendNotification: Completed with status code {result.StatusCode}: {result.ReasonPhrase}");
                }
            }
            catch (Exception ex)
            {
                Diag.DebugPrint("TriggerToast - Error sending request to Web API: " + ex.Message);
            }
        }

        private async void SendToast(object sender, RoutedEventArgs e)
        {
            try
            {
                Diag.DebugPrint("SendToast: Posting a generic notifaction to Web API");
                var notification = new Notification
                {
                    title = "Notification Hub Toast",
                    content = "Toast initiated from the Notification Hub Client",
                    image = "http://unsplash.it/300/180?random",
                    logo = "http://unsplash.it/200/200?random",
                    tag = new Random().Next(501, 1000).ToString(),
                    group = "reminders",
                    launch = new KeyValuePair<string, string>("uri", $"http://{rootUri}")
                };

                await SendNotification(notification);
            }
            catch (Exception ex)
            {
                Diag.DebugPrint("SendToast - Error sending request to Web API: " + ex.Message);
            }
        }

        private void CheckEnter(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == Windows.System.VirtualKey.Enter)
            {
                if (!(string.IsNullOrEmpty(txtMessage.Text)))
                {
                    Diag.DebugPrint("TestSocket: Posting message to Web API and returning a notification");
                    SendMessage(txtMessage.Text);
                }
            }
        }

        private void TestSocket(object sender, RoutedEventArgs e)
        {
            if (!(string.IsNullOrEmpty(txtMessage.Text)))
            {
                Diag.DebugPrint("TestSocket: Posting message to Web API and returning a notification");
                SendMessage(txtMessage.Text);
            }
        }

        private void ClearDiagnostics(object sender, RoutedEventArgs e)
        {
            txtDebug.Text = string.Empty;
            Diag.DebugPrint("Diagnostics successfully cleared...");
        }

        private void ShowBackgroundTasks(object sender, RoutedEventArgs e)
        {
            Diag.DebugPrint("Background Tasks: ");
            foreach (var cur in BackgroundTaskRegistration.AllTasks)
            {
                Diag.DebugPrint($"    {cur.Value.Name}");
            }
        }

        #endregion
    }
}