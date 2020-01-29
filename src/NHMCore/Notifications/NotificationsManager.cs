﻿using NHM.Common;
using System.Collections.Generic;
using System.Linq;

namespace NHMCore.Notifications
{
    public class NotificationsManager : NotifyChangedBase
    {
        public static NotificationsManager Instance { get; } = new NotificationsManager();
        private static readonly object _lock = new object();

        private NotificationsManager()
        {}

        private readonly List<Notification> _notifications = new List<Notification>();

        // TODO must not modify Notifications outside manager
        public List<Notification> Notifications
        {
            get
            {
                lock (_lock)
                {
                    return _notifications;
                }
            }
        }

        public void AddNotificationToList(Notification notification)
        {
            lock (_lock)
            {
                notification.NotificationNew = true;
                _notifications.Insert(0, notification);
                notification.PropertyChanged += Notification_PropertyChanged;
            }
            OnPropertyChanged(nameof(Notifications));
            OnPropertyChanged(nameof(NotificationNewCount));
        }

        private void Notification_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if(nameof(Notification.NotificationNew) == e.PropertyName)
            {
                OnPropertyChanged(nameof(NotificationNewCount));
            }
        }

        public bool RemoveNotificationFromList(Notification notification)
        {
            var ok = false;
            lock (_lock)
            {
                ok = _notifications.Remove(notification);
                notification.PropertyChanged -= Notification_PropertyChanged;
            }
            OnPropertyChanged(nameof(Notifications));
            OnPropertyChanged(nameof(NotificationNewCount));
            return ok;
        }

        // TODO use this instead RemoveNotificationFromList, deterministic keys
        public bool RemoveNotificationFromList(string notificationName)
        {
            var ok = false;
            lock (_lock)
            {
                var removedNotification = _notifications.Where(notification => notification.Name == notificationName).FirstOrDefault();
                ok = _notifications.Remove(removedNotification);
                if (removedNotification != null) removedNotification.PropertyChanged -= Notification_PropertyChanged;
            }
            OnPropertyChanged(nameof(Notifications));
            OnPropertyChanged(nameof(NotificationNewCount));
            return ok;
        }


        private int _notificationNewCount { get; set; }
        public int NotificationNewCount
        {
            get => Instance.Notifications.Where(notif => notif.NotificationNew == true).Count();
            set
            {
                _notificationNewCount = value;
                OnPropertyChanged(nameof(NotificationNewCount));
            }
        }
    }
}