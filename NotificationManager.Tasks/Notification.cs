using Microsoft.QueryStringDotNET;
using Microsoft.Toolkit.Uwp.Notifications;
using System;
using System.Collections.Generic;
using Windows.UI.Notifications;

namespace NotificationManager.Tasks
{
    public sealed class Notification
    {
        public string title { get; set; }
        public string content { get; set; }
        public string image { get; set; }
        public string logo { get; set; }
        public KeyValuePair<string, string> launch { get; set; }
        public string tag { get; set; }
        public string group { get; set; }

        public void SendToast()
        {
            var visual = new ToastVisual()
            {
                BindingGeneric = new ToastBindingGeneric()
                {
                    Children =
                    {
                        new AdaptiveText()
                        {
                            Text = title
                        },
                        new AdaptiveText()
                        {
                            Text = content
                        }
                    }
                }
            };

            if (!(string.IsNullOrEmpty(image)))
            {
                visual.BindingGeneric.Children.Add(new AdaptiveImage() { Source = image });
            }

            if (!(string.IsNullOrEmpty(logo)))
            {
                visual.BindingGeneric.AppLogoOverride = new ToastGenericAppLogo()
                {
                    Source = logo,
                    HintCrop = ToastGenericAppLogoCrop.Circle
                };
            }

            var toastContent = new ToastContent()
            {
                Visual = visual,
                Launch = new QueryString()
                {
                    { launch.Key, launch.Value }
                }.ToString()
            };

            var toast = new ToastNotification(toastContent.GetXml());
            toast.ExpirationTime = DateTime.Now.AddDays(7);
            toast.Tag = tag;
            toast.Group = group;

            ToastNotificationManager.CreateToastNotifier().Show(toast);
        }
    }
}
