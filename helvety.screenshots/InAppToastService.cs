using System;
using System.Collections.Generic;

namespace helvety.screenshots
{
    internal enum InAppToastSeverity
    {
        Informational = 0,
        Success = 1,
        Warning = 2,
        Error = 3
    }

    internal readonly record struct InAppToastMessage(string Message, InAppToastSeverity Severity);

    internal static class InAppToastService
    {
        private static readonly object SyncRoot = new();
        private static readonly List<InAppToastMessage> PendingToasts = new();
        private static Action<InAppToastMessage>? _toastRequested;

        internal static event Action<InAppToastMessage>? ToastRequested
        {
            add
            {
                if (value is null)
                {
                    return;
                }

                List<InAppToastMessage>? pendingToastsToDeliver = null;
                lock (SyncRoot)
                {
                    _toastRequested += value;
                    if (PendingToasts.Count > 0)
                    {
                        pendingToastsToDeliver = new List<InAppToastMessage>(PendingToasts);
                        PendingToasts.Clear();
                    }
                }

                if (pendingToastsToDeliver is null)
                {
                    return;
                }

                foreach (var pendingToast in pendingToastsToDeliver)
                {
                    value(pendingToast);
                }
            }
            remove
            {
                if (value is null)
                {
                    return;
                }

                lock (SyncRoot)
                {
                    _toastRequested -= value;
                }
            }
        }

        internal static void Show(string message, InAppToastSeverity severity = InAppToastSeverity.Informational)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return;
            }

            var toastMessage = new InAppToastMessage(message.Trim(), severity);
            Action<InAppToastMessage>? handlers;
            lock (SyncRoot)
            {
                handlers = _toastRequested;
                if (handlers is null)
                {
                    PendingToasts.Add(toastMessage);
                    return;
                }
            }

            handlers.Invoke(toastMessage);
        }
    }
}
