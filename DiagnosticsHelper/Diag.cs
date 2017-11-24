using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Windows.UI.Core;
using Windows.UI.Xaml.Controls;

namespace DiagnosticsHelper
{
    public static class Diag
    {
        public static CoreDispatcher coreDispatcher { get; set; }
        public static TextBlock debug { get; set; }
        public static async void DebugPrint(string msg)
        {
            Debug.WriteLine(msg);
            if (coreDispatcher != null)
                await coreDispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                {
                    debug.Text = $"{debug.Text} {DateTime.Now.ToString(@"M/d/yyyy hh:mm:ss tt")} - {msg} \r\n";
                });
        }
    }
}
